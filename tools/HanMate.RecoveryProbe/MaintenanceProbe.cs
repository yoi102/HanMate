using System.Diagnostics;
using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using Microsoft.Data.Sqlite;

internal static class MaintenanceProbe
{
    private sealed class FutureClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddDays(8); }
    internal static async Task RunAsync(string[] args, Func<HanMateDatabase, string, Task<ContentDocument>> seed)
    {
        if (args[0] == "maintenance-child")
        {
            var root = args[1]; var stage = args[2]; var db = new HanMateDatabase(Path.Combine(root, "hanmate.db"));
            void Checkpoint(string reached)
            { if (stage == reached) { File.WriteAllText(Path.Combine(root, "checkpoint"), reached); Thread.Sleep(Timeout.Infinite); } }
            if (stage.StartsWith("gc-", StringComparison.Ordinal))
            {
                var store = new OrphanAudioStore(db) { Clock = new FutureClock(), Checkpoint = Checkpoint };
                await store.CleanAsync(await store.ScanAsync());
            }
            else
            {
                var id = Guid.Parse(await File.ReadAllTextAsync(Path.Combine(root, "source-id")));
                var store = new SafetyRecoveryStore(db) { Checkpoint = Checkpoint };
                var plan = await store.PlanAsync(id); await store.PrepareAsync(plan); Checkpoint("restore-after-safety");
                await store.RestoreAsync(plan);
            }
            return;
        }
        var basePath = Path.Combine(Path.GetTempPath(), "HanMate-stage-b-crash-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(basePath);
        var results = new List<object>();
        foreach (var stage in new[] { "restore-after-safety", "restore-after-clear", "restore-before-commit", "restore-after-commit", "restore-sqlite-full", "gc-after-metadata", "gc-after-delete" })
        {
            var root = Path.Combine(basePath, stage); Directory.CreateDirectory(root); var db = new HanMateDatabase(Path.Combine(root, "hanmate.db"));
            var doc = await seed(db, "snapshot state"); Guid sourceId = default;
            if (stage.StartsWith("gc-", StringComparison.Ordinal))
            {
                using var c = await db.OpenConnectionAsync(); await Execute(c, "DELETE FROM audio_binding");
                await new OrphanAudioStore(db).ScanAsync();
            }
            else
            {
                var exporter = new BackupExportStore(db); using var bytes = new MemoryStream();
                await exporter.ExportAsync(await exporter.PreviewAsync(), bytes, true); bytes.Position = 0;
                var replacement = new BackupReplacementStore(db); var snapshot = await replacement.PlanAsync(bytes); await replacement.PrepareSafetyAsync(snapshot);
                sourceId = snapshot.OperationId; await File.WriteAllTextAsync(Path.Combine(root, "source-id"), sourceId.ToString());
                var documents = new SqliteContentDocumentStore(db, new()); var current = (await documents.GetAsync(doc.Id))!;
                await documents.SaveAsync(current.Document with { Title = "current state" }, current.RowRevision);
            }
            if (stage == "restore-sqlite-full")
            {
                var store = new SafetyRecoveryStore(db); var plan = await store.PlanAsync(sourceId); await store.PrepareAsync(plan);
                using var c = await db.OpenConnectionAsync();
                await Execute(c, "CREATE TABLE fault_space(value BLOB); CREATE TRIGGER inject_full BEFORE INSERT ON import_operation BEGIN INSERT INTO fault_space VALUES(zeroblob(4194304)); END;");
                var pages = Convert.ToInt32(await Scalar(c, "PRAGMA page_count")); await Execute(c, $"PRAGMA max_page_count={pages + 4}");
                try { await store.RestoreAsync(plan); throw new Exception("Expected SQLITE_FULL"); } catch (SqliteException e) when (e.SqliteErrorCode == 13) { }
                await Execute(c, "DROP TRIGGER inject_full");
            }
            else
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("maintenance-child"); start.ArgumentList.Add(root); start.ArgumentList.Add(stage);
                using var process = Process.Start(start)!; using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                try
                {
                    while (!File.Exists(Path.Combine(root, "checkpoint")))
                    { if (process.HasExited) throw new Exception("Child exited before " + stage); await Task.Delay(50, deadline.Token); }
                }
                finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            }
            var reopened = new HanMateDatabase(db.DatabasePath); await reopened.InitializeAsync();
            using (var c = await reopened.OpenConnectionAsync())
            {
                Require(await Scalar(c, "PRAGMA integrity_check") as string == "ok", "integrity");
                using (var cmd = c.CreateCommand()) { cmd.CommandText = "PRAGMA foreign_key_check"; using var r = await cmd.ExecuteReaderAsync(); Require(!await r.ReadAsync(), "foreign keys"); }
                if (stage.StartsWith("gc-", StringComparison.Ordinal))
                {
                    Require(Convert.ToInt64(await Scalar(c, "SELECT count(*) FROM audio_asset")) == 0, "metadata committed before file delete");
                    var waveCount = Directory.EnumerateFiles(Path.Combine(root, "audio", "local"), "*.wav").Count();
                    Require(waveCount == (stage == "gc-after-metadata" ? 1 : 0), "media state");
                    var retry = new OrphanAudioStore(reopened) { Clock = new FutureClock() }; await retry.CleanAsync(await retry.ScanAsync());
                    Require(!Directory.EnumerateFiles(Path.Combine(root, "audio", "local"), "*.wav").Any(), "retry cleanup");
                }
                else
                {
                    var committed = stage == "restore-after-commit";
                    Require(await Scalar(c, "SELECT title FROM content") as string == (committed ? "snapshot state" : "current state"), "content atomicity");
                    Require(Convert.ToInt64(await Scalar(c, "SELECT count(*) FROM import_operation WHERE json_extract(result_json,'$.format')='safetyRestore.v1'")) == (committed ? 1 : 0), "receipt atomicity");
                    var snapshots = await new SafetyRecoveryStore(reopened).ListAsync(); Require(snapshots.Count == 2 && snapshots.All(x => x.Status == "valid"), "both snapshots preserved");
                    await using var lease = await new LocalAudioStore(reopened).OpenPlaybackAsync("target", doc.TextUnits[0].Id); Require(lease.Stream.Length > 44, "actual media readable");
                }
            }
            results.Add(new { stage, status = "PASS" }); Console.WriteLine(stage + " PASS");
        }
        var report = Path.Combine(basePath, "report.json");
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { scenarios = results, scope = "isolated host process kill and real SQLite SQLITE_FULL; not device volume exhaustion" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(report);
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Execute(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(); }
    private static async Task<object?> Scalar(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return await cmd.ExecuteScalarAsync(); }
}
