using System.Globalization;
using System.Text;
using HanMate.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed record FavoriteFolder(Guid Id, string Name, string Description, int Order, bool IsDefault, int Count);
public sealed record FavoriteEntry(Guid Id, string Title, string BodyJson, string SourceState);
public sealed record FavoriteSelection(long Revision, IReadOnlyList<Guid> FolderIds);

public sealed class FavoriteStore(HanMateDatabase database)
{
    public static readonly Guid DefaultId = Guid.Parse("27773085-7991-5e24-9b66-2c7281a94866");
    private async Task EnsureAsync(CancellationToken token)
    {
        await database.InitializeAsync(token); using var connection = await database.OpenConnectionAsync(token);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO favorite_folder(id,name,name_key,system_role,sort_order) SELECT $id,'default','default','default',-1 WHERE NOT EXISTS(SELECT 1 FROM favorite_folder WHERE system_role='default');";
        command.Parameters.AddWithValue("$id", DefaultId.ToString()); await command.ExecuteNonQueryAsync(token);
    }
    public async Task<IReadOnlyList<FavoriteFolder>> GetFoldersAsync(CancellationToken token = default)
    {
        await EnsureAsync(token); using var connection = await database.OpenConnectionAsync(token); using var command = connection.CreateCommand();
        command.CommandText = "SELECT f.id,f.name,f.description,f.sort_order,f.system_role,(SELECT count(*) FROM favorite_item i JOIN content c ON c.id=i.content_id WHERE i.folder_id=f.id AND " + ContentTrashStore.Active + ") FROM favorite_folder f ORDER BY f.sort_order,f.id";
        using var reader = await command.ExecuteReaderAsync(token); var rows = new List<FavoriteFolder>();
        while (await reader.ReadAsync(token)) rows.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), !reader.IsDBNull(4), reader.GetInt32(5)));
        return rows;
    }
    public async Task<Guid> SaveFolderAsync(string name, string description, FavoriteFolder? expected = null, CancellationToken token = default)
    {
        name = name.Trim().Normalize(NormalizationForm.FormC); description = description.Trim();
        if (new StringInfo(name).LengthInTextElements is < 1 or > 40 || new StringInfo(description).LengthInTextElements > 300 || expected?.IsDefault == true)
            throw new ArgumentException("Invalid folder name or description.");
        await EnsureAsync(token); using var connection = await database.OpenConnectionAsync(token); using var command = connection.CreateCommand();
        var id = expected?.Id ?? Guid.NewGuid();
        command.CommandText = expected is null
            ? "INSERT INTO favorite_folder(id,name,name_key,description,sort_order) VALUES($id,$name,$key,$description,(SELECT COALESCE(max(sort_order),0)+1 FROM favorite_folder));"
            : "UPDATE favorite_folder SET name=$name,name_key=$key,description=$description WHERE id=$id AND name=$oldName AND description=$oldDescription AND system_role IS NULL;";
        command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$key", name.Normalize(NormalizationForm.FormKC).ToUpperInvariant()); command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$oldName", expected?.Name ?? ""); command.Parameters.AddWithValue("$oldDescription", expected?.Description ?? "");
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new RevisionConflictException("folder", id.ToString(), 0);
        return id;
    }
    public async Task DeleteFolderAsync(FavoriteFolder folder, CancellationToken token = default)
    {
        if (folder.IsDefault) throw new InvalidOperationException("Default folder cannot be deleted.");
        await EnsureAsync(token); using var connection = await database.OpenConnectionAsync(token); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM favorite_folder WHERE id=$id AND name=$name AND description=$description AND system_role IS NULL";
        command.Parameters.AddWithValue("$id", folder.Id.ToString()); command.Parameters.AddWithValue("$name", folder.Name); command.Parameters.AddWithValue("$description", folder.Description);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 1) throw new RevisionConflictException("folder", folder.Id.ToString(), 0);
        command.CommandText = "UPDATE content SET membership_revision=membership_revision+1 WHERE id IN(SELECT content_id FROM favorite_item WHERE folder_id=$id); DELETE FROM favorite_folder WHERE id=$id;";
        await command.ExecuteNonQueryAsync(token); token.ThrowIfCancellationRequested(); transaction.Commit();
    }
    public async Task MoveAsync(Guid id, int direction, CancellationToken token = default)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        await EnsureAsync(token); using var connection = await database.OpenConnectionAsync(token); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT id FROM favorite_folder ORDER BY sort_order,id";
        var ids = new List<string>(); using (var reader = await command.ExecuteReaderAsync(token)) { while (await reader.ReadAsync(token)) ids.Add(reader.GetString(0)); }
        var index = ids.IndexOf(id.ToString()); var next = index + direction;
        if (index < 0 || next < 0 || next >= ids.Count) return;
        (ids[index], ids[next]) = (ids[next], ids[index]);
        for (var i = 0; i < ids.Count; i++)
        {
            command.Parameters.Clear(); command.CommandText = "UPDATE favorite_folder SET sort_order=$order WHERE id=$id";
            command.Parameters.AddWithValue("$order", i); command.Parameters.AddWithValue("$id", ids[i]); await command.ExecuteNonQueryAsync(token);
        }
        token.ThrowIfCancellationRequested(); transaction.Commit();
    }
    public async Task<FavoriteSelection> GetSelectionAsync(Guid contentId, CancellationToken token = default)
    {
        await EnsureAsync(token); using var connection = await database.OpenConnectionAsync(token); using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.Parameters.AddWithValue("$id", contentId.ToString());
        command.CommandText = "SELECT membership_revision FROM content WHERE id=$id";
        var revision = await command.ExecuteScalarAsync(token) ?? throw new InvalidOperationException("Content unavailable.");
        command.CommandText = "SELECT folder_id FROM favorite_item WHERE content_id=$id";
        using var reader = await command.ExecuteReaderAsync(token); var ids = new List<Guid>();
        while (await reader.ReadAsync(token)) ids.Add(Guid.Parse(reader.GetString(0)));
        return new(Convert.ToInt64(revision), ids);
    }
    public Task<long> SetSelectionAsync(Guid id, IReadOnlyCollection<Guid> folders, long revision, CancellationToken token = default) =>
        new FavoriteMembershipStore(database).SetMembershipAsync(id, folders, revision, DateTimeOffset.UtcNow, token);

    public async Task<IReadOnlyList<FavoriteEntry>> GetEntriesAsync(Guid folderId, int offset = 0, CancellationToken token = default)
    {
        await EnsureAsync(token); using var connection = await database.OpenConnectionAsync(token); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id,c.title,c.body_json,CASE WHEN c.origin='retained' THEN 'Retained'
              WHEN r.is_present=0 THEN 'Missing' WHEN o.removed=1 THEN 'Hidden' WHEN r.enabled=0 THEN 'Disabled' ELSE 'Ready' END
            FROM favorite_item f JOIN content c ON c.id=f.content_id
            LEFT JOIN resource_entry e ON e.content_id=c.id LEFT JOIN installed_resource r ON r.resource_id=e.resource_id
            LEFT JOIN resource_entry_override o ON o.resource_id=e.resource_id AND o.entry_id=e.entry_id
            WHERE f.folder_id=$id AND
            """ + " " + ContentTrashStore.Active + " ORDER BY f.sort_order,f.added_at_utc,c.id LIMIT 50 OFFSET $offset;";
        command.Parameters.AddWithValue("$id", folderId.ToString()); command.Parameters.AddWithValue("$offset", offset);
        using var reader = await command.ExecuteReaderAsync(token); var result = new List<FavoriteEntry>();
        while (await reader.ReadAsync(token)) result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return result;
    }
}
