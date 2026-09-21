using System.Text.Json.Nodes;
using HanMate.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class LegacyMigrationTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task RealVersionOneFileMigratesWithForeignKeysReceiptsLocksAndIndexes(bool wal)
    {
        using var dir=new TestDatabaseDirectory();
        var document=await Seed(dir.DatabasePath,wal);
        await new HanMateDatabase(dir.DatabasePath).InitializeAsync();
        using var c=new SqliteConnection($"Data Source={dir.DatabasePath};Pooling=False"); await c.OpenAsync();
        Assert.Equal(2L,await Scalar(c,"PRAGMA user_version")); Assert.Equal("ok",await Scalar(c,"PRAGMA integrity_check"));
        using(var cmd=c.CreateCommand()) { cmd.CommandText="PRAGMA foreign_key_check"; using var r=await cmd.ExecuteReaderAsync(); Assert.False(await r.ReadAsync()); }
        var saved=JsonNode.Parse((string)(await Scalar(c,"SELECT body_json FROM content"))!)!;
        document["schemaVersion"]=2; Assert.True(JsonNode.DeepEquals(document,saved));
        Assert.Equal(7L,await Scalar(c,"SELECT row_revision FROM content")); Assert.Equal(1L,await Scalar(c,"SELECT membership_revision FROM content"));
        Assert.Equal(1L,await Scalar(c,"SELECT count(*) FROM favorite_item")); Assert.Equal(1L,await Scalar(c,"SELECT count(*) FROM import_receipt"));
        Assert.Equal(0L,await Scalar(c,"SELECT count(*) FROM import_operation")); Assert.Equal(0L,await Scalar(c,"SELECT alias_ordinal FROM search_index"));
        Assert.Equal(1L,await Scalar(c,"SELECT row_revision FROM draft")); Assert.Equal(1L,await Scalar(c,"SELECT row_revision FROM user_settings"));
        var safety=Assert.Single(Directory.GetFiles(Path.GetDirectoryName(dir.DatabasePath)!,"*.safety.sqlite"));
        using var old=new SqliteConnection($"Data Source={safety};Pooling=False"); await old.OpenAsync(); Assert.Equal(1L,await Scalar(old,"PRAGMA user_version"));
        await new HanMateDatabase(dir.DatabasePath).InitializeAsync();
    }
    [Theory]
    [InlineData("content")][InlineData("foreignKey")][InlineData("schema")]
    public async Task InvalidLegacyDataRollsBackWholeMigration(string damage)
    {
        using var dir=new TestDatabaseDirectory(); await Seed(dir.DatabasePath,false);
        using(var c=new SqliteConnection($"Data Source={dir.DatabasePath};Pooling=False"))
        {
            await c.OpenAsync();
            await Execute(c,damage switch { "content"=>"UPDATE content SET body_json=json_set(body_json,'$.textUnits[0].tokens[0].length',99999)", "foreignKey"=>"PRAGMA foreign_keys=OFF; UPDATE favorite_item SET content_id='missing'", _=>"ALTER TABLE content ADD COLUMN unexpected TEXT" });
        }
        await Assert.ThrowsAnyAsync<Exception>(()=>new HanMateDatabase(dir.DatabasePath).InitializeAsync());
        using var check=new SqliteConnection($"Data Source={dir.DatabasePath};Pooling=False"); await check.OpenAsync();
        Assert.Equal(1L,await Scalar(check,"PRAGMA user_version")); Assert.Equal(1L,await Scalar(check,"SELECT count(*) FROM content"));
        Assert.Equal(0L,await Scalar(check,"SELECT count(*) FROM sqlite_master WHERE name LIKE 'migration_%' OR name='installed_resource'"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(dir.DatabasePath)!,"*.safety.sqlite"));
    }
    private static async Task<JsonNode> Seed(string path,bool wal)
    {
        using var c=new SqliteConnection($"Data Source={path};Pooling=False"); await c.OpenAsync();
        if(wal) await Execute(c,"PRAGMA journal_mode=WAL");
        await Execute(c,await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Legacy/database.sql")));
        var doc=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Legacy/contents.json")))!["contents"]![0]!.DeepClone();
        using var cmd=c.CreateCommand(); cmd.CommandText="""
            INSERT INTO content VALUES($id,'word','personal',$title,$body,7,1,1,1,$hash,$created,$updated);
            INSERT INTO favorite_folder VALUES('folder','默认','默认','',0,'default');
            INSERT INTO favorite_item VALUES('folder',$id,3,'2026-09-18T00:00:00Z');
            INSERT INTO user_settings VALUES(1,1,'{"uiLanguage":"ja"}');
            INSERT INTO search_index VALUES($id,'你好','nihao','ni hao','ni3hao3','ni3 hao3','[]',1,1);
            INSERT INTO import_receipt VALUES('old-receipt',$hash,$hash,'2026-09-18T00:00:00Z','{}');
            INSERT INTO import_mapping VALUES('legacy','content',$id,$hash,$id,'old-receipt');
            INSERT INTO draft VALUES('old-draft',NULL,'content','{"original":"keep"}','2026-09-18T00:00:00Z');
            """;
        cmd.Parameters.AddWithValue("$id",doc["id"]!.GetValue<string>()); cmd.Parameters.AddWithValue("$title",doc["title"]!.GetValue<string>());
        cmd.Parameters.AddWithValue("$body",doc.ToJsonString()); cmd.Parameters.AddWithValue("$hash",new string('a',64));
        cmd.Parameters.AddWithValue("$created",doc["createdAtUtc"]!.GetValue<string>()); cmd.Parameters.AddWithValue("$updated",doc["updatedAtUtc"]!.GetValue<string>());
        await cmd.ExecuteNonQueryAsync(); return doc;
    }
    private static async Task Execute(SqliteConnection c,string sql) { using var cmd=c.CreateCommand(); cmd.CommandText=sql; await cmd.ExecuteNonQueryAsync(); }
    private static async Task<object?> Scalar(SqliteConnection c,string sql) { using var cmd=c.CreateCommand(); cmd.CommandText=sql; return await cmd.ExecuteScalarAsync(); }
}
