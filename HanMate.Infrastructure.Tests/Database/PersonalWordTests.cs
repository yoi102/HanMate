using HanMate.Core.Content;
using HanMate.Infrastructure.Catalog;
using HanMate.Infrastructure.Database;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class PersonalWordTests
{
    [Fact]
    public async Task CustomCategoryAndWordSurviveReopenEditDeleteAndTrash()
    {
        using var dir = new TestDatabaseDirectory();
        var db = new HanMateDatabase(dir.DatabasePath);
        var categories = new CustomWordCategoryStore(new VersionedLocalStateStore(db));
        var category = await categories.CreateAsync("我的词语");
        Assert.Equal(category, Assert.Single(await new CustomWordCategoryStore(new VersionedLocalStateStore(
            new HanMateDatabase(dir.DatabasePath))).ListAsync()));
        var words = new PersonalWordStore(db);
        var input = new PersonalWordInput(category.Id, "一条", "用于细长物体的量词",
            [new("一条鱼"), new("河里有一条鱼。")], "one", "一本", false);
        var original = await words.SaveAsync(input);
        var head = original.TextUnits.Single(x => x.Role == TextUnitRole.Headword);
        Assert.All(head.Tokens.Where(x => x.Kind == TokenKind.Hanzi),
            x => Assert.NotNull(x.Pinyin));
        Assert.Equal("one", head.Translations["en"]);
        var originalExamples = original.TextUnits.Where(x => x.Role == TextUnitRole.Example).ToArray();
        Assert.Equal(new[] { "一条鱼", "河里有一条鱼。" }, originalExamples.Select(x => x.Text));
        var detail = HanMate.Infrastructure.Dictionary.DictionaryDetailProjection.Create(original, sourceOnly: true);
        Assert.Equal("用于细长物体的量词", Assert.Single(detail.Definitions).Text);
        Assert.Equal(new[] { "一条鱼", "河里有一条鱼。" }, detail.Examples.Select(x => x.Text));
        Assert.Contains(PersonalWordStore.AutoVoiceScene, original.Scenes);
        var catalog = new LearningCatalogStore(db);
        Assert.Equal(original.Id, Assert.Single((await catalog.QueryAsync(new(ContentKind.Word, WordCategory: category.Id))).Items).Id);
        var edited = await words.SaveAsync(input with { Definition = "量词", UseRecording = true,
            Examples = [new("一条鱼", originalExamples[0].Id), new("池里有一条鱼。", originalExamples[1].Id)] }, original.Id);
        Assert.Equal(head.Id, edited.TextUnits.Single(x => x.Role == TextUnitRole.Headword).Id);
        Assert.Equal(originalExamples.Select(x => x.Id), edited.TextUnits.Where(x => x.Role == TextUnitRole.Example).Select(x => x.Id));
        Assert.DoesNotContain(PersonalWordStore.AutoVoiceScene, edited.Scenes);
        var destination = await categories.CreateAsync("复习");
        await words.SaveAsync(input with { Category = destination.Id }, original.Id);
        Assert.Empty((await catalog.QueryAsync(new(ContentKind.Word, WordCategory: category.Id))).Items);
        Assert.Equal(original.Id, Assert.Single((await catalog.QueryAsync(new(ContentKind.Word, WordCategory: destination.Id))).Items).Id);
        await words.SaveAsync(input, original.Id);
        await categories.RenameAsync(category.Id, "收藏的词");
        Assert.Equal("收藏的词", (await categories.ListAsync()).Single(x => x.Id == category.Id).Name);
        await categories.DeleteAsync(category.Id);
        Assert.Equal(original.Id, Assert.Single((await catalog.QueryAsync(new(ContentKind.Word, WordCategory: WordCategories.Other))).Items).Id);
        var moved = await words.SaveAsync(input with { Category = WordCategories.Other }, original.Id);
        Assert.DoesNotContain(moved.Scenes, CustomWordCategoryStore.IsCustom);
        var trash = new ContentTrashStore(db);
        await trash.MoveAsync(await trash.PreviewAsync(original.Id));
        Assert.Equal(0, (await catalog.GetWordCategoryCountsAsync()).Total);
    }

    [Fact]
    public async Task ManualPinyinIsSavedAndChangingWordRegeneratesReading()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath);
        var category = await new CustomWordCategoryStore(new VersionedLocalStateStore(db)).CreateAsync("发音");
        var words = new PersonalWordStore(db);
        var input = new PersonalWordInput(category.Id, "一条", "量词", [], "", "", false)
        { PinyinCorrections = [new(0, "yi3"), new(1, "tiao2")] };
        var saved = await words.SaveAsync(input);
        var tokens = saved.TextUnits.Single(x => x.Role == TextUnitRole.Headword).Tokens;
        Assert.Equal(new[] { "yǐ", "tiáo" }, tokens.Select(x => x.Pinyin?.Display));
        Assert.All(tokens, x => Assert.Equal(AnnotationReviewState.Confirmed, x.ReviewState));
        var changed = await words.SaveAsync(input with { Word = "一只", PinyinCorrections = [] }, saved.Id);
        Assert.Equal("一只", changed.Title);
        Assert.NotEqual("yǐ", changed.TextUnits.Single(x => x.Role == TextUnitRole.Headword).Tokens[0].Pinyin?.Display);
        await Assert.ThrowsAnyAsync<Exception>(() => words.SaveAsync(input with
        { PinyinCorrections = [new(0, "yi"), new(1, "tiao2")] }));
    }

    [Fact]
    public async Task BuiltInWordEditCreatesPersonalReplacementAndHidePreservesSource()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath);
        await new BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
        var catalog = new LearningCatalogStore(db);
        var original = (await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "greetings")))
            .Items.Single(x => x.Title == "你好");
        var favorites = new FavoriteStore(db);
        var folder = (await favorites.GetFoldersAsync())[0];
        await favorites.SetSelectionAsync(original.Id, [folder.Id], (await favorites.GetSelectionAsync(original.Id)).Revision);
        var words = new PersonalWordStore(db);
        var replacement = new PersonalWordInput("greetings", "你好", "见面时的招呼语",
            [new("你好，老师。"), new("大家好，你好！")], "hello", "こんにちは", false);
        await using (var connection = await db.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER reject_word_override BEFORE INSERT ON resource_entry_override BEGIN SELECT RAISE(ABORT,'test failure'); END";
            await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => words.SaveAsync(replacement, replaceResourceId: original.Id));
            command.CommandText = "SELECT count(*) FROM content WHERE origin='personal'";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
            command.CommandText = "DROP TRIGGER reject_word_override";
            await command.ExecuteNonQueryAsync();
        }
        Assert.Equal(original.Id, (await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "greetings")))
            .Items.Single(x => x.Title == "你好").Id);
        Assert.Equal([folder.Id], (await favorites.GetSelectionAsync(original.Id)).FolderIds);
        var edited = await words.SaveAsync(replacement, replaceResourceId: original.Id);
        Assert.NotEqual(original.Id, edited.Id);
        Assert.Equal(ContentOrigin.Personal, edited.Origin);
        Assert.Equal("见面时的招呼语", edited.TextUnits.Single(x => x.Role == TextUnitRole.Definition).Text);
        Assert.Equal(2, edited.TextUnits.Count(x => x.Role == TextUnitRole.Example));
        Assert.Equal(edited.Id, (await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "greetings")))
            .Items.Single(x => x.Title == "你好").Id);
        Assert.Contains((await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "common"))).Items,
            x => x.Id == edited.Id);
        Assert.Empty((await favorites.GetSelectionAsync(original.Id)).FolderIds);
        Assert.Equal([folder.Id], (await favorites.GetSelectionAsync(edited.Id)).FolderIds);
        await using (var connection = await db.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT body_json FROM content WHERE id=$id";
            command.Parameters.AddWithValue("$id", original.Id.ToString("D"));
            Assert.Equal(original.BodyJson, (string)(await command.ExecuteScalarAsync())!);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => words.SaveAsync(new("greetings", "你好", "重复", [], "", "", false),
            replaceResourceId: original.Id));

        var next = (await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "greetings")))
            .Items.First(x => x.Id != edited.Id);
        await words.HideResourceAsync(next.Id);
        Assert.DoesNotContain((await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "greetings"))).Items,
            x => x.Id == next.Id);
        await words.SaveAsync(replacement with { Category = "food" }, edited.Id);
        Assert.DoesNotContain((await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "greetings"))).Items,
            x => x.Id == edited.Id);
        Assert.DoesNotContain((await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "common"))).Items,
            x => x.Id == edited.Id);
        Assert.Contains((await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "food"))).Items,
            x => x.Id == edited.Id);
    }

    [Fact]
    public async Task BuiltInCategoryRenameHideAndRestorePersistWithoutRemovingWords()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath);
        var categories = new CustomWordCategoryStore(new VersionedLocalStateStore(db));
        await categories.RenameBuiltInAsync("numbers", "我的数字");
        await categories.SetBuiltInHiddenAsync("numbers", true);
        var restored = new CustomWordCategoryStore(new VersionedLocalStateStore(new(dir.DatabasePath)));
        Assert.Equal(new BuiltInWordCategoryPreference("numbers", "我的数字", true),
            Assert.Single(await restored.ListBuiltInAsync()));
        await restored.SetBuiltInHiddenAsync("numbers", false);
        Assert.False(Assert.Single(await categories.ListBuiltInAsync()).Hidden);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => categories.RenameBuiltInAsync("all", "错误"));
    }
}
