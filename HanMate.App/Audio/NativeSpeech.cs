using HanMate.Core.Audio;

namespace HanMate.App.Audio;

public sealed record LocalVoice(string Id, string Name, string Language)
{
    public override string ToString() => Name + " · " + Language;
}

/// <summary>Only enumerates installed Mandarin candidates. Actual offline quality remains a device acceptance check.</summary>
public static class NativeSpeech
{
    public const string Sample = "你好，欢迎学习汉语。";
    private static bool Mandarin(string? language) => MandarinVoiceLanguage.Matches(language);

    public static Task<IReadOnlyList<LocalVoice>> ListAsync(CancellationToken token) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        token.ThrowIfCancellationRequested();
#if WINDOWS
        await Task.CompletedTask;
        return (IReadOnlyList<LocalVoice>)global::Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices
            .Where(v => Mandarin(v.Language)).Select(v => new LocalVoice(v.Id, v.DisplayName, v.Language)).ToArray();
#elif ANDROID
        var (engine, listener) = await OpenAndroidAsync(token);
        using (engine) using (listener)
        {
            try
            {
                foreach (var v in (engine.Voices ?? []).Where(v => Mandarin(v.Locale?.ToLanguageTag())))
                    SpeechDiagnostics.Write($"system-voice language={v.Locale?.ToLanguageTag()} network={v.IsNetworkConnectionRequired} missing={v.Features?.Contains("notInstalled") ?? false}");
                return (IReadOnlyList<LocalVoice>)(engine.Voices ?? []).Where(Eligible)
                    .Select(v => new LocalVoice(AndroidVoiceId(v), v.Name!, v.Locale!.ToLanguageTag()!))
                    .DistinctBy(v => v.Id).OrderByDescending(v => v.Language is "zh-CN" or "zh-Hans" or "zh-Hans-CN")
                    .ThenBy(v => v.Id, StringComparer.Ordinal).ToArray();
            }
            finally { engine.Shutdown(); }
        }
#elif IOS || MACCATALYST
        await Task.CompletedTask;
        return (IReadOnlyList<LocalVoice>)AVFoundation.AVSpeechSynthesisVoice.GetSpeechVoices().Where(v => Mandarin(v.Language))
            .Select(v => new LocalVoice(v.Identifier, v.Name, v.Language)).ToArray();
#else
        await Task.CompletedTask;
        return (IReadOnlyList<LocalVoice>)Array.Empty<LocalVoice>();
#endif
    });

    public static Task PreviewAsync(LocalVoice voice, CancellationToken token) => SpeakAsync(voice, Sample, token);

    public static Task SpeakAsync(LocalVoice voice, string text, CancellationToken token) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500) throw new InvalidDataException("Speech chunk is invalid.");
        token.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(text.Length * 0.8 + 15, 30, 150)));
        try
        {
#if WINDOWS
            using var synthesizer = new global::Windows.Media.SpeechSynthesis.SpeechSynthesizer();
            synthesizer.Voice = global::Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.Single(v => v.Id == voice.Id && Mandarin(v.Language));
            using var speech = await synthesizer.SynthesizeTextToStreamAsync(text).AsTask(timeout.Token);
            using var stream = speech.AsStreamForRead();
            var player = Plugin.Maui.Audio.AudioManager.Current.CreateAsyncPlayer(stream);
            try { await player.PlayAsync(timeout.Token); }
            finally
            {
                try { try { player.Stop(); } finally { player.Dispose(); } }
                catch (Exception e) { throw new PlaybackCleanupException(e); }
            }
#elif ANDROID
            var (engine, listener) = await OpenAndroidAsync(timeout.Token);
            using (engine) using (listener) using (var progress = new SpeechProgress())
            {
                try
                {
                    var selected = (engine.Voices ?? []).First(v => AndroidVoiceId(v) == voice.Id && Eligible(v));
                    if (engine.SetVoice(selected) != Android.Speech.Tts.OperationResult.Success) throw new IOException("Voice unavailable.");
                    engine.SetOnUtteranceProgressListener(progress);
                    timeout.Token.ThrowIfCancellationRequested();
                    if (engine.Speak(text, Android.Speech.Tts.QueueMode.Flush, null, "hanmate-speech") != Android.Speech.Tts.OperationResult.Success)
                        throw new IOException("Speech rejected.");
                    await progress.Done.Task.WaitAsync(timeout.Token);
                }
                finally
                {
                    try { try { if (engine.Stop() != Android.Speech.Tts.OperationResult.Success) throw new IOException("Speech stop failed."); } finally { engine.Shutdown(); } }
                    catch (Exception e) { throw new PlaybackCleanupException(e); }
                }
            }
#elif IOS || MACCATALYST
            using var synthesizer = new AVFoundation.AVSpeechSynthesizer();
            using var utterance = new AVFoundation.AVSpeechUtterance(text);
            utterance.Voice = AVFoundation.AVSpeechSynthesisVoice.GetSpeechVoices().Single(v => v.Identifier == voice.Id && Mandarin(v.Language));
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            synthesizer.DidFinishSpeechUtterance += (_, _) => done.TrySetResult(true);
            synthesizer.DidCancelSpeechUtterance += (_, _) => done.TrySetCanceled();
            try { synthesizer.SpeakUtterance(utterance); await done.Task.WaitAsync(timeout.Token); }
            finally
            {
                if (synthesizer.Speaking && !synthesizer.StopSpeaking(AVFoundation.AVSpeechBoundary.Immediate))
                    throw new PlaybackCleanupException(new IOException("Speech stop failed."));
            }
#else
            await Task.CompletedTask;
            throw new PlatformNotSupportedException();
#endif
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Speech provider timed out."); }
    });

#if ANDROID
    private static string AndroidVoiceId(Android.Speech.Tts.Voice voice) => Uri.EscapeDataString(voice.Name ?? "") + "@" + voice.Locale!.ToLanguageTag();
    private static bool Eligible(Android.Speech.Tts.Voice v) => !v.IsNetworkConnectionRequired && Mandarin(v.Locale?.ToLanguageTag())
        && !(v.Features?.Contains("notInstalled") ?? false);
    private static async Task<(Android.Speech.Tts.TextToSpeech, SpeechInit)> OpenAndroidAsync(CancellationToken token)
    {
        var listener = new SpeechInit();
        var engine = new Android.Speech.Tts.TextToSpeech(Android.App.Application.Context, listener);
        try
        {
            var result = await listener.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            if (result != Android.Speech.Tts.OperationResult.Success) throw new IOException("Speech provider unavailable.");
            return (engine, listener);
        }
        catch { engine.Shutdown(); engine.Dispose(); listener.Dispose(); throw; }
    }
    private sealed class SpeechInit : Java.Lang.Object, Android.Speech.Tts.TextToSpeech.IOnInitListener
    {
        private readonly TaskCompletionSource<Android.Speech.Tts.OperationResult>? _ready;
        [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors, typeof(SpeechInit))]
        public SpeechInit() => _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Binder may deliver OnInit after timeout and Shutdown/Dispose. A rehydrated peer
        // must accept that callback without resurrecting a completed operation or UI.
        public SpeechInit(IntPtr handle, Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
        public TaskCompletionSource<Android.Speech.Tts.OperationResult> Ready => _ready!;
        public void OnInit(Android.Speech.Tts.OperationResult status) => _ready?.TrySetResult(status);
    }
    private sealed class SpeechProgress : Android.Speech.Tts.UtteranceProgressListener
    {
        private readonly TaskCompletionSource<bool>? _done;
        [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors, typeof(SpeechProgress))]
        public SpeechProgress() => _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SpeechProgress(IntPtr handle, Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
        public TaskCompletionSource<bool> Done => _done!;
        public override void OnStart(string? utteranceId) { }
        public override void OnDone(string? utteranceId) => _done?.TrySetResult(true);
        [Obsolete("Required by the Android abstract listener; use the error-code callback on current engines.")]
        public override void OnError(string? utteranceId) => _done?.TrySetException(new IOException("Speech synthesis failed."));
        public override void OnError(string? utteranceId, Android.Speech.Tts.TextToSpeechError errorCode) => _done?.TrySetException(new IOException("Speech synthesis failed."));
    }
#endif
}
