using System.Security.Cryptography;
using HanMate.Core.Audio;
using HanMate.Core.Pinyin;
using Plugin.Maui.Audio;

namespace HanMate.App.Audio;

public sealed class BundledPronunciationService : IAudioPlaybackBackend
{
    private readonly Lazy<Task<PinyinCourse>> _course = new(LoadAsync);
    public Task<PinyinCourse> GetCourseAsync() => _course.Value;

    private static async Task<PinyinCourse> LoadAsync()
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync("Pinyin/course.json");
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        return await Task.Run(() => PinyinCourse.Parse(json));
    }

    public async Task PlayAsync(string assetKey, CancellationToken cancellationToken)
    {
        var course = await GetCourseAsync();
        var asset = course.Data.Assets.Single(a => a.Key == assetKey);
        await PlayAssetAsync(asset, cancellationToken);
    }

    public static async Task PlayAssetAsync(PronunciationAsset asset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = await FileSystem.OpenAppPackageFileAsync(asset.File);
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes, cancellationToken);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes.GetBuffer().AsSpan(0, (int)bytes.Length))) != asset.Sha256)
            throw new InvalidDataException("Bundled recording hash mismatch.");
        bytes.Position = 0;
        // Player creation, cancellation and disposal stay on the UI thread for all native adapters.
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var player = AudioManager.Current.CreateAsyncPlayer(bytes);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(asset.DurationSeconds + 8));
            try { await player.PlayAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The native recording player did not finish in time.");
            }
            finally
            {
                try { try { player.Stop(); } finally { player.Dispose(); } }
                catch (Exception error) { throw new PlaybackCleanupException(error); }
            }
        });
    }
}
