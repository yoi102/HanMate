using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Tests.Database;

public sealed partial class BackupTests
{
    [Fact]
    public async Task ReplacementRequiresVerifiedSafetyAndRestoresAgainAfterLocalEdits()
    {
        using var source=new Area(); using var target=new Area(); var d=await source.Seed(); await source.Record(d);
        var old=await target.Seed(); await target.Record(old); await target.Install();
        var favorites=new FavoriteStore(source.Db); var folders=await favorites.GetFoldersAsync();
        await favorites.SetSelectionAsync(d.Id,[folders[0].Id],(await favorites.GetSelectionAsync(d.Id)).Revision);
        using var bytes=await source.Export(); var store=new BackupReplacementStore(target.Db); var plan=await store.PlanAsync(bytes);
        Assert.Equal(1,plan.DeletedContents); Assert.Equal(1,plan.DeletedAudio);
        Assert.Equal("BACKUP_SAFETY_REQUIRED",(await Assert.ThrowsAsync<PackageException>(()=>store.CommitAsync(plan))).Code);
        var safety=await store.PrepareSafetyAsync(plan);
        using(var c=new SqliteConnection($"Data Source={Path.Combine(safety,"hanmate.db")};Pooling=False"))
        { await c.OpenAsync(); using var cmd=c.CreateCommand(); cmd.CommandText="SELECT count(*) FROM content WHERE origin='personal'"; Assert.Equal(1L,await cmd.ExecuteScalarAsync()); }
        Assert.False(Assert.Single(await store.InspectRecoveryAsync()).Committed);
        await store.CommitAsync(plan); Assert.True(Assert.Single(await store.InspectRecoveryAsync()).Committed);
        Assert.Null(await new SqliteContentDocumentStore(target.Db,new()).GetAsync(old.Id));
        Assert.NotNull(await new SqliteContentDocumentStore(target.Db,new()).GetAsync(d.Id));
        Assert.Equal(1L,await target.Scalar("SELECT count(*) FROM installed_resource WHERE is_present=1"));
        Assert.Equal("replace",await target.Scalar("SELECT import_mode FROM import_operation"));
        await store.CommitAsync(plan); Assert.Equal(1L,await target.Scalar("SELECT count(*) FROM import_operation"));
        var saved=(await new SqliteContentDocumentStore(target.Db,new()).GetAsync(d.Id))!;
        await new SqliteContentDocumentStore(target.Db,new()).SaveAsync(saved.Document with { Title="local edit" },saved.RowRevision);
        bytes.Position=0; var again=await store.PlanAsync(bytes); Assert.NotEqual(plan.OperationId,again.OperationId);
        await store.PrepareSafetyAsync(again); await store.CommitAsync(again);
        Assert.Equal(d.Title,(await new SqliteContentDocumentStore(target.Db,new()).GetAsync(d.Id))!.Document.Title);
        Assert.Equal(2L,await target.Scalar("SELECT count(*) FROM import_operation"));
    }
    [Theory]
    [InlineData("settings")][InlineData("favorites")][InlineData("safety")]
    public async Task ReplacementRejectsStaleOrDamagedSafetyWithoutDeleting(string change)
    {
        using var a=new Area(); using var b=new Area(); await a.Seed(); var old=await b.Seed(); using var bytes=await a.Export();
        var store=new BackupReplacementStore(b.Db); var plan=await store.PlanAsync(bytes); var path=await store.PrepareSafetyAsync(plan);
        if(change=="settings") await new VersionedLocalStateStore(b.Db).SaveSettingsAsync("{\"uiLanguage\":\"ja\"}",0);
        if(change=="favorites") await new FavoriteStore(b.Db).SaveFolderAsync("changed","");
        if(change=="safety") await File.AppendAllTextAsync(Path.Combine(path,"hanmate.db"),"damage");
        await Assert.ThrowsAsync<PackageException>(()=>store.CommitAsync(plan));
        Assert.NotNull(await new SqliteContentDocumentStore(b.Db,new()).GetAsync(old.Id)); Assert.Equal(0L,await b.Scalar("SELECT count(*) FROM import_operation"));
    }
    [Fact]
    public async Task ReplacementBlocksDraftsAndActiveFileLease()
    {
        using var a=new Area(); using var b=new Area(); await a.Seed(); var d=await b.Seed(); await b.Record(d); using var bytes=await a.Export();
        var store=new BackupReplacementStore(b.Db);
        await using(var lease=await new LocalAudioStore(b.Db).OpenPlaybackAsync("target",d.TextUnits[0].Id))
            Assert.Equal("BACKUP_RECORDING_BUSY",(await Assert.ThrowsAsync<PackageException>(()=>store.PlanAsync(bytes))).Code);
        bytes.Position=0;
        await new TextDraftStore(b.Db).SaveAsync(Guid.NewGuid(),new("unsaved","你好",ContentKind.Text),0);
        Assert.Equal("BACKUP_DRAFTS_EXIST",(await Assert.ThrowsAsync<PackageException>(()=>store.PlanAsync(bytes))).Code);
    }
    [Fact]
    public async Task ReplacementFailureAfterDeletionRollsBackAndCanRetrySameOperation()
    {
        using var a=new Area(); using var b=new Area(); await a.Seed(); var old=await b.Seed(); using var bytes=await a.Export();
        var store=new BackupReplacementStore(b.Db); var plan=await store.PlanAsync(bytes); await store.PrepareSafetyAsync(plan);
        await b.Execute("CREATE TRIGGER fail_replace BEFORE INSERT ON import_operation BEGIN SELECT RAISE(ABORT,'failure after deletion'); END");
        await Assert.ThrowsAsync<SqliteException>(()=>store.CommitAsync(plan));
        Assert.NotNull(await new SqliteContentDocumentStore(b.Db,new()).GetAsync(old.Id)); Assert.False(Assert.Single(await store.InspectRecoveryAsync()).Committed);
        await b.Execute("DROP TRIGGER fail_replace"); await store.CommitAsync(plan); Assert.True(Assert.Single(await store.InspectRecoveryAsync()).Committed);
    }
    [Fact]
    public async Task ArchivedVersionOneBackupAndContentUseOriginalIdentityAndMigrate()
    {
        using var b=new Area();
        using(var input=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Legacy/sample-backup.hanbackup")))
        { var store=new BackupImportStore(b.Db); var p=await store.PlanAsync(input,true); await store.CommitAsync(p); }
        Assert.Equal(0L,await b.Scalar("SELECT count(*) FROM installed_resource"));
        Assert.Equal(0L,await b.Scalar("SELECT count(*) FROM content WHERE json_extract(body_json,'$.schemaVersion')<>2"));
        using(var input=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Legacy/sample-content.hanpack")))
        { var store=new ContentPackageImportStore(b.Db); var p=await store.PlanAsync(input); await store.CommitAsync(p); }
        Assert.Equal(2L,await b.Scalar("SELECT count(*) FROM import_receipt"));
    }
    [Fact]
    public async Task VersionOneOriginalHashAndSchemaAreCheckedBeforeConversion()
    {
        using var b=new Area(); using var input=new MemoryStream(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"Legacy/sample-backup.hanbackup")));
        using var broken=Rewrite(input,files=> { var n=JsonNode.Parse(files["contents.json"])!; n["contents"]![0]!["schemaVersion"]=2; files["contents.json"]=JsonSerializer.SerializeToUtf8Bytes(n); });
        await Assert.ThrowsAsync<PackageException>(()=>new BackupImportStore(b.Db).PlanAsync(broken)); Assert.False(File.Exists(b.Db.DatabasePath));
    }
    [Fact]
    public async Task LegacyBuiltinSnapshotRemainsSnapshotAndRepeatedImportReusesIt()
    {
        using var b=new Area(); using var input=new MemoryStream(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"Legacy/sample-content.hanpack")));
        using var snapshot=Rewrite(input,files=> { var n=JsonNode.Parse(files["contents.json"])!; n["contents"]![0]!["origin"]="builtinSnapshot"; files["contents.json"]=JsonSerializer.SerializeToUtf8Bytes(n); });
        var store=new ContentPackageImportStore(b.Db); var plan=await store.PlanAsync(snapshot); await store.CommitAsync(plan);
        Assert.Equal(1L,await b.Scalar("SELECT count(*) FROM content WHERE origin='builtinSnapshot'"));
        snapshot.Position=0; var repeated=await store.PlanAsync(snapshot); Assert.Equal(0,repeated.Added);
    }
    [Fact]
    public async Task ReplacementPreservesTeachingClosureAndInvalidatesOldRowRevisions()
    {
        using var a=new Area(); var doc=await a.Seed(); await a.Record(doc); var teaching=new TeachingCatalogStore(a.Db);
        using(var input=Teaching(doc)) await teaching.CommitAsync(await teaching.PlanAsync(input));
        using var backup=await a.Export(); var before=(await new SqliteContentDocumentStore(a.Db,new()).GetAsync(doc.Id))!;
        var store=new BackupReplacementStore(a.Db); var plan=await store.PlanAsync(backup); await store.PrepareSafetyAsync(plan); await store.CommitAsync(plan);
        var after=(await new SqliteContentDocumentStore(a.Db,new()).GetAsync(doc.Id))!; Assert.True(after.RowRevision>before.RowRevision);
        Assert.Equal(1L,await a.Scalar("SELECT count(*) FROM pinyin_item"));
        Assert.Single((await teaching.LoadAsync(new HanMate.Core.Pinyin.PinyinCourse(new("empty",[],[],[])))).Data.Items);
    }
}
