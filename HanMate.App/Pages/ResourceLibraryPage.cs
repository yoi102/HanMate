using System.ComponentModel;
using System.Text.Json;
using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Core.Localization;
using HanMate.Core.Resources;
using HanMate.Infrastructure.Packages;

namespace HanMate.App.Pages;

/// <summary>Strict text resource installation and previewed version updates.</summary>
public sealed class ResourceLibraryPage : ContentPage
{
    private readonly TextResourceInstaller _installer;
    private readonly LocalizationService _language;
    private readonly ContentKind? _kind;
    private readonly Guid? _expectedResource;
    private readonly Button _import = new() { AutomationId = "Resource.Import" };
    private readonly Button _cancel = new() { IsVisible = false };
    private readonly Button _previous = new();
    private readonly Button _next = new();
    private readonly Label _status = new() { LineBreakMode = LineBreakMode.WordWrap };
    private readonly Label _hint = new();
    private readonly CollectionView _items = new() { SelectionMode = SelectionMode.Single, AutomationId = "Resource.Contents" };
    private CancellationTokenSource? _operation;
    private bool _busy;
    private bool _subscribed;
    private bool _reviewing;
    private Shell? _shell;
    private int _offset;

    public ResourceLibraryPage(TextResourceInstaller installer, LocalizationService language, ContentKind? kind = null, Guid? expectedResource = null)
    {
        _installer = installer; _language = language;
        _kind = kind;
        _expectedResource = expectedResource;
        _items.ItemTemplate = new DataTemplate(() =>
        {
            var title = new Label { FontSize = 19 }; title.SetBinding(Label.TextProperty, nameof(ResourceLibraryItem.Title));
            var source = new Label { FontSize = 13 }; source.SetBinding(Label.TextProperty, nameof(ResourceLibraryItem.SourceName));
            return new VerticalStackLayout { Padding = new Thickness(8, 10), Spacing = 4, Children = { title, source } };
        });
        _items.SelectionChanged += OnSelected;
        _import.Clicked += OnImport;
        _cancel.Clicked += (_, _) => _operation?.Cancel();
        _previous.Clicked += async (_, _) => { if (_busy) return; _offset = Math.Max(0, _offset - 50); await RefreshAsync(); };
        _next.Clicked += async (_, _) => { if (_busy) return; _offset += 50; await RefreshAsync(); };
        var actions = new HorizontalStackLayout { Spacing = 10, Children = { _import, _cancel } };
        var paging = new HorizontalStackLayout { Spacing = 10, Children = { _previous, _next } };
        var grid = new Grid { Padding = 20, RowSpacing = 12, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        grid.Add(actions, 0, 0); grid.Add(_hint, 0, 1); grid.Add(_status, 0, 2); grid.Add(_items, 0, 3); grid.Add(paging, 0, 4);
        Content = grid;
        RefreshCopy();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        RefreshCopy();
        await RefreshAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_reviewing) _operation?.Cancel();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is not null && !_subscribed)
        {
            _language.PropertyChanged += OnLanguageChanged; _shell = Shell.Current;
            if (_shell is not null) _shell.Navigated += OnShellNavigated;
            _subscribed = true;
        }
        else if (Handler is null && _subscribed)
        {
            _language.PropertyChanged -= OnLanguageChanged;
            if (_shell is not null) _shell.Navigated -= OnShellNavigated;
            _subscribed = false; _operation?.Cancel();
        }
    }
    private async void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    { if (_shell?.CurrentPage == this) await RefreshAsync(); }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => RefreshCopy();
    private string T(string key) => _language["Install." + key];
    private void RefreshCopy()
    {
        Title = _kind is null ? T("Title") : _kind == ContentKind.Grammar ? _language.KindGrammar : _language["Kind." + _kind];
        _import.Text = T("Pick"); _import.IsVisible = _kind is null; _cancel.Text = T("Cancel");
        _previous.Text = T("Previous"); _next.Text = T("Next");
        _hint.Text = _kind is null ? T("Hint") : _language["Pinyin.StarterReady"];
    }

    private async void OnImport(object? sender, EventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        using var operation = new CancellationTokenSource(); _operation = operation;
        try
        {
            // Let the platform offer all selected files; manifest determines type, including .zip fallback.
            var selected = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = T("Pick") });
            if (selected is null) { _status.Text = T("Cancelled"); return; }
            operation.Token.ThrowIfCancellationRequested();
            _status.Text = T("Validating");
            await using var input = await selected.OpenReadAsync();
            var plan = await Task.Run(() => _installer.PlanAsync(input, operation.Token), operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (_expectedResource is { } expected && expected != plan.ResourceId) throw new PackageException("RESOURCE_WRONG_SELECTION");
            var summary = string.Format(System.Globalization.CultureInfo.CurrentCulture, T("Preview"), plan.Name, plan.Version, plan.Publisher, plan.ContentCount) + "\n\n" + T("Unverified");
            if (plan.Update is { } update)
            {
                summary += "\n\n" + string.Format(T("UpdatePreview"), update.PreviousVersion, plan.Version, update.Added, update.Changed, update.Removed, update.Unchanged, update.Retained);
                if (update.AudioDrafts + update.TeachingExamples > 0) summary += "\n" + string.Format(T("UpdateReferences"), update.AudioDrafts, update.TeachingExamples);
                if (update.PublisherChanged) summary += "\n\n" + T("PublisherChanged");
                if (!update.CanApply) { _status.Text = T("UpdateProtected"); return; }
                // Review the complete paged change list before the final confirmation.
                var review = new ResourceUpdateReviewPage(update, _language);
                _reviewing = true;
                try { await Navigation.PushAsync(review); if (!await review.Decision) { _status.Text = T("Cancelled"); return; } }
                finally { _reviewing = false; }
                operation.Token.ThrowIfCancellationRequested();
            }
            if (!await DisplayAlertAsync(T("ConfirmTitle"), summary, T("Confirm"), T("Cancel")))
            { _status.Text = T("Cancelled"); return; }
            operation.Token.ThrowIfCancellationRequested(); _status.Text = T("Saving");
            var result = await Task.Run(() => _installer.InstallAsync(plan, operation.Token), operation.Token);
            _offset = 0;
            await LoadRowsAsync();
            _status.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, T(result.AlreadyInstalled ? "AlreadyInstalled" : plan.Update is not null ? "Updated" : "Installed"), result.ContentCount);
        }
        catch (OperationCanceledException) { _status.Text = T("Cancelled"); }
        catch (PackageException exception) { _status.Text = ErrorText(exception.Code) + " (" + exception.Code + ")"; }
        catch (Exception) { _status.Text = T("Failed"); }
        finally { _operation = null; SetBusy(false); }
    }

    private string ErrorText(string code) => code switch
    {
        "RESOURCE_AUDIO_NOT_SUPPORTED" or "PACKAGE_FILES_UNSUPPORTED" => T("TextOnly"),
        "RESOURCE_UPDATE_NOT_SUPPORTED" or "RESOURCE_REINSTALL_NOT_SUPPORTED" => T("UpdateUnavailable"),
        "RESOURCE_UPDATE_REFERENCES_BLOCKED" => T("UpdateProtected"),
        "RESOURCE_DOWNGRADE_REJECTED" => T("Downgrade"),
        "RESOURCE_UPDATE_REQUIRES_INSTALLED" => T("UpdateMissing"),
        "RESOURCE_WRONG_SELECTION" or "RESOURCE_KIND_MISMATCH" => T("WrongResource"),
        "PLAN_STALE" => T("Stale"),
        "RESOURCE_VERSION_COLLISION" or "RESOURCE_CONTENT_CONFLICT" or "RESOURCE_ID_RESERVED" => T("Conflict"),
        "PACKAGE_LIMIT_EXCEEDED" or "RESOURCE_LIMIT_EXCEEDED" => T("TooLarge"),
        _ => T("Invalid")
    };

    private void SetBusy(bool value)
    {
        _busy = value; _import.IsEnabled = !value; _cancel.IsVisible = value;
        _items.IsEnabled = !value; _previous.IsEnabled = !value && _offset > 0;
        _next.IsEnabled = !value && (_items.ItemsSource as IReadOnlyList<ResourceLibraryItem>)?.Count == 50;
    }

    private async Task RefreshAsync()
    {
        if (_busy) return; SetBusy(true);
        try { await LoadRowsAsync(); }
        catch (Exception) { _status.Text = T("Failed"); }
        finally { SetBusy(false); }
    }

    private async Task LoadRowsAsync()
    {
        var rows = await Task.Run(() => _installer.GetLibraryAsync(offset: _offset, kind: _kind, resourceKind: _kind is null ? null : ResourceKind.Learning));
        _items.ItemsSource = rows;
        _status.Text = rows.Count == 0 ? T("Empty") : string.Format(System.Globalization.CultureInfo.CurrentCulture, T("Page"), _offset + 1, _offset + rows.Count);
    }

    private async void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_busy || e.CurrentSelection.FirstOrDefault() is not ResourceLibraryItem item) return;
        _items.SelectedItem = null; SetBusy(true);
        try
        {
            using var operation = new CancellationTokenSource(); _operation = operation;
            try
            {
                var reading = await Task.Run(() => new HanMate.Core.Reading.ReadingDocument(
                    JsonSerializer.Deserialize<ContentDocument>(item.BodyJson, ContentJson.Options) ?? throw new InvalidDataException()), operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                await Navigation.PushAsync(new ReadingPage(reading, _language));
            }
            finally { _operation = null; }
        }
        catch (OperationCanceledException) { _status.Text = T("Cancelled"); }
        catch (Exception) { _status.Text = T("Failed"); }
        finally { SetBusy(false); }
    }
}
