using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Dictionary;
using HanMate.Infrastructure.Packages;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class WordCategoryTransferTests
{
    [Fact]
    public async Task InvalidDestinationDoesNotImportContentOrCreateCategory()
    {
        using var sourceDir = new TestDatabaseDirectory(); using var targetDir = new TestDatabaseDirectory();
        var source = new HanMateDatabase(sourceDir.DatabasePath);
        var target = new HanMateDatabase(targetDir.DatabasePath);
        var word = await new PersonalWordStore(source).SaveAsync(new(WordCategories.Other,
            "西瓜", "一种水果", [new("一个西瓜")], "watermelon", "スイカ", false));
        var share = new ContentShareStore(source);
        using var package = new MemoryStream();
        await share.ExportAsync(await share.PreviewAsync([word.Id]), package, true);
        package.Position = 0;
        var plan = await new ContentPackageImportStore(target).PlanAsync(package);
        Assert.Equal([WordCategories.Other], WordCategoryTransferStore.SourceCategories(plan));
        await Assert.ThrowsAsync<PackageException>(() => new WordCategoryTransferStore(target).CommitAsync(plan,
            new Dictionary<string, string> { [WordCategories.Other] = "custom-" + Guid.NewGuid().ToString("N") }, []));
        Assert.Null(await new SqliteContentDocumentStore(target, new()).GetAsync(word.Id));
        Assert.Empty(await new CustomWordCategoryStore(new VersionedLocalStateStore(target)).ListAsync());
    }

    [Fact]
    public async Task MultipleSourceCategoriesCanBePlacedInChosenCategoriesAndReimportedWithoutDuplicates()
    {
        using var sourceDir = new TestDatabaseDirectory(); using var targetDir = new TestDatabaseDirectory();
        var source = new HanMateDatabase(sourceDir.DatabasePath);
        var target = new HanMateDatabase(targetDir.DatabasePath);
        var custom = await new CustomWordCategoryStore(new VersionedLocalStateStore(source)).CreateAsync("My practice");
        var words = new PersonalWordStore(source);
        var first = await words.SaveAsync(new(custom.Id, "苹果", "一种水果", [new("一个苹果")], "apple", "りんご", false));
        var second = await words.SaveAsync(new("numbers", "十", "数字", [new("十个人")], "ten", "十", false));
        var share = new ContentShareStore(source);
        var preview = await share.PreviewAsync([first.Id, second.Id]);
        Assert.Contains(custom.Id, preview.WordCategoryIds);
        Assert.Contains("numbers", preview.WordCategoryIds);
        using var package = new MemoryStream();
        await share.ExportAsync(preview, package, true);
        var targetCategory = new CustomWordCategory("custom-" + Guid.NewGuid().ToString("N"), "Saved practice");
        var map = new Dictionary<string, string> { [custom.Id] = targetCategory.Id, ["numbers"] = "length" };
        var importer = new ContentPackageImportStore(target);
        package.Position = 0;
        var plan = await importer.PlanAsync(package);
        var result = await new WordCategoryTransferStore(target).CommitAsync(plan, map, [targetCategory]);
        Assert.Equal(2, result.Added);
        Assert.Contains(await new CustomWordCategoryStore(new VersionedLocalStateStore(target)).ListAsync(),
            category => category == targetCategory);
        var saved = new SqliteContentDocumentStore(target, new());
        var documents = await Task.WhenAll(result.ContentIds.Select(async id => (await saved.GetAsync(id))!.Document));
        Assert.Contains(documents, doc => doc.Title == "苹果" && doc.Scenes.Contains(WordCategories.Scene(targetCategory.Id)) &&
            !doc.Scenes.Contains(WordCategories.Scene(custom.Id)));
        Assert.Contains(documents, doc => doc.Title == "十" && doc.Scenes.Contains(WordCategories.Scene("length")) &&
            !doc.Scenes.Contains(WordCategories.Scene("numbers")));
        var importedApple = Assert.Single(documents, doc => doc.Title == "苹果");
        var detail = DictionaryDetailProjection.Create(importedApple, sourceOnly: true);
        Assert.Equal("一种水果", Assert.Single(detail.Definitions).Text);
        Assert.Equal("一种水果", string.Concat(detail.Definitions[0].Atoms.Select(atom => atom.Text)));
        Assert.Equal("一个苹果", Assert.Single(detail.Examples).Text);
        Assert.Equal("apple", importedApple.TextUnits.Single(unit => unit.Role == TextUnitRole.Headword).Translations["en"]);
        package.Position = 0;
        var again = await importer.PlanAsync(package);
        Assert.Equal(2, again.Reused);
        var repeated = await new WordCategoryTransferStore(target).CommitAsync(again, map, []);
        Assert.Equal(0, repeated.Added);
        Assert.Equal(result.ContentIds, repeated.ContentIds);
    }
}
