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
using HanMate.Infrastructure.Pinyin;

namespace HanMate.App.Pages;

/// <summary>A clean text/poem reader with explicit full-text and inline segment playback.</summary>
public sealed class LessonDetailPage : ContentPage
{
    private ReadingDocument _reading;
    private readonly LocalizationService _language;
    private readonly IServiceProvider _services;
    private readonly PlaybackCoordinator _playback;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly CollectionView _list = new()
    {
        SelectionMode = SelectionMode.None, AutomationId = "Lesson.Sections",
        ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepScrollOffset
    };
    private readonly ToolbarItem _pinyin = new() { AutomationId = "Lesson.Pinyin" };
    private readonly ToolbarItem _favorite = new() { Text = "☆", AutomationId = "Lesson.Favorite" };
    private readonly ToolbarItem _more = new() { Text = "⋯", AutomationId = "Lesson.More" };
    private readonly Label _status = LibraryLayout.Muted("");
    private readonly Label _reserved = LibraryLayout.Muted("");
    private List<Block> _blocks = [];
    private bool _active, _opening;
    private bool _showPinyin = Preferences.Default.Get("lesson.show-pinyin", true);
    private string? _renderedCulture;
    private long _generation, _request;
    private CancellationTokenSource? _lifetime;
    private readonly ContentView _titleView = new();
    private readonly ContentView _authorView = new();
    private LessonHeading? _titleHeading, _authorHeading;
    private string? Author => _reading.Content.Kind == ContentKind.Poem &&
        _reading.Content.Source.AuthorProvider is { Length: > 0 } author && author != "User"
        ? author : null;

    public LessonDetailPage(ReadingDocument reading, LocalizationService language, IServiceProvider services)
    {
        if (reading.Content.Kind is not (ContentKind.Text or ContentKind.Poem)) throw new ArgumentException("Expected a text or poem.", nameof(reading));
        _reading = reading; _language = language; _services = services;
        _playback = services.GetRequiredService<PlaybackCoordinator>();
        this.SetDynamicResource(StyleProperty, "LibraryPage");
        ToolbarItems.Add(_pinyin); ToolbarItems.Add(_favorite); ToolbarItems.Add(_more);
        _pinyin.Clicked += (_, _) =>
        {
            _showPinyin = !_showPinyin; Preferences.Default.Set("lesson.show-pinyin", _showPinyin);
            _pinyin.Text = _language["Reader." + (_showPinyin ? "HidePinyin" : "ShowPinyin")];
            foreach (var block in _blocks) block.Pinyin = _showPinyin;
            RenderHeadings();
        };
        _favorite.Clicked += async (_, _) => await OpenAsync(() => Navigation.PushAsync(
            new FavoritePickerPage(_services.GetRequiredService<FavoriteStore>(), _language, _reading.Content.Id)));
        _more.Clicked += async (_, _) => await OpenAsync(MoreAsync);
        _list.ItemTemplate = new DataTemplate(() => new BlockView(ActivateAsync, () => T("SpeakSegment")));
        _reserved.Opacity = 0; _reserved.InputTransparent = true;
        AutomationProperties.SetIsInAccessibleTree(_reserved, false);
        _status.AutomationId = "Lesson.Status";
        var footer = new Grid { Padding = new Thickness(24, 8), InputTransparent = true, AutomationId = "Lesson.PlaybackFooter" };
        footer.Add(_reserved); footer.Add(_status);
        var layout = new Grid { MaximumWidthRequest = 920, RowDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        layout.Add(_list); layout.Add(footer, 0, 1); Content = layout;
        Render();
    }

    private string T(string key) => _language["LessonDetail." + key];

    protected override async void OnAppearing()
    {
        base.OnAppearing(); _active = true; var generation = ++_generation;
        _lifetime?.Dispose(); _lifetime = new(); var token = _lifetime.Token;
        _language.PropertyChanged += LanguageChanged;
        var showPinyin = Preferences.Default.Get("lesson.show-pinyin", true);
        if (_showPinyin != showPinyin)
        {
            _showPinyin = showPinyin;
            _pinyin.Text = _language["Reader." + (_showPinyin ? "HidePinyin" : "ShowPinyin")];
            foreach (var block in _blocks) block.Pinyin = _showPinyin;
            RenderHeadings();
        }
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
            if (changed is not null)
            {
                _reading = changed;
                Render();
            }
            var title = _reading.Content.Title; var author = Author;
            if (_titleHeading?.Text != title || _authorHeading?.Text != author)
            {
                var headings = await Task.Run(() => (Title: LessonHeadingAnnotation.Create(title, token),
                    Author: author is null ? null : LessonHeadingAnnotation.Create(author, token)), token);
                if (!_active || generation != _generation) return;
                _titleHeading = headings.Title; _authorHeading = headings.Author;
                RenderHeadings();
            }
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
        // Detach reusable heading hosts before replacing the collection header.
        if (_list.Header is Layout previousHeader) previousHeader.Children.Clear();
        RenderHeadings();
        header.Add(_titleView);
        if (Author is not null) header.Add(_authorView);
        header.Add(LibraryLayout.Muted(T("Hint")));
        var read = LibraryLayout.Quiet(new Button { Text = T("ReadAll"),
            AutomationId = "Lesson.Play", HorizontalOptions = LayoutOptions.Start, LineBreakMode = LineBreakMode.WordWrap });
        read.IsEnabled = _reading.Targets.Count > 0;
        read.Clicked += async (_, _) => await SpeakAsync(); header.Add(read);
        _list.Header = header;
        _blocks = LessonPresentation.Create(_reading, _language.CurrentLanguage)
            .Select(part => new Block(part, _showPinyin, .9)).ToList();
        _list.ItemsSource = _blocks;
    }

    private Task ActivateAsync(ReadingTarget target) => SpeakAsync(target);

    private void RenderHeadings()
    {
        _titleView.Content = HeadingView(_reading.Content.Title, _titleHeading, "Title", 1.08);
        _authorView.Content = Author is { } author ? HeadingView(author, _authorHeading, "Author", .7) : null;
    }

    private View HeadingView(string text, LessonHeading? heading, string kind, double scale)
    {
        var body = new VerticalStackLayout { Spacing = 0, InputTransparent = true, CascadeInputTransparent = true };
        if (heading?.Text == text)
            foreach (var chunk in heading.Atoms.Chunk(ReadingDocument.PageAtomLimit))
                body.Add(new RubyTextView(chunk, _showPinyin, scale, null, dictionaryStyle: true));
        else body.Add(new Label { Text = text, FontSize = 26 * scale, LineBreakMode = LineBreakMode.WordWrap });
        body.SetValue(AutomationProperties.ExcludedWithChildrenProperty, true);
        var tap = new Button { BackgroundColor = Colors.Transparent, BorderWidth = 0, Padding = 0,
            AutomationId = "Lesson." + kind + ".Speak", ZIndex = 1 };
        SemanticProperties.SetDescription(tap, text);
        SemanticProperties.SetHint(tap, T("SpeakSegment"));
        tap.Clicked += async (_, _) => await SpeakHeadingAsync(text, kind);
        var area = new Grid { MinimumHeightRequest = 48 }; area.Add(body); area.Add(tap); return area;
    }

    private async Task SpeakHeadingAsync(string text, string kind)
    {
        if (!_active || _opening) return;
        var request = ++_request; string? problem = null;
        _status.Text = _language["DictionaryDetail.SpeechPending"];
        var outcome = await _playback.PlayOperationAsync(_owner, $"lesson-heading:{_reading.Content.Id}:{kind}:{text}", async token =>
        {
            var heading = kind == "Title" ? _titleHeading : _authorHeading;
            if (heading?.Text != text) heading = await Task.Run(() => LessonHeadingAnnotation.Create(text, token), token);
            try { await _services.GetRequiredService<TextSpeechService>().SpeakAsync(text,
                heading.Phonemes is { } phones ? () => phones : null, token); }
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

    private async Task SpeakAsync(ReadingTarget? selected = null)
    {
        if (!_active || _opening || _reading.Targets.Count == 0) return;
        var request = ++_request;
        var reading = _reading;
        string? problem = null;
        _status.Text = _language["DictionaryDetail.SpeechPending"];
        var identity = selected is null ? "lesson-all:" + reading.Content.Id : "lesson-segment:" + selected.SegmentId;
        var outcome = await _playback.PlayOperationAsync(_owner, identity, async token =>
        {
            var store = _services.GetRequiredService<ReadingAudioStore>();
            var plan = await store.PlanAsync(reading, selected, token, allowSpeechFallback: true);
            foreach (var step in plan.Steps)
            {
                token.ThrowIfCancellationRequested();
                if (!step.AssetKey.StartsWith("speech:", StringComparison.Ordinal))
                    await _services.GetRequiredService<IAudioPlaybackBackend>().PlayAsync(step.AssetKey, token);
                else
                {
                    var text = await store.ReadSpeechAsync(step.AssetKey, token);
                    try { await _services.GetRequiredService<TextSpeechService>().SpeakAsync(text, null, token); }
                    catch (SpeechUnavailableException e) { problem = _language[e.SystemVoice ? "Speech.Unavailable" : "DictionaryDetail.NoVoice"]; throw; }
                    catch (UnsupportedVoiceTextException) { problem = _language["Voice.UnsupportedText"]; throw; }
                }
            }
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
            _language["LearningEdit.Edit"], _language["LanShare.Share"]);
        if (!_active) return;
        if (choice == _language["LearningEdit.Edit"])
            await LearningEditorNavigation.OpenAsync(this, _reading.Content, _language, _services);
        else if (choice == _language["LanShare.Share"])
            await LearningShareRoomLauncher.OpenAsync(this, _services, _language, _reading.Content.Id);
    }

    internal sealed class Block(LessonBlock part, bool pinyin, double scale) : INotifyPropertyChanged
    {
        public LessonBlock Part { get; } = part;
        public double Scale { get; } = scale;
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
        private readonly Func<ReadingTarget, Task> _activate;
        private readonly Func<string> _hint;
        public BlockView(Func<ReadingTarget, Task> activate, Func<string> hint)
        {
            _activate = activate; _hint = hint;
#if ANDROID
            Loaded += (_, _) => AndroidCollectionRowFocus.RemoveUnnamedItemFocus(this);
#endif
            SetBinding(PinyinProperty, Binding.Create(static (Block block) => block.Pinyin));
        }
        protected override void OnBindingContextChanged() { base.OnBindingContextChanged(); Render(); }
        private void Render()
        {
            if (BindingContext is not Block block) { Content = null; return; }
            var part = block.Part;
            if (part.Atoms.Count > 0 && part.Atoms.All(a => a.HardBreak))
            {
                // Speech rows already start on a new line; retain any additional blank lines.
                Content = new BoxView { Opacity = 0, InputTransparent = true,
                    HeightRequest = Math.Max(0, part.Atoms.Count - 1) * (Pinyin ? 50 : 34) * block.Scale };
                return;
            }
            var body = new VerticalStackLayout { Padding = new Thickness(24, 4, 24, 10), Spacing = 8 };
            if (part.Atoms.Count > 0)
            {
                var ruby = new RubyTextView(part.Atoms, Pinyin, block.Scale, null, dictionaryStyle: true);
                if (part.Target is { } target)
                {
                    ruby.InputTransparent = true; ruby.CascadeInputTransparent = true;
                    ruby.SetValue(AutomationProperties.ExcludedWithChildrenProperty, true);
                    var tap = new Button { BackgroundColor = Colors.Transparent, BorderWidth = 0, Padding = 0, ZIndex = 1,
                        AutomationId = "Lesson.Segment." + target.SegmentId + ":" + part.Atoms[0].Start };
                    SemanticProperties.SetDescription(tap, string.Concat(part.Atoms.Select(a => a.Text)));
                    SemanticProperties.SetHint(tap, _hint());
                    tap.Clicked += async (_, _) => await _activate(target);
                    var area = new Grid { MinimumHeightRequest = 48 }; area.Add(ruby); area.Add(tap); body.Add(area);
                }
                else body.Add(ruby);
            }
            if (part.Translation is not null) body.Add(LibraryLayout.Muted(part.Translation));
            Content = body;
        }
    }
}
