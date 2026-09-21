using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Pinyin;

namespace HanMate.App.Pages;

public sealed class AnnotationPage : ContentPage
{
    private readonly TextDraftStore _store;
    private readonly LocalizationService _language;
    private readonly Action<SavedTextDraft?, bool>? _changed;
    private SavedTextDraft _draft;
    private TextDraft _body;
    private bool _busy, _dirty, _onlyReview, _navigating;
    private int _offset;
    private Shell? _shell;
    private Label _status = new();
    private string? _error;
    private string T(string key) => _language["Library." + key];
    public AnnotationPage(TextDraftStore store, LocalizationService language, SavedTextDraft draft, Action<SavedTextDraft?, bool>? changed = null)
    { _store = store; _language = language; _draft = draft; _body = draft.Body; _changed = changed; Render(); }

    private Button ActionButton(string key, Func<Task> action)
    {
        var button = new Button { Text = T(key), AutomationId = "Annotation." + key };
        button.Clicked += async (_, _) => await RunAsync(action); return button;
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return; _busy = true;
        try { await action(); }
        catch (HanMate.Core.Contracts.RevisionConflictException) { _error = "DraftConflict"; }
        catch (FormatException) { _error = "ToneRequired"; }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e.GetType().Name); _error = e.Message == "EDITOR_AUDIO_TARGET_PROTECTED" ? "AudioProtected" : "DraftFailed"; }
        finally { _busy = false; Render(); }
    }
    private void Render()
    {
        Title = T("ReviewPinyin"); _status = new Label { Text = _error is null ? (_dirty ? T("Unsaved") : T("SavedDraft")) : T(_error) };
        var document = _body.Annotation!; var all = document.TextUnits.SelectMany(u => u.Tokens.Select(t => (Unit: u, Token: t))).Where(x => x.Token.Kind == TokenKind.Hanzi).ToArray();
        var rows = (_onlyReview ? all.Where(x => x.Token.ReviewState != AnnotationReviewState.Confirmed) : all).ToArray();
        if (_offset >= rows.Length) _offset = Math.Max(0, ((rows.Length - 1) / 48) * 48);
        var layout = new VerticalStackLayout { Padding = 16, Spacing = 10 };
        layout.Add(new Label { Text = document.Title, FontSize = 24 }); layout.Add(new Label { Text = T("AnnotationHint") });
        layout.Add(new Label { Text = string.Format(T("ReviewCount"), all.Count(x => x.Token.ReviewState != AnnotationReviewState.Confirmed), all.Length) });
        layout.Add(_status);
        layout.Add(ActionButton("SaveDraft", SaveAsync));
        layout.Add(ActionButton("SavePersonal", CommitAsync));
        if (document.Kind == ContentKind.Grammar)
        {
            var structure = new Button { Text = _language["GrammarEdit.Title"] };
            structure.Clicked += async (_, _) => await RunAsync(async () =>
            {
                await SaveAsync(); _navigating = true;
                try { await Navigation.PushAsync(new GrammarEditorPage(_store, _language, _draft, (updated, finalized) =>
                { if (updated is not null) { _draft = updated; _body = updated.Body; _dirty = false; Render(); } _changed?.Invoke(updated, finalized); })); }
                finally { _navigating = false; }
            });
            layout.Add(structure);
        }
        layout.Add(ActionButton("PreviewContent", async () => { await SaveAsync(); _navigating = true; try { await Navigation.PushAsync(new ReadingPage(new ReadingDocument(document), _language, allowEditing: false)); } finally { _navigating = false; } }));
        if (_body.PreviousAnnotation is { } previous)
            layout.Add(ActionButton("UndoAnnotation", async () => { SetDocument(previous with { ContentRevision = document.ContentRevision + 1, MetadataRevision = document.MetadataRevision + 1, AnnotationRevision = document.AnnotationRevision + 1, UpdatedAtUtc = DateTimeOffset.UtcNow }); await SaveAsync(); }));
        layout.Add(ActionButton("ReviewFilter", () => { _onlyReview = !_onlyReview; _offset = 0; return Task.CompletedTask; }));
        layout.Add(new Label { Text = $"{(_onlyReview ? T("NeedsReviewOnly") : T("AllTokens"))} · {_offset / 48 + 1} / {Math.Max(1, (rows.Length + 47) / 48)}" });
        foreach (var row in rows.Skip(_offset).Take(48))
        {
            var token = row.Token; var button = new Button { Text = $"{token.Text}   {token.Pinyin?.Display ?? "?"}   {T(token.Locked ? "ManualLocked" : token.ReviewState == AnnotationReviewState.Confirmed ? "Confirmed" : "NeedsReview")}", AutomationId = "Annotation.Token." + token.Start };
            var contextStart = Math.Max(0, token.Start - 6); var contextLength = Math.Min(13, row.Unit.ElementBoundariesUtf16.Count - 1 - contextStart);
            layout.Add(new Label { Text = TextElementMap.Slice(row.Unit.Text, row.Unit.ElementBoundariesUtf16, contextStart, contextLength).Replace('\r', ' ').Replace('\n', ' '), FontSize = 16 });
            button.Clicked += async (_, _) => await RunAsync(() => CorrectAsync(token)); layout.Add(button);
        }
        var prior = ActionButton("Previous", () => { _offset = Math.Max(0, _offset - 48); return Task.CompletedTask; }); prior.IsEnabled = _offset > 0;
        var next = ActionButton("Next", () => { _offset += 48; return Task.CompletedTask; }); next.IsEnabled = _offset + 48 < rows.Length;
        layout.Add(new HorizontalStackLayout { Children = { prior, next } });
        layout.Add(ActionButton("SaveCopy", async () =>
        {
            var copy = EditorCommitStore.Copy(document);
            var body = _body with { Annotation = copy, PreviousAnnotation = null, ExpectedContentRevision = 0 };
            _draft = await Task.Run(() => _store.SaveAsync(Guid.NewGuid(), body, 0)); _body = body; _dirty = false; _error = null; _changed?.Invoke(_draft, false);
        }));
        Content = new ScrollView { Content = layout };
    }
    private async Task CorrectAsync(TextToken token)
    {
        var candidates = await Task.Run(() => BundledAnnotationLexicon.Default.Engine.Annotate(token.Text).SelectMany(s => s.Candidates)
            .SelectMany(c => c.Syllables).Distinct().ToArray());
        var labels = candidates.Select(c => $"{c.Display} ({c.Base}{c.Tone})").ToArray();
        var actions = labels.Concat(new[] { T("EnterPinyin"), T("UnknownPinyin") }).ToArray();
        var selected = await DisplayActionSheetAsync(token.Text + " · " + T("CandidateHint"), T("Cancel"), null, actions);
        if (selected is null || selected == T("Cancel")) return;
        string? input = null;
        if (selected == T("EnterPinyin"))
        {
            input = await DisplayPromptAsync(T("EnterPinyin"), T("ToneHint"), T("SaveDraft"), T("Cancel"), initialValue: token.Pinyin is { } p ? p.Base + (p.Erhua ? "r" : "") + p.Tone : "");
            if (input is null) return;
        }
        else if (Array.IndexOf(labels, selected) is var index && index >= 0)
        { var p = candidates[index]; input = p.Base + (p.Erhua ? "r" : "") + p.Tone; }
        SetDocument(DraftAnnotation.Correct(_body.Annotation!, token.Id, input)); await SaveAsync();
    }
    private void SetDocument(ContentDocument document)
    { _body = _body with { PreviousAnnotation = _body.Annotation, Annotation = document, Title = document.Title, Kind = document.Kind,
        Text = EditorCommitStore.PrimaryUnit(document).Text, Pattern = document.Grammar is null ? "" : EditorCommitStore.Pattern(document.Grammar),
        Example = document.TextUnits.FirstOrDefault(u => u.Role == TextUnitRole.Example)?.Text ?? "" }; _dirty = true; _error = null; }
    private async Task SaveAsync()
    {
        if (!_dirty) return;
        _draft = await Task.Run(() => _store.SaveAsync(_draft.Id, _body, _draft.Revision)); _dirty = false; _error = null; _changed?.Invoke(_draft, false);
    }
    private async Task CommitAsync()
    {
        await SaveAsync();
        var count = _body.Annotation!.TextUnits.SelectMany(u => u.Tokens).Count(t => t.Kind == TokenKind.Hanzi && t.ReviewState != AnnotationReviewState.Confirmed);
        if (count > 0 && !await DisplayAlertAsync(T("SavePersonal"), string.Format(T("SaveUnreviewed"), count), T("SavePersonal"), T("Cancel"))) return;
        var store = Handler!.MauiContext!.Services.GetRequiredService<EditorCommitStore>();
        var committed = await Task.Run(() => store.CommitAsync(_draft)); _changed?.Invoke(null, true); _dirty = false; _busy = false;
        // Shell's implicit root can be null in NavigationStack. Keep the Shell
        // navigation owner, not this page's proxy (which detaches on pop).
        var navigation = Shell.Current.Navigation;
        await navigation.PopToRootAsync(false);
        await navigation.PushAsync(new ReadingPage(new ReadingDocument(committed.Document), _language), false);
    }
    protected override bool OnBackButtonPressed() { if (!_busy && !_dirty) return base.OnBackButtonPressed(); _ = RunAsync(SaveAsync); return true; }
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (_shell is not null) { _shell.Navigating -= Leaving; _language.PropertyChanged -= LanguageChanged; _shell = null; }
        if (Handler is not null) { _shell = Shell.Current; if (_shell is not null) _shell.Navigating += Leaving; _language.PropertyChanged += LanguageChanged; }
    }
    private void Leaving(object? sender, ShellNavigatingEventArgs e)
    { if (_shell?.CurrentPage == this && !_navigating && (_busy || _dirty) && e.CanCancel) e.Cancel(); }
    private void LanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (string.IsNullOrEmpty(e.PropertyName) && !_busy) Render(); }
}
