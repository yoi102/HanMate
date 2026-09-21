using HanMate.Infrastructure.Voices;

namespace HanMate.App.Audio;

public sealed class SpeechVoicePacks
{
    private readonly VoicePackStore _melo = new(Path.Combine(FileSystem.AppDataDirectory, "voices"),
        new HttpClient(), bundledFile: name => FileSystem.OpenAppPackageFileAsync("Voices/Melo/" + name));
    private readonly VoicePackStore _kokoro = new(Path.Combine(FileSystem.AppDataDirectory, "voices-kokoro"),
        new HttpClient(), VoicePack.Kokoro, bundledFile: name => FileSystem.OpenAppPackageFileAsync("Voices/Kokoro/" + name));
    public VoicePackStore Get(SpeechEngine engine) => engine switch
    { SpeechEngine.Melo => _melo, SpeechEngine.Kokoro => _kokoro, _ => throw new ArgumentException("System voices have no model pack.", nameof(engine)) };
}
