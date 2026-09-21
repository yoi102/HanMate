using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Core.Reading;

namespace HanMate.App.Pages;

/// <summary>Learning lists and favorites open the same detail for the same saved content.</summary>
internal static class LearningDetailPageFactory
{
    public static async Task<Page> CreateAsync(ContentDocument document, LocalizationService language, IServiceProvider services)
    {
        if (document.Kind == ContentKind.Word)
            return new DictionaryEntryPage(document, language, services, allowEditing: true, learningContent: true);
        var reading = await Task.Run(() => new ReadingDocument(document));
        return document.Kind switch
        {
            ContentKind.Text or ContentKind.Poem => new LessonDetailPage(reading, language, services),
            ContentKind.Grammar => new GrammarDetailPage(reading, language, services),
            _ => throw new ArgumentException("Unsupported learning content.", nameof(document))
        };
    }
}
