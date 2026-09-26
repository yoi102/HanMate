using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using HanMate.Core.Localization;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly VersionedLocalStateStore _settingsStore;
    private readonly SemaphoreSlim _changeGate = new(1, 1);
    private UiLanguage _currentLanguage;
    private bool _initialized;

    public LocalizationService(VersionedLocalStateStore settingsStore)
    {
        _settingsStore = settingsStore;
        _currentLanguage = UiLanguagePolicy.ResolveSystemLanguage(CultureInfo.CurrentUICulture.Name);
        ApplyCulture(_currentLanguage, notify: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiLanguage CurrentLanguage => _currentLanguage;
    public string CurrentCultureName => UiLanguagePolicy.ToCultureName(_currentLanguage);
    public bool ShowAuxiliaryTranslations => _currentLanguage != UiLanguage.ChineseSimplified;
    public string AppName => this["App.Name"];
    public string NavPinyin => this["Nav.Pinyin"];
    public string NavLearn => this["Nav.Learn"];
    public string NavSearch => this["Nav.Search"];
    public string NavFavorites => this["Nav.Favorites"];
    public string NavSettings => this["Nav.Settings"];
    public string ActionPlay => this["Action.Play"];
    public string KindWord => this["Kind.Word"];
    public string KindText => this["Kind.Text"];
    public string KindGrammar => this["Learn.Grammar"];
    public string KindPoem => this["Kind.Poem"];
    public string StateNoResults => this["State.NoResults"];
    public string SearchPlaceholder => this["Search.Placeholder"];
    public string DefaultFolderName => this["Folder.Default"];
    public string ManageResources => this["Resource.Manage"];
    public string ManageDictionaries => this["Dictionary.Manage"];
    public string ExportBackup => this["Backup.Title"];
    public string PinyinResources => this["Pinyin.Resources"];
    public string SpeechSettings => this["Speech.Title"];
    public string VoicePacks => this["Voice.Title"];
    public string PrivacyNotice => this["Privacy.Title"];
    public string LearnWordHint => this["Learn.WordHint"];
    public string LearnTextHint => this["Learn.TextHint"];
    public string LearnGrammarHint => this["Learn.GrammarHint"];
    public string LearnPoemHint => this["Learn.PoemHint"];
    public string LearnDraftsTitle => this["Learn.DraftsTitle"];
    public string LearnDraftsHint => this["Learn.DraftsHint"];
    public string SettingsLanguageGroup => this["Settings.LanguageGroup"];
    public string SettingsContentGroup => this["Settings.ContentGroup"];
    public string SettingsAudioGroup => this["Settings.AudioGroup"];
    public string SettingsDataGroup => this["Settings.DataGroup"];
    public string SettingsUpdatesGroup => this["Update.Group"];
    public string CheckForUpdates => this["Update.Check"];
    public string ViewRelease => this["Update.ViewRelease"];
    public string this[string key] => AppResources.Get(key);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _changeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            var settings = await _settingsStore.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            var language = ReadLanguage(settings?.BodyJson) ?? _currentLanguage;
            if (settings is null)
            {
                await _settingsStore.SaveSettingsAsync(CreateSettingsJson(null, language), 0, cancellationToken).ConfigureAwait(false);
            }

            await MainThread.InvokeOnMainThreadAsync(() => ApplyCulture(language, notify: true));
            _initialized = true;
        }
        catch
        {
            // Keep the already applied system language; startup recovery UI is implemented with the startup flow.
        }
        finally
        {
            _changeGate.Release();
        }
    }

    public async Task<bool> ChangeLanguageAsync(UiLanguage language, CancellationToken cancellationToken = default)
    {
        await _changeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (language == _currentLanguage) return true;
            var settings = await _settingsStore.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            var expectedRevision = settings?.RowRevision ?? 0;
            var body = CreateSettingsJson(settings?.BodyJson, language);
            await _settingsStore.SaveSettingsAsync(body, expectedRevision, cancellationToken).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => ApplyCulture(language, notify: true));
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _changeGate.Release();
        }
    }

    public async Task ReloadSavedAsync()
    {
        await _changeGate.WaitAsync();
        try
        {
            var saved = await _settingsStore.GetSettingsAsync();
            await MainThread.InvokeOnMainThreadAsync(() => ApplyCulture(ReadLanguage(saved?.BodyJson) ?? _currentLanguage, true));
        }
        finally { _changeGate.Release(); }
    }

    private void ApplyCulture(UiLanguage language, bool notify)
    {
        _currentLanguage = language;
        var culture = CultureInfo.GetCultureInfo(UiLanguagePolicy.ToCultureName(language));
        AppResources.Culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        if (!notify) return;
        OnPropertyChanged(string.Empty);
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(CurrentCultureName));
        OnPropertyChanged(nameof(ShowAuxiliaryTranslations));
        OnPropertyChanged(nameof(AppName));
    }

    private static UiLanguage? ReadLanguage(string? bodyJson)
    {
        if (string.IsNullOrWhiteSpace(bodyJson)) return null;
        try
        {
            var root = JsonNode.Parse(bodyJson) as JsonObject;
            var value = root?["uiLanguage"]?.GetValue<string>();
            return value is null ? null : UiLanguagePolicy.ParsePersisted(value);
        }
        catch
        {
            return null;
        }
    }

    private static string CreateSettingsJson(string? existingJson, UiLanguage language)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(existingJson)
                ? new JsonObject()
                : JsonNode.Parse(existingJson) as JsonObject ?? new JsonObject();
        }
        catch
        {
            root = new JsonObject();
        }

        root["uiLanguage"] = UiLanguagePolicy.ToCultureName(language);
        return root.ToJsonString();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        // Persisting a language succeeded before notifications start. A stale window subscriber
        // must not abort delivery to the live window or report that successful save as cancelled.
        if (PropertyChanged is not { } changed) return;
        var args = new PropertyChangedEventArgs(propertyName);
        foreach (PropertyChangedEventHandler listener in changed.GetInvocationList())
        {
            try { listener(this, args); }
            catch (Exception error)
            {
                // Record type/stack only: no settings values, document text or exception message.
                Console.Error.WriteLine($"Language notification failed: {error.GetType().Name}\n{error.StackTrace}");
            }
        }
    }
}
