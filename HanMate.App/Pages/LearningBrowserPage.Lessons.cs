using HanMate.App.Controls;
using HanMate.Core.Content;
using HanMate.Core.Localization;
using HanMate.Core.Reading;

namespace HanMate.App.Pages;

public sealed partial class LearningBrowserPage
{
    private object CreateLessonCard()
    {
        var preview = new VerticalStackLayout { Spacing = 6 };
        var row = LibraryLayout.ContentRow(nameof(ContentDocument.Title), open =>
        {
            open.AutomationId = "Learning.LessonItem";
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
            if (row.BindingContext is not ContentDocument document) return;
            var unit = document.TextUnits.FirstOrDefault();
            if (unit is null) return;
            var segment = unit.Segments.FirstOrDefault(s => s.Kind == SegmentKind.Speech);
            var segmentTranslation = segment is null ? null : UiLanguagePolicy.SelectAuxiliaryTranslation(segment.Translations, Language.CurrentLanguage);
            var previewText = segmentTranslation is null ? unit.Text : segment!.Text;
            // Restrict the preview without splitting a text element or changing the stored content.
            var boundaries = TextElementMap.CreateUtf16Boundaries(previewText);
            var text = TextElementMap.Slice(previewText, boundaries, 0, Math.Min(80, boundaries.Count - 1));
            preview.Add(new Label { Text = text, FontSize = 16, MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation });
            if ((segmentTranslation ?? UiLanguagePolicy.SelectAuxiliaryTranslation(unit.Translations, Language.CurrentLanguage)) is { } translation)
            {
                var auxiliary = LibraryLayout.Muted(translation); auxiliary.MaxLines = 2;
                auxiliary.LineBreakMode = LineBreakMode.TailTruncation; preview.Add(auxiliary);
            }
        };
        return CreateEditableContentCard(row);
    }
}
