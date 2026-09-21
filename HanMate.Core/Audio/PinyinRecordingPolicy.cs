namespace HanMate.Core.Audio;

/// <summary>Reported teaching errors must not be reintroduced by the optional AI voice.</summary>
public static class PinyinRecordingPolicy
{
    public static bool PreferRecording(string phonemes, string? recording, bool preferAi = false)
    {
        if (recording is null) return false;
        if (!preferAi) return true;
        if (recording.StartsWith("local:", StringComparison.Ordinal)) return true;
        var phones = phonemes.Split(' ');
        // Match complete syllables, including inside words and whole-syllable lessons.
        // Keep these on their matching recordings until the AI pronunciation is reviewed.
        for (var i = 0; i + 2 < phones.Length; i += 3)
            if ((phones[i] == "p" && phones[i + 1] is "a1" or "a4") ||
                (phones[i] == "ch" && phones[i + 1] is "iii1" or "iii4")) return true;
        return false;
    }
}
