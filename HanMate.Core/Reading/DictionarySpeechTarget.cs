using HanMate.Core.Content;

namespace HanMate.Core.Reading;

/// <summary>A complete displayed paragraph or example, independent of visual pagination.</summary>
public sealed record DictionarySpeechTarget(string Identity, string Text, Guid? SourceUnitId, bool WholeUnit)
{
    public static DictionarySpeechTarget Create(ContentDocument document, IReadOnlyList<RubyAtom> atoms)
    {
        if (atoms.Count == 0 || atoms.Any(a => a.UnitId != atoms[0].UnitId))
            throw new ArgumentException("A dictionary passage must belong to one projected unit.", nameof(atoms));
        var text = string.Concat(atoms.Select(a => a.Text)).Trim();
        if (text.Length == 0) throw new ArgumentException("Empty dictionary passage.", nameof(atoms));
        var unit = document.TextUnits.FirstOrDefault(u => u.Id == atoms[0].UnitId);
        return new($"{document.Id}:{atoms[0].UnitId}:{atoms[0].Start}:{atoms[^1].Start + atoms[^1].Length}",
            text, unit?.Id, unit is not null && unit.Text.Trim() == text);
    }
}
