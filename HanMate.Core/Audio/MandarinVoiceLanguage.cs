namespace HanMate.Core.Audio;

public static class MandarinVoiceLanguage
{
    public static bool Matches(string? language) => language?.Replace('_', '-').ToLowerInvariant() is
        "zh" or "zh-hans" or "zh-hant" or "zh-cn" or "zh-tw" or "zh-sg"
        or "zh-hans-cn" or "zh-hant-tw" or "zh-hans-sg"
        or "cmn" or "cmn-cn" or "cmn-tw" or "cmn-hans" or "cmn-hant" or "cmn-hans-cn" or "cmn-hant-tw";
}
