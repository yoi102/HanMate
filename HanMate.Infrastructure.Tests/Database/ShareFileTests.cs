using HanMate.Infrastructure.Packages;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class ShareFileTests
{
    [Fact]
    public async Task FailedExportCleansOnlyItsPartialFileAndRetainsCompletedHandoff()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HanMate-share-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ShareFileStore(directory); var original = await store.CreateAsync(".wav", s => s.WriteAsync("safe"u8.ToArray()).AsTask());
            await Assert.ThrowsAsync<IOException>(() => store.CreateAsync(".hanpack", async s => { await s.WriteAsync("partial"u8.ToArray()); throw new IOException("injected"); }));
            Assert.Equal(new[] { original }, Directory.GetFiles(directory)); Assert.Equal("safe", await File.ReadAllTextAsync(original));
            File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddDays(-8)); var next = await store.CreateAsync(".hanpack", _ => Task.CompletedTask);
            Assert.False(File.Exists(original)); Assert.True(File.Exists(next));
            await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync("../escape", _ => Task.CompletedTask));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
