using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Content;
using HanMate.Core.Resources;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Tests.Database;

namespace HanMate.Infrastructure.Tests.Content;

public sealed class TextResourceInstallerTests
{
    [Theory]
    [InlineData("sample-learning.hanresource", 4, ResourceKind.Learning)]
    [InlineData("sample-dictionary.handict", 3, ResourceKind.Dictionary)]
    public async Task InstallPersistsOneAtomicGraphAndMatchesIndependentPythonFingerprint(string file, int count, ResourceKind kind)
    {
        using var directory = new TestDatabaseDirectory();
        var db = new HanMateDatabase(directory.DatabasePath); var installer = new TextResourceInstaller(db);
        using var stream = Open(file); var plan = await installer.PlanAsync(stream);
        Assert.Equal(count, plan.ContentCount); Assert.Equal(kind, plan.Kind);
        Assert.Empty(await installer.GetLibraryAsync()); // preview never installs
        var result = await installer.InstallAsync(plan); Assert.False(result.AlreadyInstalled);
        Assert.True((await installer.InstallAsync(plan)).AlreadyInstalled);
        var reopened = new TextResourceInstaller(new(directory.DatabasePath));
        Assert.Equal(count, (await reopened.GetLibraryAsync()).Count);
        var saved = await new ResourceStateStore(db).GetAsync(plan.ResourceId); Assert.NotNull(saved);
        var states = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "resources.json")))!["resources"]!.AsArray();
        var expected = states.Single(s => s!["descriptor"]!["resourceId"]!.GetValue<string>() == plan.ResourceId.ToString("D"))!;
        Assert.Equal(expected["payloadFingerprint"]!.GetValue<string>(), saved.PayloadFingerprint);
        Assert.Equal(expected["descriptorSha256"]!.GetValue<string>(), saved.DescriptorSha256);
        Assert.Equal(1, await Scalar(db, "SELECT count(*) FROM resource_operation;"));
        Assert.Equal(count, await Scalar(db, "SELECT count(*) FROM resource_entry;"));
        Assert.True(await Scalar(db, "SELECT count(*) FROM playback_target;") >= count);
        Assert.Equal(kind == ResourceKind.Dictionary ? 3 : 1, await Scalar(db, "SELECT count(*) FROM search_index;"));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("path")]
    [InlineData("duplicate-path")]
    [InlineData("duplicate-json")]
    [InlineData("missing-field")]
    [InlineData("unknown-field")]
    [InlineData("bad-uuid")]
    [InlineData("wrong-owner")]
    [InlineData("entry-id")]
    [InlineData("counts")]
    [InlineData("audio")]
    [InlineData("grammar-ref")]
    [InlineData("non-word-dictionary")]
    [InlineData("duplicate-global-id")]
    [InlineData("invalid-utf8")]
    [InlineData("invalid-surrogate")]
    [InlineData("too-deep")]
    public async Task RejectsMalformedInputWithoutCreatingContent(string mutation)
    {
        using var directory = new TestDatabaseDirectory();
        var db = new HanMateDatabase(directory.DatabasePath); var installer = new TextResourceInstaller(db);
        using var input = Mutate(mutation);
        await Assert.ThrowsAsync<PackageException>(() => installer.PlanAsync(input));
        Assert.Empty(await installer.GetLibraryAsync());
        Assert.Equal(0, await Scalar(db, "SELECT count(*) FROM resource_operation;"));
    }

    [Fact]
    public async Task ExistingContentCollisionLeavesDatabaseUnchanged()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        using var archive = Open("sample-learning.hanresource"); using var zip = new ZipArchive(archive);
        using var content = zip.GetEntry("contents.json")!.Open(); var node = JsonNode.Parse(content)!;
        var conflict = node["contents"]!.AsArray()[1]!.Deserialize<ContentDocument>(ContentJson.Options)!;
        await new SqliteContentDocumentStore(db, new()).SaveAsync(conflict, 0);
        using var source = Open("sample-learning.hanresource"); var installer = new TextResourceInstaller(db); var plan = await installer.PlanAsync(source);
        await Assert.ThrowsAsync<PackageException>(() => installer.InstallAsync(plan));
        Assert.Equal(1, await Scalar(db, "SELECT count(*) FROM content;"));
        Assert.Equal(0, await Scalar(db, "SELECT count(*) FROM playback_target;"));
        Assert.Equal(0, await Scalar(db, "SELECT count(*) FROM installed_resource;"));
        Assert.Equal(0, await Scalar(db, "SELECT count(*) FROM resource_operation;"));
    }

    [Fact]
    public async Task ExistingTokenIdCollisionIsRejectedEvenWhenContentIdsDiffer()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        using var archive = Open("sample-learning.hanresource"); using var zip = new ZipArchive(archive);
        using var content = zip.GetEntry("contents.json")!.Open();
        var document = JsonNode.Parse(content)!["contents"]!.AsArray().Single(d => d!["kind"]!.GetValue<string>() == "word")!.Deserialize<ContentDocument>(ContentJson.Options)!;
        await new SqliteContentDocumentStore(db, new()).SaveAsync(document with
        {
            Id = Guid.NewGuid(),
            TextUnits = document.TextUnits.Select(u => u with { Id = Guid.NewGuid(), Segments = u.Segments.Select(s => s with { Id = Guid.NewGuid() }).ToArray() }).ToArray()
        }, 0);
        using var input = Open("sample-learning.hanresource"); var installer = new TextResourceInstaller(db);
        var plan = await installer.PlanAsync(input);
        var error = await Assert.ThrowsAsync<PackageException>(() => installer.InstallAsync(plan));
        Assert.Equal("RESOURCE_CONTENT_CONFLICT", error.Code);
        Assert.Equal(1, await Scalar(db, "SELECT count(*) FROM content;"));
        Assert.Equal(0, await Scalar(db, "SELECT count(*) FROM installed_resource;"));
    }

    [Fact]
    public async Task FailureAfterContentInsertionRollsBackWholeGraph()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        var installer = new TextResourceInstaller(db); using var input = Open("sample-learning.hanresource");
        var plan = await installer.PlanAsync(input);
        using (var connection = await db.OpenConnectionAsync())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER fail_receipt BEFORE INSERT ON resource_operation BEGIN SELECT RAISE(ABORT,'simulated late failure'); END;";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<PackageException>(() => installer.InstallAsync(plan));
        foreach (var table in new[] { "content", "playback_target", "search_index", "installed_resource", "resource_entry", "resource_operation" })
            Assert.Equal(0, await Scalar(db, $"SELECT count(*) FROM {table};"));
        Assert.Equal(plan.ExpectedEpoch, await Scalar(db, "SELECT data_epoch FROM app_state WHERE singleton=1;"));
    }

    [Fact]
    public async Task ReinstallDoesNotClaimSuccessWhenSavedBodyChanged()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        var installer = new TextResourceInstaller(db); using var input = Open("sample-learning.hanresource");
        var plan = await installer.PlanAsync(input); await installer.InstallAsync(plan);
        using (var connection = await db.OpenConnectionAsync())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE content SET body_json=json_set(body_json,'$.title','changed');";
            await command.ExecuteNonQueryAsync();
        }
        var error = await Assert.ThrowsAsync<PackageException>(() => installer.InstallAsync(plan));
        Assert.Equal("RESOURCE_INCOMPLETE", error.Code);
        Assert.Equal(1, await Scalar(db, "SELECT count(*) FROM resource_operation;"));
    }

    [Fact]
    public async Task SupportsNonSeekableFilePickerStreamAndLeavesItOwnedByCaller()
    {
        using var directory = new TestDatabaseDirectory(); using var input = new NonSeekableStream(Open("sample-learning.hanresource"));
        var installer = new TextResourceInstaller(new(directory.DatabasePath));
        var plan = await installer.PlanAsync(input); Assert.Equal(4, plan.ContentCount);
        Assert.True(input.CanRead);
        Assert.Empty(await installer.GetLibraryAsync());
    }

    [Fact]
    public async Task OversizedAndCancelledInputCreateNoInstalledContent()
    {
        using var directory = new TestDatabaseDirectory(); var installer = new TextResourceInstaller(new(directory.DatabasePath));
        using var oversized = new MemoryStream(new byte[32 * 1024 * 1024 + 1]);
        var error = await Assert.ThrowsAsync<PackageException>(() => installer.PlanAsync(oversized));
        Assert.Equal("PACKAGE_LIMIT_EXCEEDED", error.Code);
        using var input = Open("sample-learning.hanresource");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.PlanAsync(input, new CancellationToken(true)));
        Assert.Empty(await installer.GetLibraryAsync());
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    [Fact]
    public async Task StalePlanAndCancelledInstallLeaveOtherResourceIntact()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath); var installer = new TextResourceInstaller(db);
        using var a = Open("sample-learning.hanresource"); using var b = Open("sample-dictionary.handict");
        var first = await installer.PlanAsync(a); var second = await installer.PlanAsync(b);
        await installer.InstallAsync(first);
        var exception = await Assert.ThrowsAsync<PackageException>(() => installer.InstallAsync(second)); Assert.Equal("PLAN_STALE", exception.Code);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(second, new CancellationToken(true)));
        Assert.Equal(4, await Scalar(db, "SELECT count(*) FROM content;"));
    }

    [Fact]
    public async Task SameVersionDifferentPayloadCannotOverwriteInstalledResource()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath); var installer = new TextResourceInstaller(db);
        using var first = Open("sample-learning.hanresource"); await installer.InstallAsync(await installer.PlanAsync(first));
        using var changed = Mutate("title"); var plan = await installer.PlanAsync(changed);
        var exception = await Assert.ThrowsAsync<PackageException>(() => installer.InstallAsync(plan));
        Assert.Equal("RESOURCE_VERSION_COLLISION", exception.Code);
        Assert.DoesNotContain(await installer.GetLibraryAsync(), c => c.Title == "changed");
    }

    private static FileStream Open(string file) => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
    private static async Task<long> Scalar(HanMateDatabase database, string sql)
    {
        await database.InitializeAsync(); using var connection = await database.OpenConnectionAsync(); using var command = connection.CreateCommand();
        command.CommandText = sql; return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static MemoryStream Mutate(string mutation)
    {
        using var source = Open("sample-learning.hanresource"); using var archive = new ZipArchive(source);
        var files = archive.Entries.ToDictionary(e => e.FullName, e => { using var stream = e.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray(); });
        var manifest = JsonNode.Parse(files["manifest.json"])!; var descriptor = JsonNode.Parse(files["resource.json"])!;
        var contents = JsonNode.Parse(files["contents.json"])!; var documents = contents["contents"]!.AsArray();
        switch (mutation)
        {
            case "title": documents[0]!["title"] = "changed"; break;
            case "missing-field": descriptor.AsObject().Remove("publisherId"); break;
            case "unknown-field": descriptor["run"] = "untrusted"; break;
            case "bad-uuid": descriptor["resourceId"] = "not-a-uuid"; break;
            case "wrong-owner": documents[0]!["source"]!["resourceVersion"] = "9.0.0"; break;
            case "entry-id": descriptor["entries"]![0]!["entryId"] = "changed"; break;
            case "counts": manifest["counts"]!["contents"] = 123; break;
            case "audio": descriptor["audioBindingIds"]!.AsArray().Add(Guid.NewGuid().ToString("D")); break;
            case "grammar-ref": documents.First(d => d!["kind"]!.GetValue<string>() == "grammar")!["grammar"]!["exampleUnitIds"]![0] = Guid.NewGuid().ToString("D"); break;
            case "non-word-dictionary": descriptor["resourceKind"] = "dictionary"; break;
            case "duplicate-global-id": documents[1]!["textUnits"]![0]!["id"] = documents[0]!["textUnits"]![0]!["id"]!.DeepClone(); break;
        }
        files["resource.json"] = Encoding.UTF8.GetBytes(descriptor.ToJsonString()); files["contents.json"] = Encoding.UTF8.GetBytes(contents.ToJsonString());
        if (mutation == "duplicate-json") files["resource.json"] = Encoding.UTF8.GetBytes(descriptor.ToJsonString().Replace("{", "{\"schemaVersion\":1,", StringComparison.Ordinal));
        if (mutation == "invalid-utf8") files["resource.json"][5] = 0xff;
        if (mutation == "invalid-surrogate") files["resource.json"] = Encoding.UTF8.GetBytes(descriptor.ToJsonString().Replace("\"publisherName\":", "\"publisherName\":\"\\uD800\",\"invalid\":"));
        if (mutation == "too-deep") files["resource.json"] = Encoding.UTF8.GetBytes(new string('[', 33) + "0" + new string(']', 33));
        foreach (var entry in manifest["files"]!.AsArray())
        {
            var bytes = files[entry!["path"]!.GetValue<string>()]; entry["byteLength"] = bytes.Length; entry["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        if (mutation == "hash") files["contents.json"][5] ^= 1;
        files["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in files)
            {
                var path = mutation == "path" && name == "resource.json" ? "../resource.json" : name;
                if (mutation == "duplicate-path" && name == "resource.json") path = "manifest.json";
                using var stream = zip.CreateEntry(path).Open(); stream.Write(bytes);
            }
        }
        output.Position = 0; return output;
    }
}
