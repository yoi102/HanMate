using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

public sealed class DraftsPage(TextDraftStore store, LocalizationService language) : DataPage(language)
{
    private int _offset;
    protected override async Task ReloadAsync()
    {
        Status = new Label();
        Title = T("Drafts"); var rows = await Task.Run(() => store.ListAsync(_offset));
        if (rows.Count == 0 && _offset > 0) { _offset = 0; await ReloadAsync(); return; }
        var body = new VerticalStackLayout { Padding = 16, Spacing = 10 };
        body.Add(Button("NewDraft", async () => await Navigation.PushAsync(new TextDraftPage(store, Language))));
        body.Add(Button("ContentTransfer", () => Navigation.PushAsync(ContentTransferPage.Create(Handler!.MauiContext!.Services, Language))));
        body.Add(Button("Trash", () => Navigation.PushAsync(new ContentTrashPage(Handler!.MauiContext!.Services.GetRequiredService<ContentTrashStore>(), Language))));
        body.Add(Button("RetainedContents", () => Navigation.PushAsync(new RetainedContentPage(Handler!.MauiContext!.Services.GetRequiredService<ResourceManagementStore>(), Language))));
        body.Add(new Label { Text = T("DraftHint") }); body.Add(Button("Refresh", ReloadAsync));
        Status.Text = rows.Count == 0 ? T("EmptyDrafts") : ""; body.Add(Status);
        foreach (var row in rows)
        {
            var card = new VerticalStackLayout { Spacing = 6 };
            card.Add(new Label { Text = string.IsNullOrWhiteSpace(row.Body.Title) ? T("Untitled") : row.Body.Title, FontSize = 20 });
            card.Add(new Label { Text = row.Updated.ToLocalTime().ToString("g") });
            card.Add(Button("EditDraft", async () => await Navigation.PushAsync(row.Body.Kind == ContentKind.Grammar && row.Body.StructuredOnly && row.Body.Annotation is not null ? new GrammarEditorPage(store, Language, row) : row.Body.StructuredOnly && row.Body.Annotation is not null ? new AnnotationPage(store, Language, row) : new TextDraftPage(store, Language, row))));
            card.Add(Button("DeleteDraft", async () => { if (await DisplayAlertAsync(T("DeleteDraft"), T("DeleteDraftHint"), T("Delete"), T("Cancel"))) { await Task.Run(() => store.DeleteAsync(row)); await ReloadAsync(); } }));
            body.Add(new Border { Padding = 12, Content = card });
        }
        var previous = Button("Previous", async () => { _offset = Math.Max(0, _offset - 50); await ReloadAsync(); }); previous.IsEnabled = _offset > 0;
        var next = Button("Next", async () => { _offset += 50; await ReloadAsync(); }); next.IsEnabled = rows.Count == 50;
        body.Add(new HorizontalStackLayout { Children = { previous, next } }); Content = new ScrollView { Content = body };
    }
}

public sealed class TextDraftPage : ContentPage
{
    private readonly TextDraftStore _store;
    private readonly LocalizationService _language;
    private readonly Entry _title = new();
    private readonly Editor _text = new() { HeightRequest = 320, AutoSize = EditorAutoSizeOption.Disabled, AutomationId = "Draft.Text" };
    private readonly Picker _kind = new();
    private readonly Entry _pattern = new();
    private readonly Editor _example = new() { HeightRequest = 120 };
    private readonly Button _annotate = new(), _cancelAnnotation = new();
    private TextDraft _extra;
    private CancellationTokenSource? _annotationOperation;
    private bool _annotating, _finalized;
    private readonly Label _status = new(), _hint = new();
    private readonly Button _save = new(), _import = new(), _paste = new(), _cancel = new(), _copy = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private Guid _id;
    private long _revision, _editVersion, _savedVersion;
    private CancellationTokenSource? _debounce, _importOperation;
    private bool _importing;
    private string? _statusKey;
    private void SetStatus(string key) { _statusKey = key; _status.Text = T(key); }
    private Shell? _shell;
    private bool _copyingLanguage, _leaving;
    private string T(string key) => _language["Library." + key];

    public TextDraftPage(TextDraftStore store, LocalizationService language, SavedTextDraft? saved = null)
    {
        _store = store; _language = language; _id = saved?.Id ?? Guid.NewGuid(); _revision = saved?.Revision ?? 0;
        _extra = saved?.Body ?? new("", "");
        _pattern.Text = _extra.Pattern; _example.Text = _extra.Example;
        _title.Text = saved?.Body.Title ?? ""; _text.Text = saved?.Body.Text ?? "";
        _kind.ItemsSource = Enum.GetValues<ContentKind>().Select(KindName).ToArray(); _kind.SelectedIndex = (int)(saved?.Body.Kind ?? ContentKind.Text);
        _title.TextChanged += (_, _) => Edited(); _text.TextChanged += (_, _) => Edited(); _kind.SelectedIndexChanged += (_, _) => Edited();
        _pattern.TextChanged += (_, _) => Edited(); _example.TextChanged += (_, _) => Edited();
        _annotate.Clicked += async (_, _) => await AnnotateAsync(); _cancelAnnotation.Clicked += (_, _) => _annotationOperation?.Cancel();
        _save.Clicked += async (_, _) => await SaveAsync();
        _copy.Clicked += async (_, _) =>
        {
            if (!_copy.IsEnabled) return; _copy.IsEnabled = false;
            await _saveGate.WaitAsync();
            try { var body = Snapshot(); var version = _editVersion; var result = await Task.Run(() => _store.SaveAsync(Guid.NewGuid(), body, 0)); _id = result.Id; _revision = result.Revision; _savedVersion = version; SetStatus("SavedDraft"); }
            catch { SetStatus("DraftFailed"); }
            finally { _saveGate.Release(); _copy.IsEnabled = true; }
        };
        _import.Clicked += async (_, _) => await ImportAsync(); _cancel.Clicked += (_, _) => _importOperation?.Cancel();
        _paste.Clicked += async (_, _) =>
        { try { var text = await Clipboard.Default.GetTextAsync(); if (text is not null) await ReplaceAsync(text); } catch { SetStatus("DraftFailed"); } };
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = 16, Spacing = 10, Children = { _hint, _title, _kind,
            new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = { _import, _paste, _cancel } }, _text, _pattern, _example, _status, _save, _copy, _annotate, _cancelAnnotation } } };
        Copy();
    }
    private string KindName(ContentKind kind) => kind == ContentKind.Grammar ? _language.KindGrammar : _language["Kind." + kind];
    private TextDraft Snapshot() => _extra with { Title = _title.Text ?? "", Text = _text.Text ?? "", Kind = (ContentKind)Math.Max(0, _kind.SelectedIndex), Pattern = _pattern.Text ?? "", Example = _example.Text ?? "" };
    private void Edited()
    {
        if (_copyingLanguage) return;
        _pattern.IsVisible = _example.IsVisible = _kind.SelectedIndex == (int)ContentKind.Grammar;
        _annotationOperation?.Cancel();
        _editVersion++; SetStatus("Unsaved"); _debounce?.Cancel(); _debounce = new(); _ = DebounceAsync(_debounce.Token);
    }
    private async Task DebounceAsync(CancellationToken token)
    { try { await Task.Delay(1000, token); await SaveAsync(); } catch (OperationCanceledException) { } }
    private async Task SaveAsync()
    {
        if (_finalized) return;
        await _saveGate.WaitAsync();
        try
        {
            if (_editVersion == _savedVersion) return;
            var body = Snapshot(); var version = _editVersion;
            var saved = await Task.Run(() => _store.SaveAsync(_id, body, _revision)); _revision = saved.Revision; _savedVersion = version;
            SetStatus(_savedVersion == _editVersion ? "SavedDraft" : "Unsaved");
        }
        catch (HanMate.Core.Contracts.RevisionConflictException) { SetStatus("DraftConflict"); }
        catch { SetStatus("DraftFailed"); }
        finally { _saveGate.Release(); }
    }
    private async Task AnnotateAsync()
    {
        if (_extra.StructuredOnly && _extra.Annotation is not null)
        {
            await Navigation.PushAsync(new GrammarEditorPage(_store, _language, new(_id, _extra, _revision, DateTimeOffset.UtcNow), (updated, finalized) =>
            {
                _finalized = finalized;
                if (updated is not null) { _id = updated.Id; _revision = updated.Revision; _extra = updated.Body; }
            }));
            return;
        }
        if (_annotating || _finalized) return; _annotating = true; _annotate.IsEnabled = false; _cancelAnnotation.IsVisible = true;
        using var operation = new CancellationTokenSource(); _annotationOperation = operation;
        try
        {
            await SaveAsync(); if (_editVersion != _savedVersion) return;
            var body = Snapshot(); var version = _editVersion;
            SetStatus("Annotating");
            var preview = await Task.Run(() => DraftAnnotation.Generate(body, HanMate.Infrastructure.Pinyin.BundledAnnotationLexicon.Default.Engine, operation.Token), operation.Token);
            operation.Token.ThrowIfCancellationRequested(); if (version != _editVersion) return;
            if (preview.UnmappedManualCount > 0 && !await DisplayAlertAsync(T("ReviewPinyin"), string.Format(T("UnmappedManual"), preview.UnmappedManualCount), T("ContinueAnnotation"), T("Cancel"))) return;
            operation.Token.ThrowIfCancellationRequested(); if (version != _editVersion) return;
            await _saveGate.WaitAsync(operation.Token);
            SavedTextDraft saved;
            try
            {
                body = body with { Format = "textDraft.v2", Annotation = preview.Document, PreviousAnnotation = body.Annotation, EngineVersion = HanMate.Infrastructure.Pinyin.BundledAnnotationLexicon.Version };
                saved = await Task.Run(() => _store.SaveAsync(_id, body, _revision, operation.Token));
                _extra = saved.Body; _revision = saved.Revision;
            }
            finally { _saveGate.Release(); }
            if (version != _editVersion) { SetStatus("Unsaved"); return; }
            SetStatus("SavedDraft");
            await Navigation.PushAsync(new AnnotationPage(_store, _language, saved, (updated, finalized) =>
            {
                _finalized = finalized;
                if (updated is not null)
                {
                    _id = updated.Id; _revision = updated.Revision; _extra = updated.Body;
                    _copyingLanguage = true;
                    _title.Text = _extra.Title; _text.Text = _extra.Text; _kind.SelectedIndex = (int)_extra.Kind; _pattern.Text = _extra.Pattern; _example.Text = _extra.Example;
                    _copyingLanguage = false; _savedVersion = _editVersion;
                }
            }));
        }
        catch (OperationCanceledException) { SetStatus("AnnotationCancelled"); }
        catch (HanMate.Core.Contracts.RevisionConflictException) { SetStatus("DraftConflict"); }
        catch (InvalidDataException) { SetStatus("AnnotationInputError"); }
        catch { SetStatus("DraftFailed"); }
        finally { _annotationOperation = null; _annotating = false; _annotate.IsEnabled = true; _cancelAnnotation.IsVisible = false; }
    }
    private async Task ReplaceAsync(string text)
    {
        TextDraftInput.Validate(new(_title.Text ?? "", text));
        if (!string.IsNullOrEmpty(_text.Text) && !await DisplayAlertAsync(T("Replace"), T("ReplaceHint"), T("Replace"), T("Cancel"))) return;
        _text.Text = text;
    }
    private async Task ImportAsync()
    {
        if (_importing) return; _importing = true; _import.IsEnabled = false; _cancel.IsVisible = true;
        using var operation = new CancellationTokenSource(); _importOperation = operation; var startingVersion = _editVersion;
        try
        {
            var selected = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = T("ImportTxt") }); if (selected is null) return;
            await using var input = await selected.OpenReadAsync(); var text = await Task.Run(() => TextDraftInput.ReadAsync(input, operation.Token), operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (_editVersion != startingVersion) { SetStatus("ImportChanged"); return; }
            await ReplaceAsync(text);
        }
        catch (OperationCanceledException) { SetStatus("ImportCancelled"); }
        catch { SetStatus("EncodingError"); }
        finally { _importOperation = null; _importing = false; _import.IsEnabled = true; _cancel.IsVisible = false; }
    }
    private void Copy()
    {
        _copyingLanguage = true;
        var selected = _kind.SelectedIndex; _kind.ItemsSource = Enum.GetValues<ContentKind>().Select(KindName).ToArray(); _kind.SelectedIndex = selected;
        _copyingLanguage = false;
        if (_statusKey is not null) _status.Text = T(_statusKey);
        Title = T("EditDraft"); _title.Placeholder = T("Title"); _text.Placeholder = T("Text"); _hint.Text = T("DraftHint");
        _save.Text = T("SaveDraft"); _copy.Text = T("SaveCopy"); _import.Text = T("ImportTxt"); _paste.Text = T("Paste"); _cancel.Text = T("Cancel"); _cancel.IsVisible = _importing;
        _annotate.Text = T("Annotate"); _cancelAnnotation.Text = T("Cancel"); _cancelAnnotation.IsVisible = _annotating;
        _pattern.Placeholder = T("PatternHint"); _example.Placeholder = T("ExampleHint");
        _pattern.IsVisible = _example.IsVisible = _kind.SelectedIndex == (int)ContentKind.Grammar;
        var simple = !_extra.StructuredOnly;
        _title.IsEnabled = _text.IsEnabled = _kind.IsEnabled = _pattern.IsEnabled = _example.IsEnabled = simple;
        _import.IsVisible = _paste.IsVisible = _save.IsVisible = _copy.IsVisible = simple;
        if (!simple) _annotate.Text = _language["GrammarEdit.Title"];
    }
    protected override void OnAppearing() { base.OnAppearing(); Copy(); }
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (_shell is not null) { _shell.Navigating -= Leaving; _language.PropertyChanged -= LanguageChanged; _shell = null; }
        if (Handler is not null) { _shell = Shell.Current; if (_shell is not null) _shell.Navigating += Leaving; _language.PropertyChanged += LanguageChanged; }
    }
    private void LanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (string.IsNullOrEmpty(e.PropertyName)) Copy(); }
    private async void Leaving(object? sender, ShellNavigatingEventArgs e)
    {
        if (_shell?.CurrentPage != this || _finalized || _editVersion == _savedVersion) return;
        if (_leaving) { if (e.CanCancel) e.Cancel(); return; }
        _leaving = true; var deferral = e.GetDeferral();
        try { await SaveAsync(); if (_editVersion != _savedVersion && e.CanCancel) e.Cancel(); }
        finally { _leaving = false; deferral.Complete(); }
    }
    protected override async void OnDisappearing() { base.OnDisappearing(); _debounce?.Cancel(); _importOperation?.Cancel(); await SaveAsync(); }
    protected override bool OnBackButtonPressed()
    {
        if (_editVersion == _savedVersion) return base.OnBackButtonPressed(); _ = SaveAndBackAsync(); return true;
    }
    private async Task SaveAndBackAsync() { await SaveAsync(); if (_editVersion == _savedVersion) await Navigation.PopAsync(); }
}
