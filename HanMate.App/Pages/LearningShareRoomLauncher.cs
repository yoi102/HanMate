using HanMate.App.Audio;
using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Sharing;

namespace HanMate.App.Pages;

/// <summary>Exports exactly the supplied learning items and opens their Wi-Fi room.</summary>
internal static class LearningShareRoomLauncher
{
    public static Task<bool> OpenAsync(ContentPage page, IServiceProvider services, LocalizationService language, Guid contentId) =>
        OpenAsync(page, services, language, [contentId]);

    public static async Task<bool> OpenAsync(ContentPage page, IServiceProvider services, LocalizationService language,
        IReadOnlyCollection<Guid> contentIds)
    {
        string T(string key) => language["LanShare." + key];
        try
        {
            var share = services.GetRequiredService<ContentShareStore>();
            var plan = await Task.Run(() => share.PreviewAsync(contentIds));
            if (plan.Blocked > 0)
            {
                await page.DisplayAlertAsync(T("Share"), string.Format(T("Blocked"), plan.Blocked), language["Library.Cancel"]);
                return false;
            }
            var attest = plan.NeedAttestation > 0;
            if (attest && !await page.DisplayAlertAsync(T("Share"), T("ConfirmRights"),
                T("Share"), language["Library.Cancel"])) return false;

            var availableAudio = plan.AudioTracks.Where(track => track.CanShare).Select(track => track.Id).ToArray();
            var includeAudio = availableAudio.Length > 0 && await page.DisplayAlertAsync(T("ChooseAudio"),
                language["Transfer.AudioConsent"], T("WithAudio"), T("TextOnly"));
            var selectedAudio = includeAudio ? availableAudio : [];
            var files = services.GetRequiredService<ShareFileStore>();
            var file = await Task.Run(() => files.CreateAsync(".hanpack",
                stream => share.ExportAsync(plan, stream, attest, selectedAudio, includeAudio)));
            if (new FileInfo(file).Length > LanShareRoom.MaxPackageBytes)
            {
                File.Delete(file);
                await page.DisplayAlertAsync(T("Share"), T("PackageTooLarge"), language["Library.Cancel"]);
                return false;
            }
            var engine = SpeechPreferences.Engine;
            var speaker = engine == SpeechEngine.System ? null :
                await services.GetRequiredService<SpeechVoicePacks>().Get(engine).ReadSelectionAsync();
            var categoryStore = services.GetRequiredService<CustomWordCategoryStore>();
            var custom = (await categoryStore.ListAsync()).ToDictionary(x => x.Id, x => x.Name);
            var builtIn = (await categoryStore.ListBuiltInAsync()).ToDictionary(x => x.Id, x => x.Name);
            var wordCategories = plan.WordCategoryIds.Select(id => new LanWordCategory(id,
                custom.GetValueOrDefault(id) ?? builtIn.GetValueOrDefault(id) ??
                (id == WordCategories.Other || WordCategories.All.Contains(id)
                    ? language["WordCategories." + id] : id))).ToArray();
            await page.Navigation.PushAsync(new LanRoomPage(file,
                new(engine.ToString(), selectedAudio.Length, speaker, wordCategories), language));
            return true;
        }
        catch (PackageException error)
        {
            var message = error.Code switch
            {
                "PLAN_STALE" or "SHARE_SELECTION_INVALID" => T("Changed"),
                "SHARE_RIGHTS_REQUIRED" => T("RightsNeeded"),
                "PACKAGE_LIMIT_EXCEEDED" => T("PackageTooLarge"),
                _ => T("Failed")
            };
            await page.DisplayAlertAsync(T("Share"), message, language["Library.Cancel"]);
            return false;
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine(error);
            await page.DisplayAlertAsync(T("Share"), T("Failed"), language["Library.Cancel"]);
            return false;
        }
    }
}
