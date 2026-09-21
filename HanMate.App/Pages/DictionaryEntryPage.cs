using System.ComponentModel;
using System.Collections.ObjectModel;
using HanMate.App.Audio;
using HanMate.App.Controls;
using HanMate.App.Localization;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Localization;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Dictionary;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

/// <summary>Dictionary presentation is a disposable view, never an edit to publisher or user content.</summary>
public sealed class DictionaryEntryPage : ContentPage
{
    private readonly ContentDocument _document;
    private readonly LocalizationService _language;
    private readonly IServiceProvider _services;
    private readonly bool _allowEditing;
    private readonly bool _learningContent;
    private readonly CollectionView _list = new() { SelectionMode = SelectionMode.None, AutomationId = "Dictionary.Sections" };
    private readonly Guid _owner = Guid.NewGuid();
    private readonly ToolbarItem _pinyin = new() { AutomationId = "Dictionary.Pinyin" };
    private readonly ToolbarItem _more = new() { Text = "⋯", AutomationId = "Dictionary.More" };
    private readonly Label _status = new() { FontSize = 13, IsVisible = false, Margin = new Thickness(24, 8), AutomationId = "Dictionary.Status" };
    private readonly Button _voiceSettings = new() { IsVisible = false, AutomationId = "Dictionary.VoiceSettings" };
    private DictionaryDetail? _detail;
    private CancellationTokenSource? _lifetime;
    private PlaybackCoordinator? _playback;
    private bool _active, _showPinyin, _opening, _expandedDefinitions;
    private long _generation;
    private long _speechRequest;
    private ImageButton? _favorite;
    private DictionaryBookmark? _bookmark;
    private bool _favoriteReady, _isFavorite, _savingFavorite;
    private readonly ObservableCollection<Block> _blocks = [];
    private readonly List<Block> _extraDefinitions = [];
    private Block? _definitionToggle;
    private string T(string key) => _language["DictionaryDetail." + key];
    private string PageTitle => _learningContent ? _language["LearningWords.Meaning"] : T("Title");
    internal sealed class Block(string heading, IReadOnlyList<RubyAtom> atoms, IReadOnlySet<int>? highlight, bool pinyin, Action? expand = null, DictionarySpeechTarget? speech = null, string? translation = null) : INotifyPropertyChanged
    {
        public string Heading
        {
            get => heading;
            set { if (heading == value) return; heading = value; PropertyChanged?.Invoke(this, new(nameof(Heading))); }
        }
        public IReadOnlyList<RubyAtom> Atoms { get; } = atoms;
        public IReadOnlySet<int>? Highlight { get; } = highlight;
        public Action? Expand { get; } = expand;
        public DictionarySpeechTarget? Speech { get; } = speech;
        public string? Translation { get; } = translation;
        public bool Pinyin
        {
            get => pinyin;
            set { if (pinyin == value) return; pinyin = value; PropertyChanged?.Invoke(this, new(nameof(Pinyin))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public DictionaryEntryPage(ContentDocument document, LocalizationService language, IServiceProvider services, bool allowEditing = false, bool learningContent = false)
    {
        _document = document; _language = language; _services = services ?? throw new ArgumentNullException(nameof(services)); _allowEditing = allowEditing; _learningContent = learningContent;
        _showPinyin = Preferences.Default.Get("dictionary.show-pinyin", true);
        ToolbarItems.Add(_pinyin); ToolbarItems.Add(_more);
        _pinyin.Clicked += (_, _) => TogglePinyin();
        _more.Clicked += async (_, _) => await MoreAsync();
        _list.ItemTemplate = new DataTemplate(() => new BlockView(SpeakPassageAsync, () => T("SpeakPassage")));
        _list.ItemsSource = _blocks;
        _list.ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepScrollOffset;
        _list.EmptyView = new ActivityIndicator { IsRunning = true, Margin = 30 };
        _voiceSettings.Clicked += async (_, _) =>
        {
            if (!_active || _opening) return;
            _opening = true;
            try
            {
                var services = _services;
                await Navigation.PushAsync(ActivatorUtilities.CreateInstance<SpeechSettingsPage>(services));
            }
            finally { _opening = false; }
        };
        var layout = new Grid { RowDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)] };
        layout.Add(_list, 0, 0); layout.Add(_status, 0, 1); layout.Add(_voiceSettings, 0, 2);
        Content = layout;
    }
    public static Page Create(ContentDocument document, LocalizationService language, bool allowEditing, IServiceProvider services) => document.Kind == ContentKind.Word
        ? new DictionaryEntryPage(document, language, services, allowEditing)
        : new ReadingPage(new ReadingDocument(document), language, allowEditing: allowEditing);

    internal static async Task PrepareAsync(OfflineSearchStore store, TextUnit? firstHeadword = null)
    {
        try
        {
            await Task.Run(async () =>
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                await store.PrepareAsync().ConfigureAwait(false);
                HanMate.Infrastructure.Pinyin.DictionaryDisplayPinyin.Prepare();
                // Warm the real lookup/projection path while the user is browsing pinyin.
                // This is a disposable view only; it never installs/saves a dictionary entry.
                if (firstHeadword is not null && (await store.FindEntryAsync(firstHeadword).ConfigureAwait(false)).Document is { } document)
                    DictionaryDetailProjection.Create(document);
                SpeechDiagnostics.Write($"dictionary prepared_ms={timer.ElapsedMilliseconds} entry={firstHeadword is not null}");
            });
        }
        catch { SpeechDiagnostics.Write("dictionary preparation failed; foreground lookup can retry"); }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing(); _active = true; _favoriteReady = false; UpdateFavoriteButton(); var generation = ++_generation;
        _lifetime?.Dispose(); _lifetime = new(); var token = _lifetime.Token;
        _language.PropertyChanged += LanguageChanged;
        _playback = _services.GetRequiredService<PlaybackCoordinator>();
        Title = PageTitle; _pinyin.Text = T(_showPinyin ? "HidePinyin" : "ShowPinyin");
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var detail = _detail ?? await Task.Run(() => DictionaryDetailProjection.Create(_document, token, sourceOnly: _learningContent), token);
            var projectionMs = timer.ElapsedMilliseconds;
            if (!_active || generation != _generation) return;
            _detail = detail; Render();
            SpeechDiagnostics.Write($"dictionary detail projection_ms={projectionMs} render_ms={timer.ElapsedMilliseconds - projectionMs}");
            await RefreshFavoriteAsync(generation, token);
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (_active && generation == _generation)
            {
                if (_detail is null) _list.EmptyView = new Label { Text = T("Failed"), Margin = 24 };
                else { _status.Text = T("FavoriteFailed"); _status.IsVisible = true; }
            }
        }
    }
    protected override async void OnDisappearing()
    {
        _active = false; _generation++; _lifetime?.Cancel(); _language.PropertyChanged -= LanguageChanged;
        base.OnDisappearing(); if (_playback is not null) await _playback.StopAsync(_owner);
    }
    private void LanguageChanged(object? sender, PropertyChangedEventArgs e)
    { if (_active && Shell.Current?.CurrentPage == this && e.PropertyName == nameof(LocalizationService.CurrentLanguage)) Render(); }

    private void TogglePinyin()
    {
        _showPinyin = !_showPinyin;
        Preferences.Default.Set("dictionary.show-pinyin", _showPinyin);
        _pinyin.Text = T(_showPinyin ? "HidePinyin" : "ShowPinyin");
        // Keep the native list, its header and item identities in place. Replacing ItemsSource
        // resets the viewport; changing only row presentation lets it retain the reading position.
        if (_list.ItemsSource is IEnumerable<Block> blocks)
            foreach (var block in blocks) block.Pinyin = _showPinyin;
        foreach (var block in _extraDefinitions) block.Pinyin = _showPinyin;
    }

    private void Render()
    {
        Title = PageTitle; _pinyin.Text = T(_showPinyin ? "HidePinyin" : "ShowPinyin");
        SemanticProperties.SetDescription(_more, T("More"));
        if (_detail is null) return;
        _list.EmptyView = null;
        _status.IsVisible = _voiceSettings.IsVisible = false;
        _voiceSettings.Text = _language["Speech.Title"];
        var header = new VerticalStackLayout { Padding = new Thickness(24, 22, 24, 20), Spacing = 8 };
        var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        var headword = new Grid { MinimumHeightRequest = 48, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center };
        var speak = new Button { BackgroundColor = Colors.Transparent, BorderWidth = 0, Padding = 0, AutomationId = "Dictionary.Headword" };
        SemanticProperties.SetDescription(speak, _detail.Headword.Text); SemanticProperties.SetHint(speak, T("Speak"));
        speak.Clicked += async (_, _) => await SpeakAsync();
        var title = new Label { Text = _detail.Headword.Text, FontSize = 38, FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center, InputTransparent = true };
        AutomationProperties.SetIsInAccessibleTree(title, false);
        speak.ZIndex = 1;
        headword.Add(title); headword.Add(speak); row.Add(headword, 0);
        _favorite = new ImageButton { WidthRequest = 48, HeightRequest = 48, Padding = 12, CornerRadius = 24,
            BackgroundColor = Colors.Transparent, Aspect = Aspect.AspectFit, AutomationId = "Dictionary.Favorite" };
        _favorite.Clicked += async (_, _) => await ToggleFavoriteAsync();
        UpdateFavoriteButton();
        row.Add(_favorite, 1); header.Add(row);
        header.Add(new Label { Text = string.Join(" ", _detail.Headword.Atoms.Where(a => a.Pinyin is not null).Select(a => a.Pinyin)),
            FontSize = 18, TextColor = Color.FromArgb("#73717A"), AutomationId = "Dictionary.HeadwordPinyin" });
        if (_learningContent && UiLanguagePolicy.SelectAuxiliaryTranslation(
            _document.TextUnits.Single(u => u.Role == TextUnitRole.Headword).Translations, _language.CurrentLanguage) is { } translation)
            header.Add(LibraryLayout.Muted(translation));
        var characterInfo = _detail.Notes.Split('\n').Where(s => s.StartsWith("部首：", StringComparison.Ordinal) || s.StartsWith("笔画：", StringComparison.Ordinal));
        var information = string.Join("   ·   ", characterInfo);
        if (information.Length > 0) header.Add(new Label { Text = information, FontSize = 13, TextColor = Colors.Gray });
        _list.Header = header;
        _blocks.Clear(); _extraDefinitions.Clear(); _definitionToggle = null;
        var paragraphs = _detail.Definitions.SelectMany(d => DictionaryPresentation.DefinitionParagraphs(d.Atoms)).ToArray();
        var longDefinition = paragraphs.Sum(p => p.Count) > 240;
        var remaining = longDefinition ? 120 : int.MaxValue;
        for (var i = 0; i < paragraphs.Length; i++)
        {
            var paragraph = paragraphs[i];
            var speech = DictionarySpeechTarget.Create(_document, paragraph);
            var take = Math.Min(remaining, paragraph.Count);
            AddAtoms(paragraph.Take(take).ToArray(), i == 0 ? T("Definitions") : "", null, _blocks, speech);
            remaining -= take;
            AddAtoms(paragraph.Skip(take).ToArray(), "", null, _extraDefinitions, speech);
        }
        if (longDefinition)
        {
            if (_expandedDefinitions) foreach (var block in _extraDefinitions) _blocks.Add(block);
            _definitionToggle = new(T(_expandedDefinitions ? "Collapse" : "Expand"), [], null, false, ToggleDefinitions);
            _blocks.Add(_definitionToggle);
        }
        for (var i = 0; i < _detail.Examples.Count; i++)
            Add(_detail.Examples[i], i == 0 ? T("Examples") : "", true);
        var footer = new VerticalStackLayout { Padding = new Thickness(24, 16, 24, 24), Spacing = 6 };
        if (_detail.Examples.Count == 0) footer.Add(new Label { Text = T("NoExamples"), FontSize = 13, TextColor = Colors.Gray });
        if (_detail.HasAutomaticPinyin) footer.Add(new Label { Text = T("Automatic"), FontSize = 12, TextColor = Colors.Gray });
        if (_detail.Examples.Any(e => e.Supplement)) footer.Add(new Label { Text = T("Supplement"), FontSize = 12, TextColor = Colors.Gray });
        footer.Add(new Label { Text = _document.Source.AuthorProvider, FontSize = 12, TextColor = Colors.Gray });
        _list.Footer = _learningContent ? null : footer;
        if (_learningContent && _blocks.Count == 0)
            _list.EmptyView = new Label { Text = _language["LearningWords.NoMeaning"], Margin = 24 };

        void Add(DictionaryText text, string heading, bool highlight)
        {
            var selected = highlight ? DictionaryDetailProjection.HighlightStarts(text.Text, _detail.Headword.Text) : null;
            var atoms = DictionaryPresentation.Compact(text.Atoms);
            var unit = _learningContent && atoms.Count > 0 ? _document.TextUnits.FirstOrDefault(u => u.Id == atoms[0].UnitId) : null;
            var translation = unit is null ? null : UiLanguagePolicy.SelectAuxiliaryTranslation(unit.Translations, _language.CurrentLanguage);
            if (atoms.Count > 0) AddAtoms(atoms, heading, selected, _blocks, DictionarySpeechTarget.Create(_document, atoms), translation);
        }
        void AddAtoms(IReadOnlyList<RubyAtom> atoms, string heading, IReadOnlySet<int>? selected, ICollection<Block> target, DictionarySpeechTarget speech, string? translation = null)
        {
            // Native list virtualization bounds the cost of very long publisher definitions.
            for (var offset = 0; offset < atoms.Count; offset += ReadingDocument.PageAtomLimit)
                target.Add(new(offset == 0 ? heading : "", atoms.Skip(offset).Take(ReadingDocument.PageAtomLimit).ToArray(), selected, _showPinyin,
                    speech: speech, translation: offset + ReadingDocument.PageAtomLimit >= atoms.Count ? translation : null));
        }
    }
    private void ToggleDefinitions()
    {
        if (_definitionToggle is null || _extraDefinitions.Count == 0) return;
        _expandedDefinitions = !_expandedDefinitions;
        if (_expandedDefinitions)
        {
            var index = _blocks.IndexOf(_definitionToggle);
            foreach (var block in _extraDefinitions) _blocks.Insert(index++, block);
        }
        else foreach (var block in _extraDefinitions) _blocks.Remove(block);
        _definitionToggle.Heading = T(_expandedDefinitions ? "Collapse" : "Expand");
        // On expansion keep the existing viewport; the continuation is inserted directly after
        // the preview. On collapse keep the control reachable without recreating the header.
        if (_expandedDefinitions) return;
        var anchor = _definitionToggle;
        var generation = _generation;
        Dispatcher.Dispatch(() =>
        {
            if (_active && generation == _generation && _blocks.Contains(anchor))
                _list.ScrollTo(anchor, position: ScrollToPosition.Start, animate: false);
        });
    }
    private async Task SpeakAsync()
    {
        _playback ??= _services.GetRequiredService<PlaybackCoordinator>();
        SpeechDiagnostics.Write($"dictionary-tap headword active={_active} ready={_playback is not null}");
        if (!_active || _detail is null || _playback is null) return;
        var generation = _generation; var request = ++_speechRequest; var services = _services;
        var voice = services.GetRequiredService<WordSpeechService>(); string? problem = null;
        ShowSpeechPending();
        var outcome = await _playback.PlayOperationAsync(_owner, "dictionary:" + _document.Id, async token =>
        {
            if (_allowEditing)
            {
                var document = new ReadingDocument(_document);
                var target = new ReadingTarget(_document.TextUnits.Single(u => u.Role == TextUnitRole.Headword).Id, null);
                var plan = await services.GetRequiredService<HanMate.Infrastructure.Database.ReadingAudioStore>().PlanAsync(document, target, token, true);
                var step = plan.Steps.Single();
                if (!step.AssetKey.StartsWith("speech:", StringComparison.Ordinal))
                { await services.GetRequiredService<IAudioPlaybackBackend>().PlayAsync(step.AssetKey, token); return; }
                // Recheck the original saved text/reading before using a display-only pronunciation.
                await services.GetRequiredService<HanMate.Infrastructure.Database.ReadingAudioStore>().ReadSpeechAsync(step.AssetKey, token);
            }
            try
            {
                var readings = _detail.Headword.Atoms.Select(a => PinyinSyllableParser.TryParseDictionarySyllable(a.Pinyin ?? "", out var p) ? p : null).ToArray();
                Func<string>? phonemes = readings.Length is > 0 and <= 32 && readings.All(p => p is not null && !p.Erhua)
                    ? () => string.Join(' ', readings.Select(p => PinyinVoiceInput.Syllable(p!.Base, p.Tone))) : null;
                await voice.SpeakAsync(_detail.Headword.Text, readings, phonemes, token);
            }
            catch (SpeechUnavailableException e) { problem = e.SystemVoice ? _language["Speech.Unavailable"] : T("NoVoice"); throw; }
            catch (UnsupportedVoiceTextException) { problem = _language["Voice.UnsupportedText"]; throw; }
        });
        if (!_active || generation != _generation || request != _speechRequest) return;
        ShowSpeechOutcome(outcome, problem);
    }
    private async Task SpeakPassageAsync(DictionarySpeechTarget passage)
    {
        _playback ??= _services.GetRequiredService<PlaybackCoordinator>();
        SpeechDiagnostics.Write($"dictionary-tap passage active={_active} ready={_playback is not null}");
        if (!_active || _playback is null) return;
        var generation = _generation; var request = ++_speechRequest;
        var services = _services; var voice = services.GetRequiredService<TextSpeechService>();
        string? problem = null; ShowSpeechPending();
        var outcome = await _playback.PlayOperationAsync(_owner, "dictionary-passage:" + passage.Identity, async token =>
        {
            if (_allowEditing && passage.SourceUnitId is { } unit)
            {
                var store = services.GetRequiredService<ReadingAudioStore>();
                var plan = await store.PlanAsync(new ReadingDocument(_document), new(unit, null), token, true);
                var step = plan.Steps.Single();
                // A whole-unit recording cannot be sliced to a displayed paragraph.
                if (passage.WholeUnit && !step.AssetKey.StartsWith("speech:", StringComparison.Ordinal))
                { await services.GetRequiredService<IAudioPlaybackBackend>().PlayAsync(step.AssetKey, token); return; }
                if (step.AssetKey.StartsWith("speech:", StringComparison.Ordinal)) await store.ReadSpeechAsync(step.AssetKey, token);
            }
            try { await voice.SpeakAsync(passage.Text, null, token); }
            catch (SpeechUnavailableException e) { problem = e.SystemVoice ? _language["Speech.Unavailable"] : T("NoVoice"); throw; }
            catch (UnsupportedVoiceTextException) { problem = _language["Voice.UnsupportedText"]; throw; }
        });
        if (!_active || generation != _generation || request != _speechRequest) return;
        ShowSpeechOutcome(outcome, problem);
    }
    private void ShowSpeechPending()
    {
        _voiceSettings.IsVisible = false;
        _status.Text = T("SpeechPending"); _status.IsVisible = true;
    }
    private void ShowSpeechOutcome(PlaybackOutcome outcome, string? problem)
    {
        _status.IsVisible = outcome is PlaybackOutcome.Failed or PlaybackOutcome.Busy;
        _status.Text = problem ?? _language[outcome == PlaybackOutcome.Busy ? "Audio.Busy" : "Audio.Failed"];
        _voiceSettings.IsVisible = outcome == PlaybackOutcome.Failed && (problem == T("NoVoice") || problem == _language["Speech.Unavailable"]);
    }
    private async Task MoreAsync()
    {
        if (_opening || !_active) return; _opening = true;
        try
        {
            var selection = await DisplayActionSheetAsync(T("More"), _language["Library.Cancel"], null,
                _allowEditing ? [T("Original"), T("Source"), T("Manage")] : [T("Original"), T("Source")]);
            if (!_active) return;
            if (selection == T("Manage")) await Navigation.PushAsync(new ReadingPage(new ReadingDocument(_document), _language));
            else if (selection == T("Original")) await Navigation.PushAsync(new ReadingPage(new ReadingDocument(_document), _language, allowEditing: false));
            else if (selection == T("Source")) await DisplayAlertAsync(T("Source"),
                string.Join("\n\n", new[] { _detail?.Notes, _document.Source.AuthorProvider, _document.Source.Reference,
                    _document.Source.PermissionNotes, T("PinyinSource") }.Where(s => !string.IsNullOrWhiteSpace(s))), _language["Library.Cancel"]);
        }
        finally { _opening = false; }
    }
    private void UpdateFavoriteButton()
    {
        if (_favorite is null) return;
        _favorite.Source = _isFavorite ? "favorite_filled.png" : "favorite_outline.png";
        _favorite.IsEnabled = _favoriteReady && !_savingFavorite;
        var text = T(_isFavorite ? "SavedFavorite" : "AddFavorite");
        SemanticProperties.SetDescription(_favorite, text);
        SemanticProperties.SetHint(_favorite, T(_isFavorite ? "RemoveFavorite" : "AddFavorite"));
        ToolTipProperties.SetText(_favorite, text);
    }
    private async Task RefreshFavoriteAsync(long generation, CancellationToken token)
    {
        var services = _services;
        DictionaryBookmark? bookmark = null;
        if (!_allowEditing)
        {
            if (_document.Source.SourceId == DefaultDictionaryStore.SourceId)
                bookmark = new(DictionaryBookmarkStore.Xinhua, _document.Id, _document.Title);
            else if ((await services.GetRequiredService<BundledPronunciationService>().GetCourseAsync()).Data.Contents.Any(d => d.Id == _document.Id))
                bookmark = new(DictionaryBookmarkStore.Pinyin, _document.Id, _document.Title);
        }
        var saved = bookmark is not null
            ? (await services.GetRequiredService<DictionaryBookmarkStore>().GetAsync(token)).Any(b => b.Provider == bookmark.Provider && b.EntryId == bookmark.EntryId)
            : (await services.GetRequiredService<FavoriteStore>().GetSelectionAsync(_document.Id, token)).FolderIds.Count > 0;
        if (!_active || generation != _generation) return;
        _bookmark = bookmark; _isFavorite = saved; _favoriteReady = true; UpdateFavoriteButton();
    }
    private async Task ToggleFavoriteAsync()
    {
        if (!_active || !_favoriteReady || _savingFavorite) return;
        _savingFavorite = true; UpdateFavoriteButton(); var generation = _generation;
        try
        {
            var services = _services;
            if (_bookmark is not null)
                await services.GetRequiredService<DictionaryBookmarkStore>().SetAsync(_bookmark, !_isFavorite);
            else
            {
                var store = services.GetRequiredService<FavoriteStore>();
                var selection = await store.GetSelectionAsync(_document.Id);
                var defaultFolder = (await store.GetFoldersAsync()).Single(f => f.IsDefault);
                await store.SetSelectionAsync(_document.Id, _isFavorite ? [] : [defaultFolder.Id], selection.Revision);
            }
            if (_active && generation == _generation)
            { await RefreshFavoriteAsync(generation, _lifetime!.Token); _status.IsVisible = false; }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (_active && generation == _generation)
            { _status.Text = T("FavoriteFailed"); _status.IsVisible = true; }
        }
        finally { _savingFavorite = false; UpdateFavoriteButton(); }
    }
    private sealed class BlockView : ContentView
    {
        private readonly Func<DictionarySpeechTarget, Task> _speak;
        private readonly Func<string> _hint;
        private static readonly BindableProperty PinyinProperty = BindableProperty.Create(
            nameof(Pinyin), typeof(bool), typeof(BlockView), false,
            propertyChanged: (view, _, _) => ((BlockView)view).RenderBlock());
        public bool Pinyin { get => (bool)GetValue(PinyinProperty); set => SetValue(PinyinProperty, value); }

        public BlockView(Func<DictionarySpeechTarget, Task> speak, Func<string> hint)
        { _speak = speak; _hint = hint; SetBinding(PinyinProperty, Binding.Create(static (Block block) => block.Pinyin)); }

        protected override void OnBindingContextChanged()
        {
            base.OnBindingContextChanged(); RenderBlock();
        }
        private void RenderBlock()
        {
            Content = null;
            if (BindingContext is not Block block) return;
            var body = new VerticalStackLayout { Padding = new Thickness(24, 0, 24, 14), Spacing = 10 };
            if (block.Expand is { } expand)
            {
                var button = new Button { Text = block.Heading, FontSize = 14, Padding = new Thickness(0, 4),
                    LineBreakMode = LineBreakMode.WordWrap,
                    BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb("#7452AD"), HorizontalOptions = LayoutOptions.Start,
                    AutomationId = "Dictionary.ExpandDefinitions" };
                button.BindingContext = block;
                button.SetBinding(Button.TextProperty, Binding.Create(static (Block item) => item.Heading));
                button.Clicked += (_, _) => expand(); body.Add(button); Content = body; return;
            }
            if (block.Heading.Length > 0) body.Add(new Label { Text = block.Heading, FontSize = 15,
                FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#73717A"), Margin = new Thickness(0, 12, 0, 0) });
            var ruby = new RubyTextView(block.Atoms, Pinyin, .74, null, block.Highlight, dictionaryStyle: true);
            if (block.Speech is { } passage)
            {
                var tap = new Button { BackgroundColor = Colors.Transparent, BorderWidth = 0, Padding = 0,
                    AutomationId = "Dictionary.Passage." + passage.Identity + ":" + block.Atoms[0].Start };
                SemanticProperties.SetDescription(tap, passage.Text); SemanticProperties.SetHint(tap, _hint());
                tap.Clicked += async (_, _) => await _speak(passage);
                ruby.InputTransparent = true; ruby.CascadeInputTransparent = true;
                ruby.SetValue(AutomationProperties.ExcludedWithChildrenProperty, true);
                // Put the native click target above the custom text layout so Android
                // hit testing does not depend on input transparency through its children.
                tap.ZIndex = 1;
                var area = new Grid { MinimumHeightRequest = 48 }; area.Add(ruby); area.Add(tap); body.Add(area);
            }
            else body.Add(ruby);
            if (block.Translation is not null) body.Add(LibraryLayout.Muted(block.Translation));
            Content = body;
        }
    }
}
