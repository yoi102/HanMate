using System.Net;
using System.Security.Cryptography;
using HanMate.Infrastructure.Voices;

namespace HanMate.Infrastructure.Tests;

public sealed partial class VoicePackTests
{
    private static readonly byte[] Data = [1, 3, 5, 7];
    private static VoicePack Catalog => new("test-voice", "test", "1", 2, 0, "test", "test",
        [new("model.onnx", "https://huggingface.co/test", Data.Length, Convert.ToHexStringLower(SHA256.HashData(Data)))]);
    private sealed class Download(Func<CancellationToken, Task<byte[]>> get) : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return new(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(await get(token)) }; }
    }
    private static string Root() => Path.Combine(Path.GetTempPath(), "HanMateVoiceTests", Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task VerifiedPackCacheDetectsChangedBytesAndMissingFilesBeforeUse()
    {
        var root = Root(); using var http = new HttpClient(new Download(_ => Task.FromResult(Data)));
        var store = new VoicePackStore(root, http, Catalog);
        await store.InstallAsync(null); await store.SelectAsync(0);
        Assert.True((await store.StateAsync()).Installed);
        Assert.True((await store.StateAsync()).Installed);
        var file = Path.Combine(root, Catalog.Id, "model.onnx");
        var written = File.GetLastWriteTimeUtc(file);
        await File.WriteAllBytesAsync(file, new byte[Data.Length]);
        File.SetLastWriteTimeUtc(file, written.AddSeconds(2));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UseAsync(0, _ => Task.FromResult(true), default));
        await store.InstallAsync(null);
        Assert.True((await store.StateAsync()).Installed);
        File.Delete(file);
        Assert.False((await store.StateAsync()).Installed);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UseAsync(0, _ => Task.FromResult(true), default));
    }
    [Fact]
    public async Task InstallationSelectionRemovalPreserveLearningDataAndDoNotRedownload()
    {
        var root = Root(); var handler = new Download(_ => Task.FromResult(Data)); using var http = new HttpClient(handler);
        var store = new VoicePackStore(root, http, Catalog);
        Assert.False((await store.StateAsync()).Installed);
        await store.InstallAsync(null); Assert.Null((await store.StateAsync()).Speaker);
        await store.SelectAsync(1); Assert.Equal(1, (await new VoicePackStore(root, http, Catalog).StateAsync()).Speaker);
        await store.InstallAsync(null); Assert.Equal(1, handler.Calls);
        await File.WriteAllTextAsync(Path.Combine(root, "learning.txt"), "keep");
        await store.RemoveAsync(); Assert.False((await store.StateAsync()).Installed);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(root, "learning.txt")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrOversizeDownloadNeverPublishesPartialPack(bool oversize)
    {
        var root = Root(); using var http = new HttpClient(new Download(_ => Task.FromResult(oversize ? new byte[9] : new byte[4])));
        var store = new VoicePackStore(root, http, Catalog);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallAsync(null));
        Assert.False((await store.StateAsync()).Installed); Assert.Empty(Directory.EnumerateDirectories(root));
    }
    [Fact]
    public async Task CancelledDownloadCanRetryAndDamagedModelCannotBeUsed()
    {
        var first = true; using var cancel = new CancellationTokenSource();
        using var http = new HttpClient(new Download(async token => { if (first) { first = false; cancel.Cancel(); await Task.Delay(50, token); } return Data; }));
        var root = Root(); var store = new VoicePackStore(root, http, Catalog);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.InstallAsync(null, cancel.Token));
        Assert.False((await store.StateAsync()).Installed);
        await store.InstallAsync(null); await store.SelectAsync(0);
        await File.WriteAllBytesAsync(Path.Combine(root, Catalog.Id, "model.onnx"), new byte[4]);
        Assert.False((await store.StateAsync()).Installed);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UseAsync(0, _ => Task.FromResult(1), default));
        await store.InstallAsync(null); Assert.True((await store.StateAsync()).Installed);
    }
    [Fact]
    public async Task DamagedPackRemainsRemovableAndDisableKeepsFilesAcrossRestart()
    {
        using var http = new HttpClient(new Download(_ => Task.FromResult(Data)));
        var root = Root(); var store = new VoicePackStore(root, http, Catalog);
        await store.InstallAsync(null); await store.SelectAsync(1); await store.SelectAsync(null);
        store = new(root, http, Catalog);
        Assert.Equal(new VoicePackState(true, null, true), await store.StateAsync());
        await File.WriteAllBytesAsync(Path.Combine(root, Catalog.Id, "model.onnx"), new byte[4]);
        Assert.Equal(new VoicePackState(false, null, true), await store.StateAsync());
        await store.RemoveAsync(); Assert.Equal(new VoicePackState(false, null, false), await store.StateAsync());
    }
    [Fact]
    public async Task UseLeasePreventsRemovalUntilInferenceReturns()
    {
        using var http = new HttpClient(new Download(_ => Task.FromResult(Data))); var store = new VoicePackStore(Root(), http, Catalog);
        await store.InstallAsync(null);
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var use = store.UseAsync(0, async _ => { entered.SetResult(); await release.Task; return true; }, default);
        await entered.Task; var remove = store.RemoveAsync(); Assert.False(remove.IsCompleted);
        release.SetResult(); Assert.True(await use); await remove; Assert.False((await store.StateAsync()).Installed);
    }
    [Fact]
    public void CatalogRejectsRemoteFileTraversalAndUnencryptedTransport()
    {
        using var http = new HttpClient();
        foreach (var file in new[] { Catalog.Files[0] with { Name = "../other" }, Catalog.Files[0] with { Url = "http://huggingface.co/test" } })
            Assert.Throws<InvalidDataException>(() => new VoicePackStore(Root(), http, Catalog with { Files = [file] }));
    }

    private sealed class StreamingDownload(Func<HttpRequestMessage, HttpResponseMessage> get) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(get(request));
    }
    private sealed class BrokenStream(bool stall) : MemoryStream(Data)
    {
        private bool _read;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (!_read) { _read = true; return await base.ReadAsync(buffer[..2], token); }
            if (stall) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new IOException("Injected body connection failure.");
        }
    }
    private static HttpResponseMessage Response(HttpRequestMessage request, Stream stream)
        => new(HttpStatusCode.OK) { RequestMessage = request, Content = new StreamContent(stream) };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StalledOrBrokenBodyNeverInstallsAndCanRetry(bool stall)
    {
        var first = true;
        using var http = new HttpClient(new StreamingDownload(request =>
        {
            var stream = first ? new BrokenStream(stall) : new MemoryStream(Data);
            first = false; return Response(request, stream);
        }));
        var root = Root(); var store = new VoicePackStore(root, http, Catalog, TimeSpan.FromMilliseconds(100));
        if (stall) await Assert.ThrowsAsync<TimeoutException>(() => store.InstallAsync(null));
        else await Assert.ThrowsAsync<IOException>(() => store.InstallAsync(null));
        Assert.False((await store.StateAsync()).Installed);
        Assert.Empty(Directory.EnumerateDirectories(root, "stage-*"));
        await store.InstallAsync(null); Assert.True((await store.StateAsync()).Installed);
    }

    [Fact]
    public async Task UserCancellationDuringBodyIsNotReportedAsTimeout()
    {
        using var http = new HttpClient(new StreamingDownload(request => Response(request, new BrokenStream(true))));
        var root = Root(); var store = new VoicePackStore(root, http, Catalog);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.InstallAsync(null, cancel.Token));
        Assert.False((await store.StateAsync()).Installed);
        Assert.Empty(Directory.EnumerateDirectories(root, "stage-*"));
    }

    [Fact]
    public async Task HttpTimeoutIsNotReportedAsUserCancellation()
    {
        using var http = new HttpClient(new Download(_ => throw new TaskCanceledException("Injected header timeout.")));
        var store = new VoicePackStore(Root(), http, Catalog);
        await Assert.ThrowsAsync<TimeoutException>(() => store.InstallAsync(null));
        Assert.False((await store.StateAsync()).Installed);
    }

    [Fact]
    public async Task InterruptedInstallRecoveryOnlyRemovesOwnedStageNames()
    {
        var root = Root(); var interrupted = Path.Combine(root, "stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(interrupted); await File.WriteAllTextAsync(Path.Combine(interrupted, "partial"), "unfinished");
        var unrelated = Path.Combine(root, "stage-not-an-install"); Directory.CreateDirectory(unrelated);
        await File.WriteAllTextAsync(Path.Combine(unrelated, "keep"), "keep");
        using var http = new HttpClient(new Download(_ => Task.FromResult(Data)));
        await new VoicePackStore(root, http, Catalog).InstallAsync(null);
        Assert.False(Directory.Exists(interrupted));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(unrelated, "keep")));
    }

    [Fact]
    public async Task CancelledRemovalWaitingForInferencePreservesSelectionAndFiles()
    {
        using var http = new HttpClient(new Download(_ => Task.FromResult(Data)));
        var store = new VoicePackStore(Root(), http, Catalog); await store.InstallAsync(null); await store.SelectAsync(1);
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var use = store.UseAsync(1, async _ => { entered.SetResult(); await release.Task; return true; }, default);
        await entered.Task;
        using var cancel = new CancellationTokenSource(); var remove = store.RemoveAsync(cancel.Token); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => remove);
        release.SetResult(); await use;
        Assert.Equal(new VoicePackState(true, 1, true), await store.StateAsync());
    }

    [Fact]
    public async Task WriteFailureNeverPublishesAndRetryPreservesLearningData()
    {
        // Real filesystem failure in an isolated stage, not a claim of physical full-disk validation.
        var root = Root(); var first = true;
        using var http = new HttpClient(new StreamingDownload(request =>
        {
            if (first)
            {
                first = false;
                Directory.CreateDirectory(Path.Combine(Directory.EnumerateDirectories(root, "stage-*").Single(), "model.onnx"));
            }
            return Response(request, new MemoryStream(Data));
        }));
        Directory.CreateDirectory(root); await File.WriteAllTextAsync(Path.Combine(root, "learning.txt"), "keep");
        var store = new VoicePackStore(root, http, Catalog);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.InstallAsync(null));
        Assert.False((await store.StateAsync()).Installed);
        Assert.Empty(Directory.EnumerateDirectories(root, "stage-*"));
        await store.InstallAsync(null);
        Assert.True((await store.StateAsync()).Installed);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(root, "learning.txt")));
    }
}
