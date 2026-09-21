using System.ComponentModel;
using System.Text.Json;
using HanMate.App.Audio;
using HanMate.App.Controls;
using HanMate.App.Localization;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Localization;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

/// <summary>A learning view over saved grammar units; patterns never become speech targets.</summary>
public sealed class GrammarDetailPage : ContentPage
{
    private ReadingDocument _reading;
    private readonly LocalizationService _language;
    private readonly IServiceProvider _services;
    private readonly PlaybackCoordinator _playback;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly CollectionView _list = new()
    {
        SelectionMode = SelectionMode.None, AutomationId = "Grammar.Sections",
        ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepScrollOffset
    };
    private readonly ToolbarItem _pinyin = new() { AutomationId = "Grammar.Pinyin" };
    private readonly ToolbarItem _favorite = new() { Text = "☆", AutomationId = "Grammar.Favorite" };
    private readonly ToolbarItem _more = new() { Text = "⋯", AutomationId = "Grammar.More" };
    private readonly Label _status = LibraryLayout.Muted("");
    private readonly Label _reserved = LibraryLayout.Muted("");
    private List<Block> _blocks = [];
    private bool _active, _opening;
    private bool _showPinyin = Preferences.Default.Get("grammar.show-pinyin", true);
    private string? _renderedCulture;
    private long _generation, _request;
    private CancellationTokenSource? _lifetime;

    public GrammarDetailPage(ReadingDocument reading, LocalizationService language, IServiceProvider services)
    {
        if (reading.Content.Kind != ContentKind.Grammar) throw new ArgumentException("Expected grammar content.", nameof(reading));
        _reading = reading; _language = language; _services = services;
        _playback = services.GetRequiredService<PlaybackCoordinator>();
        this.SetDynamicResource(StyleProperty, "LibraryPage");
        ToolbarItems.Add(_pinyin); ToolbarItems.Add(_favorite); ToolbarItems.Add(_more);
        _pinyin.Clicked += (_, _) =>
        {
            _showPinyin = !_showPinyin; Preferences.Default.Set("grammar.show-pinyin", _showPinyin);
            _pinyin.Text = _language["Reader." + (_showPinyin ? "HidePinyin" : "ShowPinyin")];
            foreach (var block in _blocks) block.Pinyin = _showPinyin;
        };
        _favorite.Clicked += async (_, _) => await OpenAsync(() => Navigation.PushAsync(
            new FavoritePickerPage(_services.GetRequiredService<FavoriteStore>(), _language, _reading.Content.Id)));
        _more.Clicked += async (_, _) => await OpenAsync(MoreAsync);
        _list.ItemTemplate = new DataTemplate(() => new BlockView(SpeakAsync, () => T("Speak")));
        _reserved.Opacity = 0; _reserved.InputTransparent = true;
        AutomationProperties.SetIsInAccessibleTree(_reserved, false);
        _status.AutomationId = "Grammar.Status";
        var footer = new Grid { Padding = new Thickness(24, 8), InputTransparent = true, AutomationId = "Grammar.PlaybackFooter" };
        footer.Add(_reserved); footer.Add(_status);
        var layout = new Grid { MaximumWidthRequest = 920, RowDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        layout.Add(_list); layout.Add(footer, 0, 1); Content = layout;
        Render();
    }

    private string T(string key) => _language["GrammarDetail." + key];

    protected override async void OnAppearing()
    {
        base.OnAppearing(); _active = true; var generation = ++_generation;
        _lifetime?.Dispose(); _lifetime = new(); var token = _lifetime.Token;
        _language.PropertyChanged += LanguageChanged;
        if (_renderedCulture != _language.CurrentCultureName) Render();
        try
        {
            // Retain the actual list and viewport on return unless saved content has changed.
            var current = _reading.Content;
            var changed = await Task.Run(async () =>
            {
                var snapshot = await _services.GetRequiredService<SqliteContentDocumentStore>().GetAsync(current.Id, token);
                return snapshot is not null && JsonSerializer.Serialize(snapshot.Document, ContentJson.Options) != JsonSerializer.Serialize(current, ContentJson.Options)
                    ? new ReadingDocument(snapshot.Document) : null;
            }, token);
            if (!_active || generation != _generation) return;
            if (changed is not null) { _reading = changed; Render(); }
            var selection = await _services.GetRequiredService<FavoriteStore>().GetSelectionAsync(current.Id, token);
            if (!_active || generation != _generation) return;
            _favorite.Text = selection.FolderIds.Count > 0 ? "★" : "☆";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { if (_active && generation == _generation) _status.Text = _language["Library.Failed"]; }
    }

    protected override async void OnDisappearing()
    {
        _active = false; _generation++; _request++; _lifetime?.Cancel(); _status.Text = "";
        _language.PropertyChanged -= LanguageChanged;
        base.OnDisappearing(); await _playback.StopAsync(_owner);
    }

    private void LanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_active && e.PropertyName == nameof(LocalizationService.CurrentLanguage)) Render();
    }

    private void Render()
    {
        Title = _reading.Content.Title; _renderedCulture = _language.CurrentCultureName;
        _pinyin.Text = _language["Reader." + (_showPinyin ? "HidePinyin" : "ShowPinyin")];
        SemanticProperties.SetDescription(_favorite, _language["Library.ChooseFolders"]);
        SemanticProperties.SetDescription(_more, _language["DictionaryDetail.More"]);
        _reserved.Text = _language["DictionaryDetail.SpeechPending"];
        _status.Text = "";
        var header = new VerticalStackLayout { Padding = new Thickness(24, 22, 24, 12), Spacing = 14 };
        header.Add(new Label { Text = Title, FontSize = 28, FontAttributes = FontAttributes.Bold });
        var grammar = _reading.Content.Grammar!;
        var pattern = new VerticalStackLayout { Spacing = 8 };
        pattern.Add(Heading(_language["Grammar.Pattern"]));
        var formatted = new FormattedString();
        foreach (var part in grammar.PatternParts)
        {
            if (formatted.Spans.Count > 0) formatted.Spans.Add(new Span { Text = " " });
            formatted.Spans.Add(new Span { Text = part.Text, FontAttributes = part.Kind == GrammarPatternPartKind.Literal ? FontAttributes.Bold : FontAttributes.None });
        }
        pattern.Add(new Label { FormattedText = formatted, FontSize = 21, AutomationId = "Grammar.Pattern" });
        if (UiLanguagePolicy.SelectAuxiliaryTranslation(grammar.PatternTranslations, _language.CurrentLanguage) is { } translation)
            pattern.Add(LibraryLayout.Muted(translation));
        header.Add(LibraryLayout.Surface(pattern, new Thickness(16)));
        header.Add(LibraryLayout.Muted(T("Hint")));
        _list.Header = header;
        // ReadingDocument follows the grammar's ordered ID lists and bounds each native ruby layout.
        var seen = new HashSet<TextUnitRole>();
        _blocks = _reading.Pages.SelectMany(p => p.Parts).Select(part => new Block(part,
            seen.Add(part.Unit.Role) ? _language[part.Unit.Role switch
            {
                TextUnitRole.GrammarExplanation => "Grammar.Explanation",
                TextUnitRole.Example => "Grammar.Examples",
                _ => "Grammar.Notes"
            }] : "",
            part.EndsUnit ? UiLanguagePolicy.SelectAuxiliaryTranslation(part.Unit.Translations, _language.CurrentLanguage) : null,
            _showPinyin)).ToList();
        _list.ItemsSource = _blocks;
    }

    private static Label Heading(string text) => new() { Text = text, FontSize = 15, FontAttributes = FontAttributes.Bold };

    private async Task SpeakAsync(Guid unitId)
    {
        if (!_active || _opening) return;
        var request = ++_request;
        var reading = _reading;
        string? problem = null;
        _status.Text = _language["DictionaryDetail.SpeechPending"];
        var outcome = await _playback.PlayOperationAsync(_owner, "grammar-unit:" + unitId, async token =>
        {
            var store = _services.GetRequiredService<ReadingAudioStore>();
            var plan = await store.PlanAsync(reading, new(unitId, null), token, allowSpeechFallback: true);
            var step = plan.Steps.Single();
            if (!step.AssetKey.StartsWith("speech:", StringComparison.Ordinal))
            {
                await _services.GetRequiredService<IAudioPlaybackBackend>().PlayAsync(step.AssetKey, token);
                return;
            }
            var text = await store.ReadSpeechAsync(step.AssetKey, token);
            try { await _services.GetRequiredService<TextSpeechService>().SpeakAsync(text, null, token); }
            catch (SpeechUnavailableException e) { problem = _language[e.SystemVoice ? "Speech.Unavailable" : "DictionaryDetail.NoVoice"]; throw; }
            catch (UnsupportedVoiceTextException) { problem = _language["Voice.UnsupportedText"]; throw; }
        });
        if (!_active || request != _request) return;
        _status.Text = outcome switch
        {
            PlaybackOutcome.Busy => _language["Audio.Busy"],
            PlaybackOutcome.Failed => problem ?? _language["Audio.Failed"],
            _ => ""
        };
    }

    private async Task OpenAsync(Func<Task> action)
    {
        if (!_active || _opening) return;
        _opening = true;
        try
        {
            _request++; _status.Text = ""; await _playback.StopAsync(_owner);
            if (_active) await action();
        }
        catch { if (_active) _status.Text = _language["Library.Failed"]; }
        finally { _opening = false; }
    }

    private async Task MoreAsync()
    {
        var choice = await DisplayActionSheetAsync(_language["DictionaryDetail.More"], _language["Library.Cancel"], null,
            T("Manage"), T("Source"), _language["Speech.Title"]);
        if (!_active) return;
        if (choice == T("Manage")) await Navigation.PushAsync(new ReadingPage(_reading, _language));
        else if (choice == _language["Speech.Title"]) await Navigation.PushAsync(ActivatorUtilities.CreateInstance<SpeechSettingsPage>(_services));
        else if (choice == T("Source"))
        {
            var source = _reading.Content.Source;
            await DisplayAlertAsync(T("Source"), string.Join("\n\n", new[] { source.AuthorProvider, source.Reference,
                source.LicenseIdentifier, source.PermissionNotes, _language["Reader." + (source.ReviewStatus == ReviewStatus.Approved ? "Source" : "Draft")] }
                .Where(s => !string.IsNullOrWhiteSpace(s))), _language["Library.Cancel"]);
        }
    }

    internal sealed class Block(ReadingPart part, string heading, string? translation, bool pinyin) : INotifyPropertyChanged
    {
        public ReadingPart Part { get; } = part;
        public string Heading { get; } = heading;
        public string? Translation { get; } = translation;
        public bool Pinyin
        {
            get => pinyin;
            set { if (value == pinyin) return; pinyin = value; PropertyChanged?.Invoke(this, new(nameof(Pinyin))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class BlockView : ContentView
    {
        private static readonly BindableProperty PinyinProperty = BindableProperty.Create(nameof(Pinyin), typeof(bool), typeof(BlockView), true,
            propertyChanged: (bindable, _, _) => ((BlockView)bindable).Render());
        public bool Pinyin { get => (bool)GetValue(PinyinProperty); set => SetValue(PinyinProperty, value); }
        private readonly Func<Guid, Task> _speak;
        private readonly Func<string> _hint;
        public BlockView(Func<Guid, Task> speak, Func<string> hint)
        {
            _speak = speak; _hint = hint;
            SetBinding(PinyinProperty, Binding.Create(static (Block block) => block.Pinyin));
        }
        protected override void OnBindingContextChanged() { base.OnBindingContextChanged(); Render(); }
        private void Render()
        {
            if (BindingContext is not Block block) { Content = null; return; }
            var body = new VerticalStackLayout { Padding = new Thickness(24, 6, 24, 10), Spacing = 8 };
            if (block.Heading.Length > 0) body.Add(Heading(block.Heading));
            var ruby = new RubyTextView(block.Part.Atoms, Pinyin, .8, null, dictionaryStyle: true)
                { InputTransparent = true, CascadeInputTransparent = true };
            ruby.SetValue(AutomationProperties.ExcludedWithChildrenProperty, true);
            var tap = new Button { BackgroundColor = Colors.Transparent, BorderWidth = 0, Padding = 0, ZIndex = 1,
                AutomationId = "Grammar.Unit." + block.Part.Unit.Id + ":" + block.Part.Atoms[0].Start };
            SemanticProperties.SetDescription(tap, block.Part.Unit.Text); SemanticProperties.SetHint(tap, _hint());
            tap.Clicked += async (_, _) => await _speak(block.Part.Unit.Id);
            var area = new Grid { MinimumHeightRequest = 48 }; area.Add(ruby); area.Add(tap); body.Add(area);
            if (block.Translation is not null) body.Add(LibraryLayout.Muted(block.Translation));
            Content = body;
        }
    }
}
