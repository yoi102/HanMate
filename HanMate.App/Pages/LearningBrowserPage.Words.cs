using HanMate.App.Audio;
using HanMate.App.Controls;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Localization;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

public sealed partial class LearningBrowserPage
{
    private sealed record WordListItem(ContentDocument Document, TextUnit Headword);
    private sealed class WordCardTemplateSelector(LearningBrowserPage page) : DataTemplateSelector
    {
        private readonly DataTemplate _readOnly = new(page.CreateWordSurface);
        private readonly DataTemplate _editable = new(page.CreateWordCard);
        protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
            item is WordListItem word && (word.Document.Origin == ContentOrigin.Resource ||
                word.Document.Origin == ContentOrigin.Personal && word.Document.Source.SourceId == "personal")
                ? _editable : _readOnly;
    }
    private readonly Guid _wordOwner = Guid.NewGuid();
    private PlaybackCoordinator? _wordPlayback;
    private CancellationTokenSource? _wordLifetime;
    private Label _wordStatus = LibraryLayout.Status();
    private bool _wordActive;
    private long _wordRequest;

    protected override void OnAppearing()
    {
        _wordActive = true;
        _wordLifetime?.Dispose(); _wordLifetime = new();
        base.OnAppearing();
    }

    protected override async void OnDisappearing()
    {
        _wordActive = false; _wordRequest++; _wordLifetime?.Cancel(); _wordStatus.Text = "";
        base.OnDisappearing();
        if (_wordPlayback is not null) await _wordPlayback.StopAsync(_wordOwner);
    }

    private async Task ResetWordPlaybackAsync()
    {
        _wordRequest++;
        if (_wordPlayback is not null) await _wordPlayback.StopAsync(_wordOwner);
        _wordStatus = LibraryLayout.Status();
        _wordStatus.AutomationId = "Learning.WordStatus";
    }

    private object CreateWordSurface()
    {
        // Reuse the native tap/hold surface, including scroll cancellation and Windows right-click/F10.
        var card = new PinyinExampleView { AutomationId = "Learning.WordItem" };
        SemanticProperties.SetHint(card, Language["LearningWords.Gesture"]);
        card.BindingContextChanged += (_, _) =>
        {
            if (card.BindingContext is not WordListItem item) { card.Content = null; return; }
            var text = new VerticalStackLayout
            {
                Padding = new Thickness(12, 8), InputTransparent = true, CascadeInputTransparent = true,
                Children = { new RubyWordView(item.Headword, scale: .85) { InputTransparent = true, CascadeInputTransparent = true } }
            };
            var translation = UiLanguagePolicy.SelectAuxiliaryTranslation(item.Headword.Translations, Language.CurrentLanguage);
            if (translation is not null)
            {
                var auxiliary = LibraryLayout.Muted(translation);
                auxiliary.Margin = new Thickness(6, 2, 6, 6);
                auxiliary.InputTransparent = true;
                text.Add(auxiliary);
            }
            card.Content = text;
            SemanticProperties.SetDescription(card, item.Headword.Text + " " + string.Join(" ", item.Headword.Tokens.Select(t => t.Pinyin?.Display)));
            if (translation is not null) SemanticProperties.SetDescription(card, SemanticProperties.GetDescription(card) + ". " + translation);
        };
        card.Read += async (_, _) => { if (card.BindingContext is WordListItem item) await SpeakWordAsync(item); };
        card.Define += async (_, _) => { if (card.BindingContext is WordListItem item) await DefineWordAsync(item); };
        return LibraryLayout.CollectionRow(card);
    }

    private object CreateWordCard()
    {
        var swipe = new SwipeView { Content = (View)CreateWordSurface() };
        var actions = new SwipeItems { Mode = SwipeMode.Reveal };
        actions.Add(new SwipeItem { Text = Language["WordEditor.EditWord"], BackgroundColor = Colors.SteelBlue,
            Command = new Command(async () =>
            {
                if (swipe.BindingContext is WordListItem item) await EditWordAsync(item);
            }) });
        actions.Add(new SwipeItem { Text = T("Delete"), BackgroundColor = Colors.IndianRed,
            Command = new Command(async () =>
            {
                if (swipe.BindingContext is WordListItem item) await DeleteWordAsync(item);
            }) });
        swipe.RightItems = actions;
        return swipe;
    }

    private async Task EditWordAsync(WordListItem item)
    {
        await RunAsync(async () =>
        {
            if (Handler?.MauiContext?.Services is not { } services) return;
            var savedCategory = item.Document.Scenes
                .Where(x => x.StartsWith("word-", StringComparison.Ordinal))
                .Select(x => x[5..]).FirstOrDefault(x => WordCategories.All.Contains(x) || CustomWordCategoryStore.IsCustom(x));
            var categories = await services.GetRequiredService<CustomWordCategoryStore>().ListAsync();
            var preferred = wordCategory is not null && wordCategory != "all" ? wordCategory : savedCategory;
            var category = preferred is not null && (WordCategories.All.Contains(preferred) || categories.Any(x => x.Id == preferred))
                ? preferred : WordCategories.Other;
            await Navigation.PushAsync(new PersonalWordEditorPage(category, item.Document,
                services.GetRequiredService<PersonalWordStore>(), services.GetRequiredService<CustomWordCategoryStore>(), Language));
        });
    }

    private async Task DeleteWordAsync(WordListItem item)
    {
        await RunAsync(async () =>
        {
            var personal = item.Document.Origin == ContentOrigin.Personal && item.Document.Source.SourceId == "personal";
            if (!await DisplayAlertAsync(T("Delete"), Language[personal ? "WordEditor.DeleteHint" : "WordEditor.HideBuiltInHint"],
                T("Delete"), T("Cancel"))) return;
            if (Handler?.MauiContext?.Services is not { } services) return;
            if (personal)
            {
                var trash = services.GetRequiredService<ContentTrashStore>();
                var plan = await trash.PreviewAsync(item.Document.Id);
                await trash.MoveAsync(plan);
            }
            else await services.GetRequiredService<PersonalWordStore>().HideResourceAsync(item.Document.Id);
            await ReloadAsync();
        });
    }

    private async Task SpeakWordAsync(WordListItem item)
    {
        if (!_wordActive || Busy || Handler?.MauiContext?.Services is not { } services) return;
        _wordPlayback ??= services.GetRequiredService<PlaybackCoordinator>();
        var request = ++_wordRequest;
        string? problem = null;
        _wordStatus.Text = Language["DictionaryDetail.SpeechPending"];
        // Do not serialize clicks with DataPage.Busy: the coordinator must receive stop/switch taps immediately.
        var outcome = await _wordPlayback.PlayOperationAsync(_wordOwner, "learning-word:" + item.Headword.Id, async token =>
        {
            var useAutomatic = item.Document.Scenes.Contains(PersonalWordStore.AutoVoiceScene);
            string speechText = item.Headword.Text;
            if (!useAutomatic)
            {
                var audio = services.GetRequiredService<ReadingAudioStore>();
                var document = await Task.Run(() => new ReadingDocument(item.Document), token);
                var plan = await audio.PlanAsync(document, new(item.Headword.Id, null), token, allowSpeechFallback: true);
                var step = plan.Steps.Single();
                if (!step.AssetKey.StartsWith("speech:", StringComparison.Ordinal))
                {
                    await services.GetRequiredService<IAudioPlaybackBackend>().PlayAsync(step.AssetKey, token);
                    return;
                }
                speechText = await audio.ReadSpeechAsync(step.AssetKey, token);
            }
            Func<string>? phonemes = item.Headword.Tokens.Count is > 0 and <= 32 &&
                item.Headword.Tokens.All(t => t.Kind == TokenKind.Hanzi && t.Pinyin is { Erhua: false })
                ? () => PinyinVoiceInput.Example(item.Headword) : null;
            try { await services.GetRequiredService<WordSpeechService>().SpeakAsync(speechText, item.Headword.Tokens.Select(t => t.Pinyin).ToArray(), phonemes, token); }
            catch (SpeechUnavailableException e)
            { problem = Language[e.SystemVoice ? "Speech.Unavailable" : "DictionaryDetail.NoVoice"]; throw; }
            catch (UnsupportedVoiceTextException) { problem = Language["Voice.UnsupportedText"]; throw; }
        });
        if (!_wordActive || request != _wordRequest) return;
        _wordStatus.Text = outcome switch
        {
            PlaybackOutcome.Busy => Language["Audio.Busy"],
            PlaybackOutcome.Failed => problem ?? Language["Audio.Failed"],
            _ => ""
        };
    }

    private async Task DefineWordAsync(WordListItem item)
    {
        if (!_wordActive || Busy || Handler?.MauiContext?.Services is not { } services) return;
        var token = _wordLifetime!.Token;
        await RunAsync(async () =>
        {
            try
            {
                _wordRequest++; _wordStatus.Text = "";
                if (_wordPlayback is not null) await _wordPlayback.StopAsync(_wordOwner);
                if (!_wordActive || token.IsCancellationRequested) return;
                await Navigation.PushAsync(await LearningDetailPageFactory.CreateAsync(item.Document, Language, services));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        });
    }
}
