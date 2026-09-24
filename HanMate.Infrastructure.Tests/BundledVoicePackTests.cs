using HanMate.Infrastructure.Voices;

namespace HanMate.Infrastructure.Tests;

public sealed partial class VoicePackTests
{
    [Theory]
    [InlineData("selected")] [InlineData("disabled")] [InlineData("removed")]
    public async Task ReplacementMigratesEnabledSpeakerButPreservesDisableAndRemoval(string choice)
    {
        using var http = new HttpClient(new Download(_ => throw new Exception("Unexpected network")));
        var root = Root(); var old = new VoicePackStore(root, http, Catalog, bundledFile: BundledFile);
        await old.StateAsync(); await old.SelectAsync(1);
        if (choice == "disabled") await old.SelectAsync(null);
        if (choice == "removed") await old.RemoveAsync();
        var next = Catalog with { Id = "replacement", ReplacesPackId = Catalog.Id, Speakers = 1 };
        var replaced = new VoicePackStore(root, http, next, bundledFile: BundledFile);
        Assert.Equal(choice != "removed", (await replaced.StateAsync()).Installed);
        Assert.Equal(choice == "selected" ? 0 : (int?)null, (await replaced.StateAsync()).Speaker);
        await replaced.RemoveAsync();
        Assert.False((await new VoicePackStore(root, http, next, bundledFile: BundledFile).StateAsync()).Installed);
    }
    [Fact]
    public async Task FailedReplacementLeavesOldSelectionAndCanResume()
    {
        using var http = new HttpClient(new Download(_ => throw new Exception("Unexpected network")));
        var root = Root(); var old = new VoicePackStore(root, http, Catalog, bundledFile: BundledFile);
        await old.StateAsync(); await old.SelectAsync(1);
        var next = Catalog with { Id = "replacement", ReplacesPackId = Catalog.Id, Speakers = 1 };
        var failed = new VoicePackStore(root, http, next, bundledFile: _ => Task.FromResult<Stream>(new MemoryStream([9])));
        await Assert.ThrowsAsync<InvalidDataException>(() => failed.StateAsync());
        Assert.Equal(1, (await old.StateAsync()).Speaker);
        Assert.Equal(0, (await new VoicePackStore(root, http, next, bundledFile: BundledFile).StateAsync()).Speaker);
    }
    [Fact]
    public async Task NestedDictionaryFilesAreVerifiedAndTraversalIsRejected()
    {
        using var http = new HttpClient(new Download(_ => throw new Exception("Unexpected network")));
        var next = Catalog with { Files = [Catalog.Files[0], Catalog.Files[0] with { Name = "dict/jieba.dict.utf8" }] };
        var store = new VoicePackStore(Root(), http, next, bundledFile: BundledFile);
        Assert.True((await store.StateAsync()).Installed);
        foreach (var name in new[] { "dict/../escape", "/absolute", "dict//empty", "dict/C:drive", "dict/./file" })
            Assert.Throws<InvalidDataException>(() => new VoicePackStore(Root(), http, next with { Files = [Catalog.Files[0] with { Name = name }] }));
    }
    private static Task<Stream> BundledFile(string name) => Task.FromResult<Stream>(new MemoryStream(Data));
    [Fact]
    public async Task SharingReadsVoiceChoiceWithoutUnpackingOrCheckingTheBundledModel()
    {
        var calls = 0;
        using var http = new HttpClient(new Download(_ => throw new Exception("Unexpected network")));
        var root = Root();
        var store = new VoicePackStore(root, http, Catalog, bundledFile: _ =>
        {
            calls++;
            throw new Exception("Sharing must not open the voice model.");
        });
        Assert.Null(await store.ReadSelectionAsync());
        Assert.False(Directory.Exists(root));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "selection.json"), "{\"Pack\":\"test-voice\",\"Speaker\":1}");
        Assert.Equal(1, await store.ReadSelectionAsync());
        Assert.False(Directory.Exists(Path.Combine(root, Catalog.Id)));
        Assert.Equal(0, calls);
    }
    [Fact]
    public async Task PrecisionReplacementPreservesCompatibleSpeakerAfterVerification()
    {
        using var http = new HttpClient(new Download(_ => throw new Exception("Unexpected network")));
        var root = Root(); var old = new VoicePackStore(root, http, Catalog, bundledFile: BundledFile);
        await old.StateAsync(); await old.SelectAsync(1);
        var next = Catalog with { Id = "fp32", ReplacesPackId = Catalog.Id,
            PreserveSpeakerOnReplace = true, Voices = [new(0, "one", "female"), new(1, "two", "male")] };
        var upgraded = new VoicePackStore(root, http, next, bundledFile: BundledFile);
        Assert.Equal(1, (await upgraded.StateAsync()).Speaker);
        Assert.True(Directory.Exists(Path.Combine(root, Catalog.Id)));
    }
    [Fact]
    public async Task BundledPackWorksOnFirstUseWithoutNetworkAndDefaultsOnlyOnce()
    {
        var handler = new Download(_ => throw new Exception("Unexpected network")); using var http = new HttpClient(handler);
        var root = Root(); var store = new VoicePackStore(root,http,Catalog,bundledFile:BundledFile);
        var states = await Task.WhenAll(Enumerable.Range(0,3).Select(_ => store.StateAsync()));
        Assert.All(states, state => Assert.Equal(new VoicePackState(true,0,true),state));
        await store.SelectAsync(1);
        Assert.Equal(1,(await new VoicePackStore(root,http,Catalog,bundledFile:BundledFile).StateAsync()).Speaker);
        await store.SelectAsync(null);
        Assert.Null((await new VoicePackStore(root,http,Catalog,bundledFile:BundledFile).StateAsync()).Speaker);
        await store.RemoveAsync();
        store = new(root,http,Catalog,bundledFile:BundledFile);
        Assert.False((await store.StateAsync()).Installed);
        await store.InstallAsync(null); Assert.Null((await store.StateAsync()).Speaker);
        await store.SelectAsync(1);
        Assert.Equal(Data,await store.UseAsync(1,path=>File.ReadAllBytesAsync(Path.Combine(path,"model.onnx")),default));
        Assert.Equal(0,handler.Calls);
    }
    [Theory]
    [InlineData("selected")] [InlineData("disabled")] [InlineData("removed")]
    public async Task BundledUpgradePreservesLegacyVoiceChoice(string choice)
    {
        var handler = new Download(_=>Task.FromResult(Data)); using var http = new HttpClient(handler);
        var root = Root(); var legacy = new VoicePackStore(root,http,Catalog);
        await legacy.InstallAsync(null); await legacy.SelectAsync(1);
        if(choice=="disabled") await legacy.SelectAsync(null);
        if(choice=="removed") await legacy.RemoveAsync();
        var upgraded = new VoicePackStore(root,http,Catalog,bundledFile:BundledFile);
        var state = await upgraded.StateAsync();
        Assert.Equal(choice!="removed",state.Installed);
        Assert.Equal(choice=="selected" ? 1 : (int?)null,state.Speaker);
        Assert.Equal(1,handler.Calls);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedBundledExtractionRetriesWithoutLosingNewInstallDefault(bool cancel)
    {
        var handler = new Download(_=>throw new Exception("Unexpected network")); using var http = new HttpClient(handler);
        var root = Root(); using var stop = new CancellationTokenSource();
        var broken = new VoicePackStore(root,http,Catalog,bundledFile: _ =>
        {
            if(cancel) stop.Cancel();
            return Task.FromResult<Stream>(new MemoryStream(new byte[4]));
        });
        await Assert.ThrowsAnyAsync<Exception>(()=>broken.StateAsync(stop.Token));
        Assert.False(Directory.Exists(Path.Combine(root,Catalog.Id)));
        Assert.Empty(Directory.EnumerateDirectories(root,"stage-*"));
        var retry = new VoicePackStore(root,http,Catalog,bundledFile:BundledFile);
        Assert.Equal(new VoicePackState(true,0,true),await retry.StateAsync());
        Assert.Equal(0,handler.Calls);
    }
}
