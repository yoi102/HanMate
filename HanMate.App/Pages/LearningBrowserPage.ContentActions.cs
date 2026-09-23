using HanMate.App.Controls;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

public sealed partial class LearningBrowserPage
{
    private object CreateEditableContentCard(View content)
    {
        var swipe = new SwipeView { Content = LibraryLayout.CollectionRow(content) };
        var actions = new SwipeItems { Mode = SwipeMode.Reveal };
        actions.Add(new SwipeItem { Text = Language["LearningEdit.Edit"], BackgroundColor = Colors.SteelBlue,
            Command = new Command(async () =>
            {
                if (swipe.BindingContext is ContentDocument document) await EditLearningAsync(document);
            }) });
        actions.Add(new SwipeItem { Text = T("Delete"), BackgroundColor = Colors.IndianRed,
            Command = new Command(async () =>
            {
                if (swipe.BindingContext is ContentDocument document) await DeleteLearningAsync(document);
            }) });
        swipe.RightItems = actions;
        return swipe;
    }

    private async Task EditLearningAsync(ContentDocument document)
    {
        await RunAsync(async () =>
        {
            if (Handler?.MauiContext?.Services is not { } services) return;
            await Navigation.PushAsync(new PersonalLearningEditorPage(document.Kind, document,
                services.GetRequiredService<PersonalLearningStore>(), Language));
        });
    }

    private async Task DeleteLearningAsync(ContentDocument document)
    {
        await RunAsync(async () =>
        {
            var personal = document.Origin == ContentOrigin.Personal;
            if (!await DisplayAlertAsync(T("Delete"), Language[personal ? "LearningEdit.DeleteHint" : "LearningEdit.HideBuiltInHint"],
                T("Delete"), T("Cancel"))) return;
            if (Handler?.MauiContext?.Services is not { } services) return;
            if (personal)
            {
                var trash = services.GetRequiredService<ContentTrashStore>();
                await trash.MoveAsync(await trash.PreviewAsync(document.Id));
            }
            else await services.GetRequiredService<PersonalLearningStore>().HideResourceAsync(document.Id, document.Kind);
            await ReloadAsync();
        });
    }
}
