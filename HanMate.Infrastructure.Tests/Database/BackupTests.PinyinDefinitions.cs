using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Pinyin;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;

namespace HanMate.Infrastructure.Tests.Database;

public sealed partial class BackupTests
{
    [Fact]
    public async Task AnnotatedCourseCopiesSurviveTwoBackupHopsAndRepeatWithoutLosingLocks()
    {
        using var a = new Area(); using var b = new Area(); using var c = new Area();
        var course = PinyinCourse.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pinyin-course.json")));
        var originals = course.Data.Contents.Select(EditorCommitStore.Copy).ToArray();
        Assert.Equal(418, originals.Length);
        var source = new SqliteContentDocumentStore(a.Db, new());
        foreach (var doc in originals) await source.SaveAsync(doc, 0);
        var favorites = new FavoriteStore(a.Db); var folder = await favorites.SaveFolderAsync("注音迁移夹具", "isolated fixture");
        await favorites.SetSelectionAsync(originals[0].Id, [folder], (await favorites.GetSelectionAsync(originals[0].Id)).Revision);
        foreach (var (from, to) in new[] { (a, b), (b, c) })
        {
            using var bytes = await from.Export(); var importer = new BackupImportStore(to.Db);
            var result = await importer.CommitAsync(await importer.PlanAsync(bytes));
            Assert.Equal(originals.Length, result.Added);
            var store = new SqliteContentDocumentStore(to.Db, new());
            foreach (var original in originals)
            {
                var loaded = (await store.GetAsync(original.Id))!.Document;
                Assert.Equal(JsonSerializer.Serialize(original, ContentJson.Options), JsonSerializer.Serialize(loaded, ContentJson.Options));
                Assert.All(loaded.TextUnits.Single(u => u.Role == TextUnitRole.Definition).Tokens.Where(t => t.Kind == TokenKind.Hanzi),
                    t => { Assert.True(t.Locked); Assert.NotNull(t.Pinyin); Assert.Equal(AnnotationReviewState.NeedsReview, t.ReviewState); });
            }
            Assert.Equal(1L, await to.Scalar("SELECT count(*) FROM favorite_item"));
            bytes.Position = 0; var repeat = await importer.PlanAsync(bytes);
            Assert.Equal(originals.Length, repeat.Reused); Assert.Equal(0, repeat.Added); await importer.CommitAsync(repeat);
            Assert.Equal((long)originals.Length, await to.Scalar("SELECT count(*) FROM content"));
        }
    }
}
