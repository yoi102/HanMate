using System.Diagnostics;
using System.Text.Json;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Pinyin;
using Microsoft.Data.Sqlite;

if (args.Length > 0 && args[0] is "stage-b" or "maintenance-child")
{ await MaintenanceProbe.RunAsync(args, Seed); return; }

if(args.Length>0 && args[0]=="inspect-backup")
{
    if(args.Length!=2) throw new ArgumentException("Usage: inspect-backup <file>");
    // Read-only inspection of system-saved bytes using the exact production validator.
    await using var input=File.OpenRead(args[1]);
    var package=await BackupPackageCodec.ReadAsync(input,CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(new { status="PASS", bytes=input.Length,
        sha256=package.Content.ArchiveHash, contents=package.Content.Documents.Count,
        assets=package.Content.Audio.Assets.Count, bindings=package.Content.Audio.Bindings.Count,
        folders=package.Collections.Folders.Count, favorites=package.Collections.Items.Count,
        resources=package.Resources.Resources.Count, teaching=package.Resources.TeachingItems?.Count??0 }));
    return;
}

if(args.Length>0 && args[0]=="child")
{
    var root=args[1]; var stage=args[2]; var db=new HanMateDatabase(Path.Combine(root,"target","hanmate.db"));
    var store=new BackupReplacementStore(db) { Checkpoint=s=> { if(s==stage) { File.WriteAllText(Path.Combine(root,"checkpoint"),s); Thread.Sleep(Timeout.Infinite); } } };
    using var input=File.OpenRead(Path.Combine(root,"incoming.hanbackup")); var plan=await store.PlanAsync(input); await store.PrepareSafetyAsync(plan);
    if(stage=="after-safety") { File.WriteAllText(Path.Combine(root,"checkpoint"),stage); Thread.Sleep(Timeout.Infinite); }
    await store.CommitAsync(plan); return;
}
var basePath=Path.Combine(Path.GetTempPath(),"HanMate-recovery-probe-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(basePath);
var results=new List<object>();
foreach(var stage in new[] { "after-safety","after-clear","before-commit","after-commit","sqlite-full" })
{
    var root=Path.Combine(basePath,stage); Directory.CreateDirectory(root);
    var target=new HanMateDatabase(Path.Combine(root,"target","hanmate.db")); var source=new HanMateDatabase(Path.Combine(root,"source","hanmate.db"));
    var old=await Seed(target,"old state"); var incoming=await Seed(source,"incoming state");
    var exporter=new BackupExportStore(source); await using(var output=File.Create(Path.Combine(root,"incoming.hanbackup"))) await exporter.ExportAsync(await exporter.PreviewAsync(),output,true);
    if(stage=="sqlite-full")
    {
        using var input=File.OpenRead(Path.Combine(root,"incoming.hanbackup")); var store=new BackupReplacementStore(target);
        var plan=await store.PlanAsync(input); await store.PrepareSafetyAsync(plan);
        using var c=await target.OpenConnectionAsync();
        await Execute(c,"CREATE TABLE fault_space(value BLOB); CREATE TRIGGER inject_full BEFORE INSERT ON import_operation BEGIN INSERT INTO fault_space VALUES(zeroblob(4194304)); END;");
        var pages=Convert.ToInt32(await Scalar(c,"PRAGMA page_count")); await Execute(c,$"PRAGMA max_page_count={pages+4}");
        try { await store.CommitAsync(plan); throw new Exception("Expected SQLITE_FULL."); }
        catch(SqliteException e) when(e.SqliteErrorCode==13) { }
        await Execute(c,"DROP TRIGGER inject_full");
    }
    else
    {
        var start=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,CreateNoWindow=true };
        start.ArgumentList.Add("child"); start.ArgumentList.Add(root); start.ArgumentList.Add(stage);
        using var process=Process.Start(start)!;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try { while(!File.Exists(Path.Combine(root,"checkpoint"))) { if(process.HasExited) throw new Exception("Child exited before checkpoint."); await Task.Delay(50,deadline.Token); } }
        finally { if(!process.HasExited) process.Kill(entireProcessTree:true); await process.WaitForExitAsync(); }
    }
    var reopened=new HanMateDatabase(target.DatabasePath); await reopened.InitializeAsync();
    using(var c=await reopened.OpenConnectionAsync())
    {
        Require(await Scalar(c,"PRAGMA integrity_check") as string=="ok","integrity");
        using var fk=c.CreateCommand(); fk.CommandText="PRAGMA foreign_key_check"; using(var r=await fk.ExecuteReaderAsync()) Require(!await r.ReadAsync(),"foreign keys");
        var committed=stage=="after-commit";
        Require(Convert.ToInt64(await Scalar(c,"SELECT count(*) FROM import_operation"))==(committed?1:0),"receipt");
        Require(await Scalar(c,"SELECT title FROM content WHERE origin='personal'") as string==(committed?"incoming state":"old state"),"content");
        var records=await new BackupReplacementStore(reopened).InspectRecoveryAsync(); Require(records.Count==1 && records[0].Committed==committed,"recovery classification");
        var targetId=committed?incoming.TextUnits[0].Id:old.TextUnits[0].Id;
        await using var lease=await new LocalAudioStore(reopened).OpenPlaybackAsync("target",targetId); Require(PcmWave.Inspect(lease.Stream).DurationMs==1000,"actual audio");
    }
    results.Add(new { stage,status="PASS" }); Console.WriteLine(stage+" PASS");
}
var report=Path.Combine(basePath,"report.json"); await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new { scenarios=results,scope="isolated host process kill and real SQLite SQLITE_FULL; not device volume exhaustion" },new JsonSerializerOptions { WriteIndented=true }));
Console.WriteLine(report);
static void Require(bool value,string check) { if(!value) throw new Exception("Failed: "+check); }
static async Task<ContentDocument> Seed(HanMateDatabase db,string title)
{
    var body=new TextDraft(title,"你好。",ContentKind.Text); var doc=DraftAnnotation.Generate(body,BundledAnnotationLexicon.Default.Engine).Document;
    var saved=(await new EditorCommitStore(db).CommitAsync(await new TextDraftStore(db).SaveAsync(Guid.NewGuid(),body with { Annotation=doc },0))).Document;
    var audio=new LocalAudioStore(db); var draft=await audio.BeginAsync(await audio.GetTargetAsync(saved.TextUnits[0].Id),"user");
    await using(var wave=File.Create(audio.CapturePath(draft.Id))) { PcmWave.WriteHeader(wave,8000,1,16000); await wave.WriteAsync(new byte[16000]); }
    await audio.SaveAsync(await audio.FinalizeAsync(draft.Id),"synthetic",true); return saved;
}
static async Task Execute(SqliteConnection c,string sql) { using var command=c.CreateCommand(); command.CommandText=sql; await command.ExecuteNonQueryAsync(); }
static async Task<object?> Scalar(SqliteConnection c,string sql) { using var command=c.CreateCommand(); command.CommandText=sql; return await command.ExecuteScalarAsync(); }
