using System.Diagnostics;
using HanMate.Core.Audio;

namespace HanMate.App.Audio;

/// <summary>Owns native lifetime and the file writer until cleanup completes. Permission is requested by the explicit UI action.</summary>
public static class NativeWaveRecorder
{
    private const int Rate = 44100;
    public static async Task CaptureAsync(string path, Action<TimeSpan> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if ANDROID
        using var storage = new Android.OS.StatFs(Path.GetDirectoryName(path));
        if (storage.AvailableBytes < 8 * 1024 * 1024) throw new IOException("Insufficient recording space.");
        await Task.Run(() => CaptureAndroid(path, progress, cancellationToken));
#elif WINDOWS
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!);
        if (drive.IsReady && drive.AvailableFreeSpace < 8 * 1024 * 1024) throw new IOException("Insufficient recording space.");
        await CaptureWindowsAsync(path, progress, cancellationToken);
#elif IOS || MACCATALYST
        var storage = Foundation.NSFileManager.DefaultManager.GetFileSystemAttributes(Path.GetDirectoryName(path)!, out var storageError);
        if (storageError is not null || storage is null || storage.FreeSize < 8 * 1024 * 1024) throw new IOException("Insufficient recording space.");
        await CaptureAppleAsync(path, progress, cancellationToken);
#else
        await Task.FromException(new PlatformNotSupportedException());
#endif
    }

#if ANDROID
    private static void CaptureAndroid(string path, Action<TimeSpan> progress, CancellationToken token)
    {
        var size = Android.Media.AudioRecord.GetMinBufferSize(Rate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit);
        if (size <= 0) throw new NotSupportedException("PCM16 capture is unavailable.");
        using var recorder = new Android.Media.AudioRecord(Android.Media.AudioSource.Mic, Rate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit, Math.Max(size, 8192));
        if (recorder.State != Android.Media.State.Initialized) throw new IOException("Microphone initialization failed.");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        PcmWave.WriteHeader(output, Rate, 1, 0);
        var buffer = new byte[8192]; long bytes = 0;
        var limit = Math.Min(PcmWave.MaximumBytes - 44, 1200L * Rate * 2) / 2 * 2;
        var clock = Stopwatch.StartNew(); long nextProgress = 0;
        Exception? stopError = null;
        try
        {
            token.ThrowIfCancellationRequested(); recorder.StartRecording();
            // Stop unblocks a blocking native Read. The worker is still awaited before finalization.
            using var registration = token.Register(() => { try { recorder.Stop(); } catch (Exception error) { stopError = error; } });
            while (!token.IsCancellationRequested && bytes < limit)
            {
                var count = recorder.Read(buffer, 0, (int)Math.Min(buffer.Length, limit - bytes));
                if (count <= 0) { if (token.IsCancellationRequested) break; throw new IOException("Microphone read failed."); }
                if ((count & 1) != 0) throw new IOException("Incomplete PCM frame.");
                output.Write(buffer, 0, count); bytes += count;
                if (clock.ElapsedMilliseconds >= nextProgress) { output.Flush(); progress(TimeSpan.FromSeconds(bytes / (double)(Rate * 2))); nextProgress = clock.ElapsedMilliseconds + 250; }
            }
        }
        finally
        {
            try { if (recorder.RecordingState == Android.Media.RecordState.Recording) recorder.Stop(); }
            catch (Exception error) { stopError = error; }
            try { recorder.Release(); } catch (Exception error) { stopError = error; }
            if (stopError is not null) throw new PlaybackCleanupException(stopError);
            // Preserve complete captured frames for explicit recovery even when a read/interruption failed.
            PcmWave.WriteHeader(output, Rate, 1, bytes); output.Flush(true);
        }
    }
#endif

#if WINDOWS
    private static async Task CaptureWindowsAsync(string path, Action<TimeSpan> progress, CancellationToken token)
    {
        using var capture = new global::Windows.Media.Capture.MediaCapture();
        var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.Failed += (_, _) => failed.TrySetResult(true);
        capture.RecordLimitationExceeded += _ => failed.TrySetResult(true);
        bool started = false;
        try
        {
            await capture.InitializeAsync(new global::Windows.Media.Capture.MediaCaptureInitializationSettings { StreamingCaptureMode = global::Windows.Media.Capture.StreamingCaptureMode.Audio });
            token.ThrowIfCancellationRequested();
            using (File.Create(path)) { }
            var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var profile = global::Windows.Media.MediaProperties.MediaEncodingProfile.CreateWav(global::Windows.Media.MediaProperties.AudioEncodingQuality.Auto);
            profile.Audio = global::Windows.Media.MediaProperties.AudioEncodingProperties.CreatePcm(Rate, 1, 16);
            await capture.StartRecordToStorageFileAsync(profile, file); started = true;
            await MonitorAsync(path, progress, token, () => failed.Task.IsCompleted);
        }
        finally
        {
            if (started)
            {
                try { await capture.StopRecordAsync(); }
                catch (Exception error) { throw new PlaybackCleanupException(error); }
            }
        }
    }
#endif

#if IOS || MACCATALYST
    private static async Task CaptureAppleAsync(string path, Action<TimeSpan> progress, CancellationToken token)
    {
        // Native container selection follows the .wav suffix, not the pending journal suffix.
        var wavPath = path + ".wav";
        var session = AVFoundation.AVAudioSession.SharedInstance();
        AVFoundation.AVAudioRecorder? recorder = null;
        using var interrupted = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, interrupted.Token);
        using var observer = AVFoundation.AVAudioSession.Notifications.ObserveInterruption((_, _) => interrupted.Cancel());
        try
        {
            session.SetCategory(AVFoundation.AVAudioSessionCategory.PlayAndRecord, AVFoundation.AVAudioSessionCategoryOptions.DefaultToSpeaker, out var categoryError);
            if (categoryError is not null) throw new IOException(categoryError.LocalizedDescription);
            session.SetActive(true, out var activeError);
            if (activeError is not null) throw new IOException(activeError.LocalizedDescription);
            var settings = new AVFoundation.AudioSettings
            {
                Format = AudioToolbox.AudioFormatType.LinearPCM, SampleRate = Rate, NumberChannels = 1,
                LinearPcmBitDepth = 16, LinearPcmBigEndian = false, LinearPcmFloat = false
            };
            using var url = Foundation.NSUrl.FromFilename(wavPath);
            recorder = AVFoundation.AVAudioRecorder.Create(url, settings, out var error);
            if (recorder is null || error is not null) throw new IOException(error?.LocalizedDescription ?? "Recorder unavailable.");
            linked.Token.ThrowIfCancellationRequested();
            if (!recorder.PrepareToRecord() || !recorder.Record()) throw new IOException("Microphone did not start.");
            await MonitorAsync(wavPath, progress, linked.Token, () => !recorder.Recording);
        }
        finally
        {
            try { recorder?.Stop(); }
            catch (Exception error) { throw new PlaybackCleanupException(error); }
            finally
            {
                recorder?.Dispose(); session.SetActive(false, out var error);
                if (error is not null) throw new PlaybackCleanupException(new IOException(error.LocalizedDescription));
            }
        }
        File.Move(wavPath, path);
    }
#endif

#if WINDOWS || IOS || MACCATALYST
    private static async Task MonitorAsync(string path, Action<TimeSpan> progress, CancellationToken token, Func<bool> interrupted)
    {
        var clock = Stopwatch.StartNew();
        while (!token.IsCancellationRequested && !interrupted() && clock.Elapsed < TimeSpan.FromMinutes(20))
        {
            if (File.Exists(path) && new FileInfo(path).Length >= PcmWave.MaximumBytes - 256 * 1024) break;
            progress(clock.Elapsed);
            try { await Task.Delay(200, token); } catch (OperationCanceledException) { break; }
        }
    }
#endif
}
