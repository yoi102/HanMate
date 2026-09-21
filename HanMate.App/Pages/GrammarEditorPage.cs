using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Pinyin;

namespace HanMate.App.Pages;

public sealed class GrammarEditorPage : ContentPage
{
    private readonly TextDraftStore _store;
    private readonly LocalizationService _language;
    private readonly Action<SavedTextDraft?, bool>? _changed;
    private SavedTextDraft _draft;
    private GrammarEditInput _input;
    private bool _dirty, _busy, _navigating;
    private int _unitIndex, _partIndex;
    private Shell? _shell;
    private Label _status = new();
    private string T(string key) => _language["GrammarEdit." + key];
    public GrammarEditorPage(TextDraftStore store, LocalizationService language, SavedTextDraft draft, Action<SavedTextDraft?, bool>? changed = null)
    { _store = store; _language = language; _draft = draft; _changed = changed; _input = GrammarEditing.From(draft.Body.Annotation!); Render(); }

    private Button Button(string key, Func<Task> action)
    {
        var button = new Button { Text = T(key), AutomationId = "GrammarEdit." + key };
        button.Clicked += async (_, _) => await RunAsync(action); return button;
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return; _busy = true; if (Content is not null) Content.IsEnabled = false;
        try { await action(); }
        catch (HanMate.Core.Contracts.RevisionConflictException) { _status.Text = _language["Library.DraftConflict"]; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.GetType().Name); _status.Text = T("Invalid"); }
        finally { _busy = false; if (Content is not null) Content.IsEnabled = true; }
    }
    private void Changed() { _dirty = true; _status.Text = T("Unsaved"); }
    private void Render()
    {
        Title = T("Title"); _status = new Label { Text = _dirty ? T("Unsaved") : _language["Library.SavedDraft"] };
        var body = new VerticalStackLayout { Padding = 16, Spacing = 10 };
        body.Add(new Label { Text = T("Hint") }); body.Add(_status);
        body.Add(Button("Save", async () => { await SaveAsync(); }));
        body.Add(Button("DiscardChanges", async () => { if (await DisplayAlertAsync(T("DiscardChanges"), T("DiscardHint"), T("DiscardChanges"), _language["Library.Cancel"])) { _input = GrammarEditing.From(_draft.Body.Annotation!); _dirty = false; Render(); } }));
        body.Add(Button("Review", async () =>
        {
            if (!await SaveAsync()) return;
            _navigating = true;
            try { await Navigation.PushAsync(new AnnotationPage(_store, _language, _draft, (updated, finalized) =>
            {
                if (updated is not null) { _draft = updated; _input = GrammarEditing.From(updated.Body.Annotation!); _dirty = false; Render(); }
                _changed?.Invoke(updated, finalized);
            })); }
            finally { _navigating = false; }
        }));
        body.Add(new Label { Text = T("Name") });
        var title = new Entry { Text = _input.Title, MaxLength = 120, AutomationId = "GrammarEdit.Name" };
        title.TextChanged += (_, e) => { _input = _input with { Title = e.NewTextValue ?? "" }; Changed(); }; body.Add(title);
        body.Add(new Label { Text = T("Pattern"), FontSize = 20 });
        var parts = new Picker { ItemsSource = _input.Parts.Select((p, i) => $"{i + 1}. {T(p.Kind.ToString())}: {p.Text}").ToArray(), SelectedIndex = Math.Min(_partIndex, _input.Parts.Count - 1) };
        parts.SelectedIndexChanged += (_, _) => { if (parts.SelectedIndex >= 0) { _partIndex = parts.SelectedIndex; Render(); } }; body.Add(parts);
        if (_input.Parts.Count > 0)
        {
            _partIndex = Math.Clamp(_partIndex, 0, _input.Parts.Count - 1);
            var part = _input.Parts[_partIndex];
            var kinds = Enum.GetValues<GrammarPatternPartKind>();
            var kind = new Picker { ItemsSource = kinds.Select(k => T(k.ToString())).ToArray(), SelectedIndex = Array.IndexOf(kinds, part.Kind) };
            kind.SelectedIndexChanged += (_, _) => { if (kind.SelectedIndex >= 0) SetPart(_input.Parts[_partIndex] with { Kind = kinds[kind.SelectedIndex] }); }; body.Add(kind);
            var text = new Entry { Text = part.Text, MaxLength = 160, AutomationId = "GrammarEdit.PartText" };
            text.TextChanged += (_, e) => SetPart(_input.Parts[_partIndex] with { Text = e.NewTextValue ?? "" }); body.Add(text);
            body.Add(Button("PartUp", () => { var rows = _input.Parts.ToList(); if (_partIndex > 0) { (rows[_partIndex - 1], rows[_partIndex]) = (rows[_partIndex], rows[_partIndex - 1]); _partIndex--; _input = _input with { Parts = rows }; Changed(); Render(); } return Task.CompletedTask; }));
            body.Add(Button("RemovePart", () => { _input = _input with { Parts = _input.Parts.Where((_, i) => i != _partIndex).ToArray() }; _partIndex = Math.Max(0, _partIndex - 1); Changed(); Render(); return Task.CompletedTask; }));
        }
        body.Add(Button("AddPart", () => { if (_input.Parts.Count >= 40) throw new InvalidDataException(); _input = _input with { Parts = _input.Parts.Append(new GrammarPatternPart { Kind = GrammarPatternPartKind.Literal, Text = "" }).ToArray() }; _partIndex = _input.Parts.Count - 1; Changed(); Render(); return Task.CompletedTask; }));
        Translations(body, _input.Translations, map => { _input = _input with { Translations = map }; Changed(); });
        body.Add(new Label { Text = T("Topics"), FontSize = 20 });
        foreach (var code in new[] { "negation", "question", "comparison", "time", "location", "progressive" }.Concat(_input.Topics).Distinct())
        {
            var label = _language["GrammarEdit.Topic." + code]; if (label == "GrammarEdit.Topic." + code) label = code;
            var check = new CheckBox { IsChecked = _input.Topics.Contains(code) };
            check.CheckedChanged += (_, e) => { _input = _input with { Topics = e.Value ? _input.Topics.Append(code).Distinct().ToArray() : _input.Topics.Where(t => t != code).ToArray() }; Changed(); };
            body.Add(new HorizontalStackLayout { Children = { check, new Label { Text = label, VerticalTextAlignment = TextAlignment.Center } } });
        }
        body.Add(new Label { Text = T("Units"), FontSize = 20 });
        var units = new Picker { ItemsSource = _input.Units.Select((u, i) => $"{i + 1}. {Role(u.Role)} · {new string(u.Text.Take(30).ToArray())}").ToArray(), SelectedIndex = Math.Min(_unitIndex, _input.Units.Count - 1) };
        units.SelectedIndexChanged += (_, _) => { if (units.SelectedIndex >= 0) { _unitIndex = units.SelectedIndex; Render(); } }; body.Add(units);
        if (_input.Units.Count > 0)
        {
            _unitIndex = Math.Clamp(_unitIndex, 0, _input.Units.Count - 1); var unit = _input.Units[_unitIndex];
            body.Add(new Label { Text = Role(unit.Role) });
            var text = new Editor { Text = unit.Text, HeightRequest = 180, AutoSize = EditorAutoSizeOption.Disabled, AutomationId = "GrammarEdit.UnitText" };
            text.TextChanged += (_, e) => SetUnit(_input.Units[_unitIndex] with { Text = e.NewTextValue ?? "" }); body.Add(text);
            Translations(body, unit.Translations, map => SetUnit(_input.Units[_unitIndex] with { Translations = map }));
            body.Add(Button("UnitUp", () =>
            {
                var rows = _input.Units.ToList(); var previous = Enumerable.Range(0, _unitIndex).LastOrDefault(i => rows[i].Role == unit.Role, -1);
                if (previous >= 0) { (rows[previous], rows[_unitIndex]) = (rows[_unitIndex], rows[previous]); _unitIndex = previous; _input = _input with { Units = rows }; Changed(); Render(); }
                return Task.CompletedTask;
            }));
            body.Add(Button("RemoveUnit", async () =>
            {
                if (!await DisplayAlertAsync(T("RemoveUnit"), T("RemoveUnitHint"), T("RemoveUnit"), _language["Library.Cancel"])) return;
                _input = _input with { Units = _input.Units.Where(u => u.Id != unit.Id).ToArray() }; _unitIndex = Math.Max(0, _unitIndex - 1); Changed(); Render();
            }));
        }
        foreach (var role in new[] { TextUnitRole.GrammarExplanation, TextUnitRole.Example, TextUnitRole.GrammarNote })
            body.Add(Button("Add" + role, () => { var max = role == TextUnitRole.Example ? 20 : 10; if (_input.Units.Count(u => u.Role == role) >= max) throw new InvalidDataException(); _input = _input with { Units = _input.Units.Append(new GrammarUnitInput(Guid.NewGuid(), role, "", new Dictionary<string, string>())).ToArray() }; _unitIndex = _input.Units.Count - 1; Changed(); Render(); return Task.CompletedTask; }));
        Content = new ScrollView { Content = body, IsEnabled = !_busy };
    }
    private string Role(TextUnitRole role) => _language["Reader.Role." + role];
    private void SetPart(GrammarPatternPart part) { var rows = _input.Parts.ToArray(); rows[_partIndex] = part; _input = _input with { Parts = rows }; Changed(); }
    private void SetUnit(GrammarUnitInput unit) { var rows = _input.Units.ToArray(); rows[_unitIndex] = unit; _input = _input with { Units = rows }; Changed(); }
    private void Translations(VerticalStackLayout body, IReadOnlyDictionary<string, string> initial, Action<IReadOnlyDictionary<string, string>> changed)
    {
        var values = new Dictionary<string, string>(initial);
        foreach (var language in new[] { "ja", "en" })
        {
            body.Add(new Label { Text = T("Translation." + language) });
            var editor = new Editor { Text = values.GetValueOrDefault(language, ""), HeightRequest = 75, MaxLength = 20000 };
            editor.TextChanged += (_, e) => { if (string.IsNullOrEmpty(e.NewTextValue)) values.Remove(language); else values[language] = e.NewTextValue; changed(new Dictionary<string, string>(values)); };
            body.Add(editor);
        }
    }
    private async Task<bool> SaveAsync()
    {
        if (!_dirty) return true;
        var preview = await Task.Run(() => GrammarEditing.Apply(_draft.Body.Annotation!, _input, BundledAnnotationLexicon.Default.Engine));
        if (preview.UnmappedManualCount > 0 && !await DisplayAlertAsync(T("Save"), string.Format(_language["Library.UnmappedManual"], preview.UnmappedManualCount), T("Save"), _language["Library.Cancel"])) return false;
        var document = preview.Document;
        var body = _draft.Body with { Title = document.Title, Text = EditorCommitStore.PrimaryUnit(document).Text,
            Pattern = EditorCommitStore.Pattern(document.Grammar!), Example = document.TextUnits.First(u => u.Role == TextUnitRole.Example).Text,
            Annotation = document, PreviousAnnotation = _draft.Body.Annotation, StructuredOnly = true };
        _draft = await Task.Run(() => _store.SaveAsync(_draft.Id, body, _draft.Revision)); _input = GrammarEditing.From(document); _dirty = false;
        _changed?.Invoke(_draft, false); Render(); return true;
    }
    protected override bool OnBackButtonPressed() { if (!_dirty && !_busy) return base.OnBackButtonPressed(); _status.Text = T("Unsaved"); return true; }
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged(); if (_shell is not null) _shell.Navigating -= Leaving;
        _shell = Handler is null ? null : Shell.Current; if (_shell is not null) _shell.Navigating += Leaving;
    }
    private void Leaving(object? sender, ShellNavigatingEventArgs args)
    { if (_shell?.CurrentPage == this && !_navigating && (_busy || _dirty) && args.CanCancel) { args.Cancel(); _status.Text = T("Unsaved"); } }
}
