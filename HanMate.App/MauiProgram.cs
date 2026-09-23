using Microsoft.Extensions.Logging;

using HanMate.App.Localization;
using HanMate.App.Pages;
using HanMate.Core.Content;
using HanMate.Core.Contracts;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.App.Audio;
using HanMate.Core.Audio;

namespace HanMate.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif
#if ANDROID
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler(typeof(Shell), typeof(Platforms.Android.AccessibleShellRenderer)));
#elif WINDOWS
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler(typeof(Controls.PinyinExampleView), typeof(Platforms.Windows.PinyinExampleHandler)));
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler(typeof(ShellItem), typeof(Platforms.Windows.RootReturningShellItemHandler)));
#endif

        builder.Services.AddSingleton(_ => new HanMateDatabase(Path.Combine(FileSystem.AppDataDirectory, "hanmate.db")));
        builder.Services.AddSingleton<ContentDocumentValidator>();
        builder.Services.AddSingleton<SqliteContentDocumentStore>();
        builder.Services.AddSingleton<IContentDocumentStore>(services => services.GetRequiredService<SqliteContentDocumentStore>());
        builder.Services.AddSingleton<VersionedLocalStateStore>();
        builder.Services.AddSingleton<FavoriteStore>();
        builder.Services.AddSingleton<DictionaryBookmarkStore>();
        builder.Services.AddSingleton<LearningCatalogStore>();
        builder.Services.AddSingleton<CustomWordCategoryStore>();
        builder.Services.AddSingleton<PersonalWordStore>();
        builder.Services.AddSingleton<TextDraftStore>();
        builder.Services.AddSingleton<EditorCommitStore>();
        builder.Services.AddSingleton<PersonalLearningStore>();
        builder.Services.AddSingleton<ContentTrashStore>();
        builder.Services.AddSingleton<LocalAudioStore>();
        builder.Services.AddSingleton<ReadingPreferenceStore>();
        builder.Services.AddSingleton<ReadingAudioStore>();
        builder.Services.AddSingleton<SpeechPolicyStore>();
        builder.Services.AddSingleton<SpeechVoicePacks>();
        builder.Services.AddSingleton<TextSpeechService>();
        builder.Services.AddSingleton<AiVoiceService>();
        builder.Services.AddSingleton<ResourceStateStore>();
        builder.Services.AddSingleton<ResourceManagementStore>();
        builder.Services.AddSingleton<SearchAliasStore>();
        builder.Services.AddSingleton(_ => new HanMate.Infrastructure.Dictionary.DefaultDictionaryStore(Path.Combine(FileSystem.CacheDirectory, "dictionary"))
        { IsEnabled = Preferences.Default.Get(HanMate.Infrastructure.Dictionary.DefaultDictionaryStore.PreferenceKey, true) });
        builder.Services.AddSingleton<OfflineSearchStore>();
        builder.Services.AddSingleton<TextResourceInstaller>();
        builder.Services.AddSingleton<ContentShareStore>();
        builder.Services.AddSingleton<ContentPackageImportStore>();
        builder.Services.AddSingleton<BackupExportStore>();
        builder.Services.AddSingleton<BackupImportStore>();
        builder.Services.AddSingleton<BackupReplacementStore>();
        builder.Services.AddSingleton<SafetyRecoveryStore>();
        builder.Services.AddSingleton<OrphanAudioStore>();
        builder.Services.AddSingleton<TeachingCatalogStore>();
        builder.Services.AddSingleton(_ => new ShareFileStore(Path.Combine(FileSystem.CacheDirectory, "sharing")));
        builder.Services.AddSingleton<HanMate.Infrastructure.Catalog.BundledResourceCatalog>();
        builder.Services.AddSingleton<BundledPronunciationService>();
        builder.Services.AddSingleton<WordSpeechService>();
        builder.Services.AddSingleton<IAudioPlaybackBackend, LocalAudioBackend>();
        builder.Services.AddSingleton<PlaybackCoordinator>();
        builder.Services.AddSingleton<PinyinSpeechService>();
        builder.Services.AddSingleton<LocalizationService>();
        // A reopened Android window has a new MAUI context. Reusing a singleton
        // visual tree also reuses handlers tied to the destroyed window's scope.
        builder.Services.AddTransient<PinyinPage>();
        builder.Services.AddTransient<LearningPage>();
        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddTransient<FavoritesPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<AppShell>();

        return builder.Build();
    }
}
