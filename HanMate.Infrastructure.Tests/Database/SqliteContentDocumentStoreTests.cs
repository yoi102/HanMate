using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Contracts;
using HanMate.Infrastructure.Database;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class SqliteContentDocumentStoreTests
{
    [Fact]
    public async Task Save_PersistsAcrossDatabaseRestartAndRejectsStaleRevision()
    {
        using var directory = new TestDatabaseDirectory();
        var document = LoadGrammarFixture();
        var firstStore = CreateStore(directory.DatabasePath);

        var created = await firstStore.SaveAsync(document, expectedRowRevision: 0);
        Assert.Equal(1, created.RowRevision);

        var restartedStore = CreateStore(directory.DatabasePath);
        var restarted = await restartedStore.GetAsync(document.Id);
        Assert.NotNull(restarted);
        Assert.Equal(
            JsonSerializer.Serialize(document, ContentJson.Options),
            JsonSerializer.Serialize(restarted.Document, ContentJson.Options));
        Assert.Equal(1, restarted.RowRevision);

        var updatedDocument = document with { Title = document.Title + " · updated", UpdatedAtUtc = document.UpdatedAtUtc.AddMinutes(1) };
        var updated = await restartedStore.SaveAsync(updatedDocument, expectedRowRevision: 1);
        Assert.Equal(2, updated.RowRevision);
        await Assert.ThrowsAsync<RevisionConflictException>(() => restartedStore.SaveAsync(updatedDocument, expectedRowRevision: 1));
    }

    private static SqliteContentDocumentStore CreateStore(string path) =>
        new(new HanMateDatabase(path), new ContentDocumentValidator());

    private static ContentDocument LoadGrammarFixture() =>
        JsonSerializer.Deserialize<ContentDocument>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "grammar-content.json")),
            ContentJson.Options)!;
}
