using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Content;
using HanMate.Infrastructure.Packages;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

internal static class LegacyDatabaseMigration
{
    // A consistent SQLite backup (including WAL) is retained beside the database. Never copy a live main file.
    internal static async Task ApplyAsync(SqliteConnection c, string path, string schema, CancellationToken token)
    {
        var backup = path + ".v1-" + Guid.NewGuid().ToString("N") + ".safety.sqlite";
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false }.ToString()))
        {
            await destination.OpenAsync(token); c.BackupDatabase(destination);
            using var check = destination.CreateCommand(); check.CommandText = "PRAGMA integrity_check";
            if (await check.ExecuteScalarAsync(token) as string != "ok") throw new UnknownDatabaseSchemaException("Invalid migration safety copy.");
        }
        using var command = c.CreateCommand(); command.CommandText = "PRAGMA foreign_keys=OFF"; await command.ExecuteNonQueryAsync(token);
        try
        {
            using var tx = c.BeginTransaction(); command.Transaction = tx;
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            var tables = new List<string>();
            using (var r = await command.ExecuteReaderAsync(token)) while (await r.ReadAsync(token)) tables.Add(r.GetString(0));
            string[] expected = ["app_state","content","pinyin_item","playback_target","audio_asset","audio_binding","audio_preference","favorite_folder","favorite_item","user_settings","search_index","import_receipt","import_mapping","draft","file_lease"];
            if (!tables.ToHashSet().SetEquals(expected)) throw new UnknownDatabaseSchemaException("Unrecognized v1 tables; original preserved.");
            command.CommandText = "SELECT name,type FROM sqlite_master WHERE type IN ('index','trigger') AND sql IS NOT NULL";
            var objects = new List<(string Name,string Type)>();
            using (var r = await command.ExecuteReaderAsync(token)) while (await r.ReadAsync(token)) objects.Add((r.GetString(0),r.GetString(1)));
            foreach (var o in objects) await Run($"DROP {o.Type} {Quote(o.Name)}");
            foreach (var table in tables) await Run($"ALTER TABLE {Quote(table)} RENAME TO {Quote("migration_" + table)}");
            await Run(schema); await Run("DELETE FROM app_state");
            foreach (var table in tables)
            {
                command.CommandText = $"PRAGMA table_info({Quote("migration_" + table)})";
                var columns = new List<string>();
                using (var r = await command.ExecuteReaderAsync(token)) while (await r.ReadAsync(token)) columns.Add(Quote(r.GetString(1)));
                var names = string.Join(',',columns);
                await Run($"INSERT INTO {Quote(table)}({names}) SELECT {names} FROM {Quote("migration_" + table)}");
            }
            command.CommandText = "SELECT id,body_json FROM content";
            var docs = new List<(string Id, JsonNode Body)>();
            using (var r = await command.ExecuteReaderAsync(token)) while (await r.ReadAsync(token))
            {
                var n = PackageJson.Parse(System.Text.Encoding.UTF8.GetBytes(r.GetString(1)));
                PackageJson.Validate("Legacy.content.schema.json", n); n["schemaVersion"] = 2;
                PackageJson.Validate("content.schema.json", n);
                if (!new ContentDocumentValidator().Validate(n.Deserialize<ContentDocument>(ContentJson.Options)!).IsValid)
                    throw new UnknownDatabaseSchemaException("Invalid legacy content graph.");
                docs.Add((r.GetString(0), n));
            }
            foreach (var d in docs)
            {
                command.CommandText = "UPDATE content SET body_json=$body,semantic_fingerprint=$hash WHERE id=$id";
                command.Parameters.Clear(); command.Parameters.AddWithValue("$body", d.Body.ToJsonString());
                command.Parameters.AddWithValue("$hash", PackageJson.ContentFingerprint(d.Body)); command.Parameters.AddWithValue("$id", d.Id);
                await command.ExecuteNonQueryAsync(token);
            }
            command.Parameters.Clear();
            foreach (var table in tables) await Run($"DROP TABLE {Quote("migration_" + table)}");
            command.CommandText = "PRAGMA foreign_key_check";
            using (var r = await command.ExecuteReaderAsync(token)) if (await r.ReadAsync(token)) throw new UnknownDatabaseSchemaException("Legacy foreign key violation.");
            await Run("PRAGMA user_version=2"); token.ThrowIfCancellationRequested(); tx.Commit();
            async Task Run(string sql) { command.CommandText = sql; await command.ExecuteNonQueryAsync(token); }
        }
        finally { command.Transaction = null; command.Parameters.Clear(); command.CommandText = "PRAGMA foreign_keys=ON"; await command.ExecuteNonQueryAsync(CancellationToken.None); }
    }
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
}
