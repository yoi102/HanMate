using HanMate.App.Localization;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Pinyin;

namespace HanMate.App.Pages;

public sealed class PersonalWordEditorPage : ContentPage
{
    private readonly string _category;
    private readonly ContentDocument? _existing;
    private readonly LocalizationService _language;
    private readonly PersonalWordStore _store;
    private readonly CustomWordCategoryStore _categoriesStore;
    private readonly Page? _sourceDetail;
    private IReadOnlyList<string> _categoryIds = [];
    private readonly Entry _word = new() { MaxLength = 64, AutomationId = "WordEditor.Word" };
    private readonly Editor _definition = new() { MaxLength = 2000, AutoSize = EditorAutoSizeOption.TextChanges, AutomationId = "WordEditor.Definition" };
    private sealed record ExampleField(Editor Input, Guid? UnitId, View Row);
    private readonly List<ExampleField> _examples = [];
    private int _nextExampleIndex;
    private readonly VerticalStackLayout _examplesBody = new() { Spacing = 8 };
    private readonly Button _addExample = new() { AutomationId = "WordEditor.AddExample" };
    private readonly Entry _pinyin = new() { AutomationId = "WordEditor.Pinyin", IsEnabled = false };
    private readonly Label _pinyinHint = new();
    private TextUnit? _generatedHead;
    private string _generatedWord = "";
    private long _pinyinVersion;
    private readonly Entry _english = new() { MaxLength = 1000, AutomationId = "WordEditor.English" };
    private readonly Entry _japanese = new() { MaxLength = 1000, AutomationId = "WordEditor.Japanese" };
    private readonly Picker _voice = new() { AutomationId = "WordEditor.Voice" };
    private readonly Picker _categoryPicker = new() { AutomationId = "WordEditor.Category" };
    private readonly Label _status = new();
    private bool _saving;

    public PersonalWordEditorPage(string category, ContentDocument? existing, PersonalWordStore store,
        CustomWordCategoryStore categoriesStore, LocalizationService language, Page? sourceDetail = null)
    {
        _category = category; _existing = existing; _store = store; _categoriesStore = categoriesStore; _language = language;
        _sourceDetail = sourceDetail;
        Title = T(existing is null ? "NewWord" : "EditWord");
        _word.Placeholder = T("Word"); _definition.Placeholder = T("Definition");
        _pinyin.Placeholder = T("Pinyin"); _pinyinHint.Text = T("PinyinHint");
        _english.Placeholder = T("English"); _japanese.Placeholder = T("Japanese");
        _voice.Title = T("Voice"); _voice.ItemsSource = new[] { T("Automatic"), T("OwnRecording") };
        _categoryPicker.Title = T("Category");
        if (existing is not null)
        {
            var head = existing.TextUnits.Single(x => x.Role == TextUnitRole.Headword);
            _word.Text = head.Text;
            _definition.Text = string.Join("\n\n", existing.TextUnits.Where(x => x.Role == TextUnitRole.Definition)
                .Select(x => x.Text));
            foreach (var example in existing.TextUnits.Where(x => x.Role == TextUnitRole.Example)) AddExample(example.Text, example.Id);
            _english.Text = head.Translations.GetValueOrDefault("en");
            _japanese.Text = head.Translations.GetValueOrDefault("ja");
        }
        if (_examples.Count == 0) AddExample();
        _addExample.Text = T("AddExample"); _addExample.Clicked += (_, _) => AddExample();
        _word.TextChanged += (_, _) => _ = RefreshPinyinAsync();
        _voice.SelectedIndex = existing?.Origin == ContentOrigin.Personal &&
            !existing.Scenes.Contains(PersonalWordStore.AutoVoiceScene) ? 1 : 0;
        var save = new Button { Text = _language["Library.Save"], AutomationId = "WordEditor.Save" };
        save.Clicked += async (_, _) => await SaveAsync(save);
        var body = new VerticalStackLayout { Padding = 20, Spacing = 12, MaximumWidthRequest = 720,
            Children = { new Label { Text = Title, FontSize = 24, FontAttributes = FontAttributes.Bold },
                _word, new Label { Text = T("Pinyin"), FontAttributes = FontAttributes.Bold }, _pinyin, _pinyinHint,
                _definition, _examplesBody, _addExample, _english, _japanese, _categoryPicker, _voice,
                new Label { Text = T("RecordingHint") }, save, _status } };
        Content = new ScrollView { Content = body };
    }

    private string T(string key) => _language["WordEditor." + key];

    private void AddExample(string text = "", Guid? unitId = null)
    {
        if (_examples.Count >= 20) return;
        var index = _nextExampleIndex++;
        var input = new Editor { Text = text, Placeholder = T("Example"), MaxLength = 2000,
            AutoSize = EditorAutoSizeOption.TextChanges, AutomationId = "WordEditor.Example." + index };
        var remove = new Button { Text = T("RemoveExample"), AutomationId = "WordEditor.RemoveExample." + index };
        var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        row.Add(input); row.Add(remove, 1);
        var field = new ExampleField(input, unitId, row);
        remove.Clicked += (_, _) => { _examples.Remove(field); _examplesBody.Remove(row); _addExample.IsEnabled = true; };
        _examples.Add(field); _examplesBody.Add(row); _addExample.IsEnabled = _examples.Count < 20;
    }

    private async Task RefreshPinyinAsync(bool immediate = false)
    {
        var word = (_word.Text ?? "").Trim();
        if (word == _generatedWord) return;
        var version = ++_pinyinVersion;
        _pinyin.IsEnabled = false;
        try
        {
            if (!immediate) await Task.Delay(250);
            if (version != _pinyinVersion) return;
            if (word.Length == 0)
            { _generatedWord = ""; _generatedHead = null; _pinyin.Text = ""; return; }
            var existingHead = _existing?.TextUnits.Single(x => x.Role == TextUnitRole.Headword);
            var head = existingHead?.Text == word ? existingHead : await Task.Run(() =>
                DraftAnnotation.Generate(new TextDraft(word, word, ContentKind.Word),
                    BundledAnnotationLexicon.Default.Engine).Document.TextUnits[0]);
            if (version != _pinyinVersion || (_word.Text ?? "").Trim() != word) return;
            _generatedWord = word; _generatedHead = head;
            _pinyin.Text = string.Join(' ', head.Tokens.Where(x => x.Kind == TokenKind.Hanzi)
                .Select(x => x.Pinyin?.Display ?? "?"));
            _pinyin.IsEnabled = true;
        }
        catch (Exception e)
        { System.Diagnostics.Debug.WriteLine(e.GetType().Name); if (version == _pinyinVersion) _status.Text = T("PinyinInvalid"); }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var categories = await _categoriesStore.ListAsync();
            var builtIn = (await _categoriesStore.ListBuiltInAsync()).ToDictionary(x => x.Id);
            var visibleBuiltIn = WordCategories.All.Where(x => builtIn.GetValueOrDefault(x)?.Hidden != true || x == _category).ToArray();
            _categoryIds = [WordCategories.Other, .. visibleBuiltIn, .. categories.Select(x => x.Id)];
            _categoryPicker.ItemsSource = new[] { builtIn.GetValueOrDefault(WordCategories.Other)?.Name ?? _language["WordCategories.other"] }
                .Concat(visibleBuiltIn.Select(x => builtIn.GetValueOrDefault(x)?.Name ?? _language["WordCategories." + x]))
                .Concat(categories.Select(x => x.Name)).ToArray();
            _categoryPicker.SelectedIndex = Math.Max(0, _categoryIds.ToList().IndexOf(_category));
            await RefreshPinyinAsync(immediate: true);
        }
        catch { _status.Text = _language["Library.Failed"]; }
    }

    private async Task SaveAsync(Button save)
    {
        if (_saving) return;
        _saving = true; save.IsEnabled = false; _status.Text = "";
        try
        {
            var recording = _voice.SelectedIndex == 1;
            var category = _categoryPicker.SelectedIndex >= 0 && _categoryPicker.SelectedIndex < _categoryIds.Count
                ? _categoryIds[_categoryPicker.SelectedIndex] : _category;
            if (_generatedWord != (_word.Text ?? "").Trim()) await RefreshPinyinAsync(immediate: true);
            if (_generatedHead is null || _generatedWord != (_word.Text ?? "").Trim()) throw new InvalidDataException("WORD_PINYIN_STALE");
            var hanzi = _generatedHead.Tokens.Where(x => x.Kind == TokenKind.Hanzi).ToArray();
            var readings = (_pinyin.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (hanzi.Length != readings.Length) throw new InvalidDataException("WORD_PINYIN_COUNT");
            var corrections = hanzi.Zip(readings, (token, reading) => new WordPinyinCorrection(token.Start,
                reading == "?" ? null : reading)).ToArray();
            var input = new PersonalWordInput(category, _word.Text ?? "", _definition.Text ?? "",
                _examples.Select(x => new PersonalWordExample(x.Input.Text ?? "", x.UnitId)).ToArray(),
                _english.Text ?? "", _japanese.Text ?? "", recording) { PinyinCorrections = corrections };
            var services = Handler?.MauiContext?.Services;
            var navigation = Shell.Current.Navigation;
            var document = await Task.Run(() => _store.SaveAsync(input,
                _existing?.Origin == ContentOrigin.Personal ? _existing.Id : null,
                replaceResourceId: _existing?.Origin == ContentOrigin.Resource ? _existing.Id : null));
            await LearningEditorNavigation.FinishAsync(this, _sourceDetail, document, _language, services);
            if (recording && services is not null)
                await navigation.PushAsync(new AudioTracksPage(document.TextUnits.Single(x => x.Role == TextUnitRole.Headword).Id,
                    services.GetRequiredService<LocalAudioStore>(), services.GetRequiredService<PlaybackCoordinator>(),
                    services.GetRequiredService<SqliteContentDocumentStore>(), _language));
        }
        catch (InvalidDataException e) when (e.Message.StartsWith("WORD_PINYIN_", StringComparison.Ordinal))
        { _status.Text = T("PinyinInvalid"); }
        catch (InvalidDataException) { _status.Text = T("Invalid"); }
        catch (FormatException) { _status.Text = T("PinyinInvalid"); }
        catch (InvalidOperationException e) when (e.Message == "EDITOR_AUDIO_TARGET_PROTECTED")
        { _status.Text = T("RecordedExampleProtected"); }
        catch (HanMate.Core.Contracts.RevisionConflictException) { _status.Text = _language["Library.Stale"]; }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e.GetType().Name); _status.Text = _language["Library.Failed"]; }
        finally { _saving = false; save.IsEnabled = true; }
    }
}
