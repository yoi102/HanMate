using System.Text.Json;
using HanMate.App.Controls;
using HanMate.Core.Content;
using HanMate.Core.Localization;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;

namespace HanMate.App.Pages;

public sealed partial class LearningBrowserPage
{
    private readonly ToolbarItem _more = new() { Text = "⋯", Order = ToolbarItemOrder.Primary,
        AutomationId = "Learning.More" };
    private readonly HashSet<Guid> _shareSelected = [];
    private bool _moreReady, _shareSelecting;
    private bool _restoreSelectionPosition;
    private int _visibleRowIndex;
    private ImageButton? _shareAction;
    private Label? _shareCount, _shareSelectionLabel;

    private void EnsureMoreMenu()
    {
        SemanticProperties.SetDescription(_more, Language["DictionaryDetail.More"]);
        if (_moreReady) return;
        _moreReady = true;
        _more.Clicked += async (_, _) => await MoreAsync();
        ToolbarItems.Add(_more);
    }

    private async Task MoreAsync()
    {
        if (Busy) return;
        var add = kind == ContentKind.Word ? Language["WordEditor.NewWord"] : Language["LearningEdit.New"];
        var select = Language["LanShare.SelectTitle"];
        var cancelSelection = Language["LanShare.CancelSelection"];
        var choices = _shareSelecting ? new[] { add, Language["LanShare.SelectPage"], cancelSelection } : [add, select];
        var choice = await DisplayActionSheetAsync(Language["DictionaryDetail.More"], Language["Library.Cancel"], null, choices);
        if (choice == add) await AddContentAsync();
        else if (!_shareSelecting && choice == select)
        {
            _shareSelected.Clear(); _shareSelecting = true; _restoreSelectionPosition = true;
            await RunAsync(ReloadAsync); UpdateShareControls();
        }
        else if (_shareSelecting && choice == cancelSelection)
        {
            _shareSelected.Clear(); _shareSelecting = false; _restoreSelectionPosition = true;
            await RunAsync(ReloadAsync); UpdateShareControls();
        }
        else if (_shareSelecting && choice == Language["LanShare.SelectPage"])
        {
            foreach (var row in _renderedResult?.Items ?? [])
            {
                if (_shareSelected.Count >= 100) break;
                var document = JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!;
                var rights = ContentShareStore.Row(document);
                if (rights.CanShare || rights.CanAttest) _shareSelected.Add(document.Id);
            }
            _restoreSelectionPosition = true;
            await RunAsync(ReloadAsync); UpdateShareControls();
        }
    }

    private async Task AddContentAsync() => await RunAsync(async () =>
    {
        if (Handler?.MauiContext?.Services is not { } services) return;
        if (kind == ContentKind.Word)
            await Navigation.PushAsync(new PersonalWordEditorPage(wordCategory ?? WordCategories.Other, null,
                services.GetRequiredService<PersonalWordStore>(), services.GetRequiredService<CustomWordCategoryStore>(), Language));
        else
            await Navigation.PushAsync(new PersonalLearningEditorPage(kind, null,
                services.GetRequiredService<PersonalLearningStore>(), Language));
    });

    private View CreateShareSelectionCard()
    {
        var checkbox = new CheckBox { AutomationId = "Learning.ShareItem" };
        var body = new VerticalStackLayout { Padding = new Thickness(10, 8), Spacing = 4 };
        var row = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        row.Add(checkbox); row.Add(body, 1);
        var syncing = false;
        row.BindingContextChanged += (_, _) =>
        {
            body.Clear();
            var document = row.BindingContext switch
            {
                WordListItem word => word.Document,
                ContentDocument content => content,
                _ => null
            };
            if (document is null) return;
            if (row.BindingContext is WordListItem wordItem)
            {
                body.Add(new RubyWordView(wordItem.Headword, scale: .85)
                    { InputTransparent = true, CascadeInputTransparent = true });
                if (UiLanguagePolicy.SelectAuxiliaryTranslation(wordItem.Headword.Translations, Language.CurrentLanguage) is { } translation)
                    body.Add(LibraryLayout.Muted(translation));
            }
            else
            {
                body.Add(new Label { Text = document.Title, FontSize = 18, FontAttributes = FontAttributes.Bold });
                var preview = document.Kind == ContentKind.Grammar ? document.Grammar?.PatternParts is { } parts
                    ? string.Join(" ", parts.Select(part => part.Text)) : null
                    : document.TextUnits.FirstOrDefault()?.Text;
                if (!string.IsNullOrWhiteSpace(preview)) body.Add(new Label
                    { Text = preview, FontSize = 14, MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation });
            }
            var rights = ContentShareStore.Row(document);
            var allowed = rights.CanShare || rights.CanAttest;
            if (!allowed) body.Add(LibraryLayout.Muted(Language["LanShare.Restricted"]));
            syncing = true;
            checkbox.IsEnabled = allowed;
            checkbox.IsChecked = _shareSelected.Contains(document.Id);
            syncing = false;
            SemanticProperties.SetDescription(row, document.Title);
            SemanticProperties.SetDescription(checkbox, document.Title);
            SemanticProperties.SetHint(row, allowed ? Language["LanShare.SelectTitle"] : Language["LanShare.Restricted"]);
        };
        checkbox.CheckedChanged += (_, e) =>
        {
            if (syncing) return;
            var document = row.BindingContext switch
            {
                WordListItem word => word.Document,
                ContentDocument content => content,
                _ => null
            };
            if (document is null) return;
            if (e.Value && _shareSelected.Count >= 100 && !_shareSelected.Contains(document.Id))
            {
                syncing = true; checkbox.IsChecked = false; syncing = false;
                Status.Text = Language["LanShare.Limit"]; return;
            }
            if (e.Value) _shareSelected.Add(document.Id); else _shareSelected.Remove(document.Id);
            UpdateShareControls();
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => { if (checkbox.IsEnabled) checkbox.IsChecked = !checkbox.IsChecked; };
        body.GestureRecognizers.Add(tap);
        return LibraryLayout.CollectionRow(row);
    }

    private View CreateShareFloatingButton()
    {
        var icon = new ImageButton
        {
            Source = "share.png", BackgroundColor = Color.FromArgb("#4056A1"),
            CornerRadius = 28, WidthRequest = 56, HeightRequest = 56, Padding = 14,
            AutomationId = "Learning.ShareSelected"
        };
        icon.Clicked += async (_, _) => await ShareSelectedAsync();
        SemanticProperties.SetDescription(icon, Language["LanShare.Share"]);
        var count = new Label
        {
            BackgroundColor = Colors.White, TextColor = Color.FromArgb("#24366E"),
            FontSize = 12, FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(5, 1), HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start, InputTransparent = true
        };
        var floating = new Grid
        {
            WidthRequest = 68, HeightRequest = 68,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 12, 12), ZIndex = 5
        };
        floating.Add(icon); floating.Add(count);
        _shareAction = icon; _shareCount = count;
        UpdateShareControls();
        return floating;
    }

    private void UpdateShareControls()
    {
        if (_shareAction is not null)
        {
            _shareAction.IsEnabled = _shareSelected.Count > 0 && !Busy;
            SemanticProperties.SetDescription(_shareAction,
                Language["LanShare.Share"] + " · " + string.Format(Language["LanShare.Selected"], _shareSelected.Count));
        }
        if (_shareCount is not null) _shareCount.Text = _shareSelected.Count.ToString();
        if (_shareSelectionLabel is not null)
            _shareSelectionLabel.Text = string.Format(Language["LanShare.Selected"], _shareSelected.Count);
    }

    private async Task ShareSelectedAsync()
    {
        if (_shareSelected.Count == 0) { Status.Text = Language["LanShare.SelectFirst"]; return; }
        await RunAsync(async () =>
        {
            if (Handler?.MauiContext?.Services is not { } services) return;
            _shareAction!.IsEnabled = false;
            var succeeded = await LearningShareRoomLauncher.OpenAsync(this, services, Language, _shareSelected.ToArray());
            if (succeeded)
            {
                _shareSelected.Clear(); _shareSelecting = false;
                _restoreSelectionPosition = true;
                _renderedResult = null; _shareAction = null; _shareCount = null; _shareSelectionLabel = null;
            }
            else UpdateShareControls();
        });
        UpdateShareControls();
    }
}
