namespace HanMate.Core.Reading;

/// <summary>Visual whitespace cleanup only; original atoms and their text-element offsets stay immutable.</summary>
public static class DictionaryPresentation
{
    /// <summary>Drop only standalone empty sense markers from the view. Keep original offsets/readings.</summary>
    public static IReadOnlyList<IReadOnlyList<RubyAtom>> DefinitionParagraphs(IReadOnlyList<RubyAtom> atoms)
    {
        var paragraphs = new List<IReadOnlyList<RubyAtom>>();
        var line = new List<RubyAtom>();
        foreach (var atom in atoms)
        {
            if (atom.HardBreak) Flush();
            else line.Add(atom);
        }
        Flush();
        return paragraphs;

        void Flush()
        {
            var compact = Compact(line);
            var text = string.Concat(compact.Select(a => a.Text));
            // Do not delete bare numbers (which can be real definitions), or markers with content.
            if (compact.Count > 0 && !System.Text.RegularExpressions.Regex.IsMatch(text,
                    @"^(?:[（(][0-9０-９一二三四五六七八九十]+[)）]|[①-⒛]|[0-9０-９]+[.、])$"))
                paragraphs.Add(compact);
            line.Clear();
        }
    }

    public static IReadOnlyList<RubyAtom> Compact(IReadOnlyList<RubyAtom> atoms)
    {
        var result = new List<RubyAtom>();
        foreach (var atom in atoms)
        {
            if (atom.HardBreak)
            {
                while (result.Count > 0 && !result[^1].HardBreak && string.IsNullOrWhiteSpace(result[^1].Text)) result.RemoveAt(result.Count - 1);
                if (result.Count > 0 && !result[^1].HardBreak) result.Add(atom);
            }
            else if (!string.IsNullOrWhiteSpace(atom.Text) || (result.Count > 0 && !result[^1].HardBreak)) result.Add(atom);
        }
        while (result.Count > 0 && string.IsNullOrWhiteSpace(result[^1].Text)) result.RemoveAt(result.Count - 1);
        return result;
    }
}
