using HanMate.App.Controls;
using HanMate.Core.Content;
using HanMate.Core.Localization;
using HanMate.Core.Reading;

namespace HanMate.App.Pages;

public sealed partial class LearningBrowserPage
{
    private object CreateGrammarCard()
    {
        var preview = new VerticalStackLayout { Spacing = 6 };
        var row = LibraryLayout.ContentRow(nameof(ContentDocument.Title), open =>
        {
            open.AutomationId = "Learning.GrammarItem";
            SemanticProperties.SetHint(open, T("Read"));
            open.Clicked += async (_, _) =>
            {
                if (open.BindingContext is not ContentDocument document || Handler?.MauiContext?.Services is not { } services) return;
                await RunAsync(async () =>
                {
                    await Navigation.PushAsync(await LearningDetailPageFactory.CreateAsync(document, Language, services));
                });
            };
        }, subtitle: preview);
        row.BindingContextChanged += (_, _) =>
        {
            preview.Clear();
            if (row.BindingContext is not ContentDocument { Grammar: { } grammar }) return;
            preview.Add(new Label { Text = string.Join(" ", grammar.PatternParts.Select(p => p.Text)), FontSize = 16 });
            if (UiLanguagePolicy.SelectAuxiliaryTranslation(grammar.PatternTranslations, Language.CurrentLanguage) is { } translation)
                preview.Add(LibraryLayout.Muted(translation));
        };
        return CreateEditableContentCard(row);
    }
}
