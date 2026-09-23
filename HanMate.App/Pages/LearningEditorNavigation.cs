using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

internal static class LearningEditorNavigation
{
    public static Task OpenAsync(Page detail, ContentDocument document, LocalizationService language, IServiceProvider services)
    {
        Page editor = document.Kind == ContentKind.Word
            ? new PersonalWordEditorPage(WordCategory(document), document,
                services.GetRequiredService<PersonalWordStore>(),
                services.GetRequiredService<CustomWordCategoryStore>(), language, detail)
            : new PersonalLearningEditorPage(document.Kind, document,
                services.GetRequiredService<PersonalLearningStore>(), language, detail);
        return detail.Navigation.PushAsync(editor);
    }

    public static async Task FinishAsync(Page editor, Page? sourceDetail, ContentDocument saved,
        LocalizationService language, IServiceProvider? services)
    {
        var navigation = editor.Navigation;
        if (sourceDetail is null || services is null || !navigation.NavigationStack.Contains(sourceDetail))
        {
            await navigation.PopAsync();
            return;
        }
        // Replace the old detail in the navigation stack. A resource edit creates a new ID,
        // and a personal edit changes the saved content; neither should return to stale rows.
        var updatedDetail = await LearningDetailPageFactory.CreateAsync(saved, language, services);
        navigation.InsertPageBefore(updatedDetail, editor);
        await navigation.PopAsync();
        navigation.RemovePage(sourceDetail);
    }

    private static string WordCategory(ContentDocument document) => document.Scenes
        .Where(scene => scene.StartsWith("word-", StringComparison.Ordinal))
        .Select(scene => scene[5..])
        .FirstOrDefault(category => WordCategories.All.Contains(category) || CustomWordCategoryStore.IsCustom(category))
        ?? WordCategories.Other;
}
