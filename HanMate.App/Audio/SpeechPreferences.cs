namespace HanMate.App.Audio;

public enum SpeechEngine { Melo, Kokoro, System }

/// <summary>Device-only provider and voice preferences are not exported in learning backups.</summary>
public static class SpeechPreferences
{
    public static SpeechEngine Engine
    {
        get => Preferences.Default.Get("speech.engine.v1", "melo") switch
        { "kokoro" => SpeechEngine.Kokoro, "system" => SpeechEngine.System, _ => SpeechEngine.Melo };
        set => Preferences.Default.Set("speech.engine.v1", value switch
        { SpeechEngine.Melo => "melo", SpeechEngine.Kokoro => "kokoro", SpeechEngine.System => "system", _ => throw new ArgumentOutOfRangeException(nameof(value)) });
    }
    public static string SystemVoiceId
    {
        get => Preferences.Default.Get("speech.system-voice.v1", "");
        set => Preferences.Default.Set("speech.system-voice.v1", value);
    }
}
