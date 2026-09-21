using HanMate.Core.Content;
using HanMate.Core.Localization;

namespace HanMate.Core.Reading;

public sealed record LessonBlock(IReadOnlyList<RubyAtom> Atoms, ReadingTarget? Target, string? Translation);

/// <summary>Bounded reading rows with exact saved speech scopes, including non-speaking layout text.</summary>
public static class LessonPresentation
{
    public static IReadOnlyList<LessonBlock> Create(ReadingDocument reading, UiLanguage language, ReadingTarget? selected = null)
    {
        if (reading.Content.Kind is not (ContentKind.Text or ContentKind.Poem)) throw new ArgumentException("Expected a text or poem.", nameof(reading));
        var pages = selected is null ? reading.Pages : reading.PagesFor(selected);
        var blocks = new List<LessonBlock>();
        foreach (var part in pages.SelectMany(p => p.Parts))
        {
            for (var start = 0; start < part.Atoms.Count;)
            {
                var target = reading.TargetFor(part.Atoms[start]);
                var end = start + 1;
                while (end < part.Atoms.Count && reading.TargetFor(part.Atoms[end]) == target) end++;
                var atoms = part.Atoms.Skip(start).Take(end - start).ToArray();
                var segment = target is null ? null : reading.Segment(target);
                var endsSegment = segment is not null && atoms[^1].Start + atoms[^1].Length == segment.Start + segment.Length;
                blocks.Add(new(atoms, target, endsSegment ? UiLanguagePolicy.SelectAuxiliaryTranslation(segment!.Translations, language) : null));
                start = end;
            }
            // A whole-paragraph translation belongs only to the full view, never to one selected sentence.
            if (selected is null && part.EndsUnit && UiLanguagePolicy.SelectAuxiliaryTranslation(part.Unit.Translations, language) is { } translation)
                blocks.Add(new([], null, translation));
        }
        return blocks;
    }
}
