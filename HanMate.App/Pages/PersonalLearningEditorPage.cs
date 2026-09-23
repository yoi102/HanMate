using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

public sealed class PersonalLearningEditorPage : ContentPage
{
    private readonly ContentKind _kind;
    private readonly ContentDocument? _existing;
    private readonly PersonalLearningStore _store;
    private readonly LocalizationService _language;
    private readonly Page? _sourceDetail;
    private readonly Entry _title = new() { MaxLength = 120, AutomationId = "LearningEdit.Title" };
    private readonly Entry _author = new() { MaxLength = 120, AutomationId = "LearningEdit.Author" };
    private readonly Entry _pattern = new() { AutomationId = "LearningEdit.Pattern" };
    private readonly Editor _patternEnglish = new() { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 50 };
    private readonly Editor _patternJapanese = new() { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 50 };
    private readonly Editor _body = new() { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 240,
        AutomationId = "LearningEdit.Body" };
    private readonly Editor _english = new() { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 60 };
    private readonly Editor _japanese = new() { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 60 };
    private readonly VerticalStackLayout _grammarRows = new() { Spacing = 12 };
    private readonly List<UnitField> _units = [];
    private readonly Label _status = new();
    private bool _saving;
    private sealed record UnitField(Guid Id, TextUnitRole Role, Editor Text, Editor English, Editor Japanese, View Row);

    public PersonalLearningEditorPage(ContentKind kind, ContentDocument? existing, PersonalLearningStore store,
        LocalizationService language, Page? sourceDetail = null)
    {
        if (kind is not (ContentKind.Text or ContentKind.Poem or ContentKind.Grammar) ||
            existing is not null && existing.Kind != kind) throw new ArgumentException("Unsupported learning kind.");
        _kind = kind; _existing = existing; _store = store; _language = language; _sourceDetail = sourceDetail;
        Title = T(existing is null ? "New" : "Edit");
        _title.Placeholder = T("Title"); _title.Text = existing?.Title;
        _author.Placeholder = T("Author");
        _author.Text = existing?.Kind == ContentKind.Poem && existing.Source.AuthorProvider != "User"
            ? existing.Source.AuthorProvider : "";
        _pattern.Placeholder = T("PatternHint");
        _pattern.Text = existing?.Grammar is { } grammar ? EditorCommitStore.Pattern(grammar) : "";
        _patternEnglish.Placeholder = T("English");
        _patternJapanese.Placeholder = T("Japanese");
        _patternEnglish.Text = existing?.Grammar?.PatternTranslations.GetValueOrDefault("en");
        _patternJapanese.Text = existing?.Grammar?.PatternTranslations.GetValueOrDefault("ja");
        _body.Placeholder = T(kind == ContentKind.Poem ? "PoemBody" : "TextBody");
        _body.Text = existing?.TextUnits.SingleOrDefault(u => u.Role == TextUnitRole.Body)?.Text;
        var bodyUnit = existing?.TextUnits.SingleOrDefault(u => u.Role == TextUnitRole.Body);
        _english.Text = bodyUnit?.Translations.GetValueOrDefault("en");
        _japanese.Text = bodyUnit?.Translations.GetValueOrDefault("ja");
        var layout = new VerticalStackLayout { Padding = 20, Spacing = 12, MaximumWidthRequest = 760 };
        layout.Add(new Label { Text = Title, FontSize = 24, FontAttributes = FontAttributes.Bold });
        layout.Add(_title);
        if (kind == ContentKind.Poem) layout.Add(_author);
        if (kind == ContentKind.Grammar)
        {
            layout.Add(new Label { Text = T("PatternHelp") }); layout.Add(_pattern);
            layout.Add(_patternEnglish); layout.Add(_patternJapanese);
            if (existing is not null)
                foreach (var unit in existing.TextUnits.Where(u => u.Role is TextUnitRole.GrammarExplanation or TextUnitRole.Example or TextUnitRole.GrammarNote))
                    AddUnit(unit.Role, unit);
            if (_units.All(u => u.Role != TextUnitRole.GrammarExplanation)) AddUnit(TextUnitRole.GrammarExplanation);
            if (_units.All(u => u.Role != TextUnitRole.Example)) AddUnit(TextUnitRole.Example);
            layout.Add(_grammarRows);
            foreach (var role in new[] { TextUnitRole.GrammarExplanation, TextUnitRole.Example, TextUnitRole.GrammarNote })
            {
                var add = new Button { Text = T(role switch { TextUnitRole.Example => "AddExample",
                    TextUnitRole.GrammarNote => "AddNote", _ => "AddExplanation" }) };
                add.Clicked += (_, _) => AddUnit(role);
                layout.Add(add);
            }
        }
        else
        {
            layout.Add(_body);
            layout.Add(new Label { Text = T("English") }); layout.Add(_english);
            layout.Add(new Label { Text = T("Japanese") }); layout.Add(_japanese);
        }
        var save = new Button { Text = _language["Library.Save"], AutomationId = "LearningEdit.Save" };
        save.Clicked += async (_, _) => await SaveAsync(save);
        layout.Add(save); layout.Add(_status);
        Content = new ScrollView { Content = layout };
    }

    private string T(string key) => _language["LearningEdit." + key];

    private void AddUnit(TextUnitRole role, TextUnit? previous = null)
    {
        if (_units.Count >= 40) return;
        var field = new Editor { Text = previous?.Text, AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = role == TextUnitRole.Example ? 90 : 150,
            Placeholder = T(role switch { TextUnitRole.Example => "Example", TextUnitRole.GrammarNote => "Note", _ => "Explanation" }),
            AutomationId = "LearningEdit.Unit." + role + "." + _units.Count };
        var en = new Editor { Text = previous?.Translations.GetValueOrDefault("en"),
            Placeholder = T("English"), AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 50 };
        var ja = new Editor { Text = previous?.Translations.GetValueOrDefault("ja"),
            Placeholder = T("Japanese"), AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 50 };
        var row = new VerticalStackLayout { Spacing = 6 };
        row.Add(new Label { Text = field.Placeholder, FontAttributes = FontAttributes.Bold });
        row.Add(field); row.Add(en); row.Add(ja);
        var remove = new Button { Text = T("Remove") };
        row.Add(remove);
        var item = new UnitField(previous?.Id ?? Guid.NewGuid(), role, field, en, ja, row);
        remove.Clicked += (_, _) =>
        {
            if (role is TextUnitRole.Example or TextUnitRole.GrammarExplanation && _units.Count(u => u.Role == role) <= 1)
            { _status.Text = T("Required"); return; }
            _units.Remove(item); _grammarRows.Remove(row);
        };
        _units.Add(item); _grammarRows.Add(row);
    }

    private async Task SaveAsync(Button button)
    {
        if (_saving) return;
        _saving = true; button.IsEnabled = false; _status.Text = "";
        try
        {
            var input = new PersonalLearningInput(_kind, _title.Text ?? "", _body.Text ?? "", _author.Text ?? "",
                _pattern.Text ?? "", _units.Select(u => new LearningUnitInput(u.Id, u.Role, u.Text.Text ?? "",
                    u.English.Text ?? "", u.Japanese.Text ?? "")).ToArray(), _english.Text ?? "", _japanese.Text ?? "",
                _patternEnglish.Text ?? "", _patternJapanese.Text ?? "");
            var document = await Task.Run(() => _store.SaveAsync(input, _existing));
            await LearningEditorNavigation.FinishAsync(this, _sourceDetail, document, _language, Handler?.MauiContext?.Services);
        }
        catch (HanMate.Core.Contracts.RevisionConflictException) { _status.Text = _language["Library.Stale"]; }
        catch (Exception error) when (error is InvalidDataException or HanMate.Infrastructure.Content.ContentDocumentValidationException or FormatException)
        { _status.Text = T("Invalid"); }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); _status.Text = _language["Library.Failed"]; }
        finally { _saving = false; button.IsEnabled = true; }
    }
}
