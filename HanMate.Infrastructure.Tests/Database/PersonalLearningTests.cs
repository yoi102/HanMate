using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Infrastructure.Catalog;
using HanMate.Infrastructure.Database;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class PersonalLearningTests
{
    [Theory]
    [InlineData(ContentKind.Text)]
    [InlineData(ContentKind.Poem)]
    [InlineData(ContentKind.Grammar)]
    public async Task CanCreateEditAndTrashPersonalLearningContent(ContentKind kind)
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath);
        var store = new PersonalLearningStore(db); var catalog = new LearningCatalogStore(db);
        var input = Input(kind);
        var created = await store.SaveAsync(input);
        Assert.Equal(ContentOrigin.Personal, created.Origin);
        Assert.Equal(created.Id, Assert.Single((await catalog.QueryAsync(new(kind))).Items).Id);
        var edited = await store.SaveAsync(input with { Title = "修改后的标题",
            Body = kind == ContentKind.Grammar ? "" : "修改后的正文。" }, created);
        Assert.Equal(created.Id, edited.Id);
        Assert.Equal("修改后的标题", Assert.Single((await catalog.QueryAsync(new(kind))).Items).Title);
        var trash = new ContentTrashStore(db);
        await trash.MoveAsync(await trash.PreviewAsync(edited.Id));
        Assert.Empty((await catalog.QueryAsync(new(kind))).Items);
    }

    [Theory]
    [InlineData(ContentKind.Text)]
    [InlineData(ContentKind.Poem)]
    [InlineData(ContentKind.Grammar)]
    public async Task BuiltInEditCreatesVisibleCopyAndKeepsOriginal(ContentKind kind)
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath);
        await new BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
        var catalog = new LearningCatalogStore(db);
        var row = Assert.Single((await catalog.QueryAsync(new(kind))).Items.Take(1));
        var original = JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!;
        var favorites = new FavoriteStore(db); var folder = (await favorites.GetFoldersAsync())[0];
        await favorites.SetSelectionAsync(original.Id, [folder.Id], (await favorites.GetSelectionAsync(original.Id)).Revision);
        var store = new PersonalLearningStore(db);
        var input = kind == ContentKind.Grammar
            ? new PersonalLearningInput(kind, original.Title + " · 我的", "", "", EditorCommitStore.Pattern(original.Grammar!),
                original.TextUnits.Select(u => new LearningUnitInput(u.Id, u.Role, u.Text,
                    u.Translations.GetValueOrDefault("en", ""), u.Translations.GetValueOrDefault("ja", ""))).ToArray(),
                PatternEnglish: original.Grammar!.PatternTranslations.GetValueOrDefault("en", ""),
                PatternJapanese: original.Grammar!.PatternTranslations.GetValueOrDefault("ja", ""))
            : new PersonalLearningInput(kind, original.Title + " · 我的", original.TextUnits.Single().Text,
                kind == ContentKind.Poem ? original.Source.AuthorProvider : "", "", []);
        var copy = await store.SaveAsync(input, original);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(ContentOrigin.Personal, copy.Origin);
        Assert.DoesNotContain((await catalog.QueryAsync(new(kind))).Items, item => item.Id == original.Id);
        Assert.Contains((await catalog.QueryAsync(new(kind))).Items, item => item.Id == copy.Id);
        await using (var connection = await db.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT body_json FROM content WHERE id=$id";
            command.Parameters.AddWithValue("$id", original.Id.ToString("D"));
            Assert.Equal(row.BodyJson, (string)(await command.ExecuteScalarAsync())!);
        }
        Assert.Equal([folder.Id], (await favorites.GetSelectionAsync(copy.Id)).FolderIds);
    }

    [Theory]
    [InlineData(ContentKind.Text)]
    [InlineData(ContentKind.Poem)]
    [InlineData(ContentKind.Grammar)]
    public async Task BuiltInDeleteOnlyHidesResourceEntry(ContentKind kind)
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath);
        await new BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
        var catalog = new LearningCatalogStore(db);
        var row = (await catalog.QueryAsync(new(kind))).Items[0];
        await new PersonalLearningStore(db).HideResourceAsync(row.Id, kind);
        Assert.DoesNotContain((await catalog.QueryAsync(new(kind))).Items, item => item.Id == row.Id);
        Assert.NotNull(await new SqliteContentDocumentStore(db, new()).GetAsync(row.Id));
    }

    private static PersonalLearningInput Input(ContentKind kind) => kind == ContentKind.Grammar
        ? new(kind, "是字句", "", "", "{主语}是{名词}",
            [new(Guid.NewGuid(), TextUnitRole.GrammarExplanation, "用于说明身份。"),
             new(Guid.NewGuid(), TextUnitRole.Example, "我是学生。")])
        : new(kind, kind == ContentKind.Poem ? "春晓" : "我的课文", "春眠不觉晓。\n处处闻啼鸟。",
            kind == ContentKind.Poem ? "孟浩然" : "", "", [], "Spring", "春");
}
