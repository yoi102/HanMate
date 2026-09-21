using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Reading;
using HanMate.Core.Search;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Dictionary;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class DefaultDictionaryTests
{
    [Theory]
    [InlineData("八", "bā")]
    [InlineData("生", "shēng")]
    [InlineData("金", "jīn")]
    [InlineData("学习", "xué xí")]
    [InlineData("行", "xíng")]
    public async Task ExactLookupKeepsReadingIdentityReadOnlyAndSourceEpoch(string text, string pinyin)
    {
        using var directory = new TestDatabaseDirectory();
        var db = new HanMateDatabase(directory.DatabasePath);
        var dictionary = new DefaultDictionaryStore(Path.Combine(Path.GetDirectoryName(directory.DatabasePath)!, "dictionary"));
        var store = new OfflineSearchStore(db, dictionary);
        var head = DefaultDictionaryStore.Project(Guid.NewGuid(), new(text, pinyin, false, ["测试"], [], [])).TextUnits[0];
        var match = await store.FindEntryAsync(head);
        Assert.NotNull(match.Document); Assert.True(match.IsReadOnly);
        Assert.Equal(text, match.Document.TextUnits[0].Text);
        Assert.Equal(head.Tokens.Select(t => t.Pinyin), match.Document.TextUnits[0].Tokens.Select(t => t.Pinyin));
        Assert.Equal(await store.GetEpochAsync(), match.Epoch);
        dictionary.IsEnabled = false;
        Assert.Null((await store.FindEntryAsync(head)).Document);
        Assert.NotEqual(await store.GetEpochAsync(), match.Epoch);
        dictionary.IsEnabled = true;
        Assert.Equal(match.Document.Id, (await store.FindEntryAsync(head)).Document!.Id);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.FindEntryAsync(head, cancelled.Token));
        using var connection = await db.OpenConnectionAsync(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM content";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ExactLookupDoesNotSubstituteAnotherToneOrInterpretSqlWildcards()
    {
        using var directory = new TestDatabaseDirectory();
        var store = new OfflineSearchStore(new(directory.DatabasePath), new(Path.Combine(Path.GetDirectoryName(directory.DatabasePath)!, "dictionary")));
        var head = DefaultDictionaryStore.Project(Guid.NewGuid(), new("八", "bà", false, ["测试"], [], [])).TextUnits[0];
        Assert.Null((await store.FindEntryAsync(head)).Document);
        Assert.Null((await store.FindEntryAsync(head with { Text = "八%_' OR 1=1--" })).Document);
    }

    [Fact]
    public async Task ExactLookupDistinguishesBothReadingsOfPersonalPolyphonicEntries()
    {
        using var directory = new TestDatabaseDirectory();
        var db = new HanMateDatabase(directory.DatabasePath);
        var documents = new SqliteContentDocumentStore(db, new());
        var store = new OfflineSearchStore(db);
        var entries = new[] { "háng", "xíng" }.Select(pinyin =>
        {
            var document = DefaultDictionaryStore.Project(Guid.NewGuid(), new("行", pinyin, false, ["测试释义"], [], []));
            return document with { Origin = ContentOrigin.Personal };
        }).ToArray();
        foreach (var entry in entries) await documents.SaveAsync(entry, 0);
        foreach (var entry in entries)
        {
            var lookup = await store.FindEntryAsync(entry.TextUnits[0]);
            Assert.Equal(entry.Id, lookup.Document?.Id); Assert.False(lookup.IsReadOnly);
        }
    }

    [Fact]
    public async Task SimplifiedEntriesSupportTraditionalLookupAndOriginalReadingSearch()
    {
        using var directory = new TestDatabaseDirectory();
        var store = new DefaultDictionaryStore(Path.Combine(Path.GetDirectoryName(directory.DatabasePath)!, "dictionary"));
        Assert.Equal(292114, DefaultDictionaryStore.EntryCount);
        foreach (var (query, title) in new[] { ("汉", "汉"), ("漢", "汉"), ("学习", "学习"), ("學習", "学习"), ("余", "余"), ("一", "一"), ("麻木", "麻木"), ("yi1xin1yi1yi4", "一心一意") })
        {
            var matches = await store.FindAsync(SearchQuery.Parse(query));
            Assert.Contains(matches, m => m.Title == title && m.Tier is SearchMatchTier.HanziExact or SearchMatchTier.PinyinExact);
            var hit = matches.First(m => m.Title == title && m.Tier is SearchMatchTier.HanziExact or SearchMatchTier.PinyinExact);
            var doc = await store.GetAsync(hit.Id);
            var validation = new ContentDocumentValidator().Validate(doc);
            Assert.True(validation.IsValid, string.Join(",", validation.Errors.Select(e => e.Code)));
            Assert.Equal(title, doc.TextUnits.Single(u => u.Role == TextUnitRole.Headword).Text);
            Assert.NotEmpty(new ReadingDocument(doc).Pages);
            Assert.Equal("NOASSERTION", doc.Source.LicenseIdentifier);
            Assert.False(doc.Source.CanDistribute);
            Assert.False(doc.Source.CanShare);
            Assert.NotEmpty(doc.TextUnits.First(u => u.Role == TextUnitRole.Definition).Text);
        }
        Assert.Empty(await store.FindAsync(SearchQuery.Parse("汉%_' OR 1=1--")));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.FindAsync(SearchQuery.Parse("中"), cancelled.Token));
    }

    [Fact]
    public async Task DefaultSourcePaginatesDisablesAndNeverPopulatesPersonalContent()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        var dictionary = new DefaultDictionaryStore(Path.Combine(Path.GetDirectoryName(directory.DatabasePath)!, "dictionary"));
        var store = new OfflineSearchStore(db, dictionary);
        Assert.Contains(await store.GetSourcesAsync(), s => s.Id == DefaultDictionaryStore.SourceId);
        var first = await store.SearchAsync("中", DefaultDictionaryStore.SourceId);
        Assert.True(first.Total > 50); Assert.Equal(50, first.Items.Count); Assert.NotNull(first.Next);
        var second = await store.SearchAsync("中", DefaultDictionaryStore.SourceId, first.Next);
        Assert.Empty(first.Items.Select(i => i.ContentId).Intersect(second.Items.Select(i => i.ContentId)));
        Assert.All(first.Items, item => Assert.True(item.IsReadOnly));
        var common = await store.SearchAsync("一", DefaultDictionaryStore.SourceId, pageSize: 12);
        Assert.Equal("一", common.Items[0].Headword);
        Assert.Contains(common.Items.Take(6), item => item.Headword == "一些");
        Assert.DoesNotContain(common.Items.Take(6), item => item.Headword == "一纳");
        var word = await store.SearchAsync("学习");
        Assert.Equal("学习", word.Items[0].Headword);
        var doc = JsonSerializer.Deserialize<ContentDocument>(word.Items[0].BodyJson, ContentJson.Options)!;
        Assert.Contains("学", doc.TextUnits[0].Text);
        Assert.All(doc.TextUnits[0].Tokens, t => Assert.Equal(AnnotationReviewState.NeedsReview, t.ReviewState));
        Assert.DoesNotContain(await dictionary.FindAsync(SearchQuery.Parse("xue2xi2")), m => m.Id == doc.Id);
        var ding = JsonSerializer.Deserialize<ContentDocument>((await store.SearchAsync("丁")).Items[0].BodyJson, ContentJson.Options)!;
        Assert.Equal("dīng", ding.TextUnits[0].Tokens[0].Pinyin!.Display);
        dictionary.IsEnabled = false;
        Assert.DoesNotContain(await store.GetSourcesAsync(), s => s.Id == DefaultDictionaryStore.SourceId);
        Assert.Empty((await store.SearchAsync("中")).Items);
        await Assert.ThrowsAsync<SearchCursorStaleException>(() => store.SearchAsync("中", DefaultDictionaryStore.SourceId, first.Next));
        dictionary.IsEnabled = true;
        Assert.NotEmpty((await store.SearchAsync("中")).Items);
        using var c = await db.OpenConnectionAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT count(*) FROM content";
        Assert.Equal(0L, await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public void ProjectionRetainsDefinitionsExamplesAndDistinguishesAutomaticPinyin()
    {
        var entry = new DefaultDictionaryStore.DictionaryEntry("学习", "xué xí", true,
            ["求取知识。\r\n例：学习语言。"], ["学习汉语。"], ["出处：原条目。"]);
        var id = Guid.NewGuid(); var first = DefaultDictionaryStore.Project(id, entry); var second = DefaultDictionaryStore.Project(id, entry);
        Assert.Equal(entry.Definitions[0], first.TextUnits[1].Text);
        Assert.Equal(first.TextUnits.SelectMany(u => u.Tokens).Select(t => t.Id), second.TextUnits.SelectMany(u => u.Tokens).Select(t => t.Id));
        Assert.Equal(new[] { "xué", "xí" }, first.TextUnits[0].Tokens.Select(t => t.Pinyin!.Display));
        Assert.All(first.TextUnits[1].Tokens, t => Assert.Null(t.Pinyin));
        Assert.Equal(TextUnitRole.Example, first.TextUnits[2].Role);
        Assert.Contains("出处：原条目。", first.TextUnits[3].Text);
        Assert.Contains("尚未人工校对", first.TextUnits[3].Text);
        Assert.All(first.TextUnits[0].Tokens, t => { Assert.False(t.Locked); Assert.Equal(AnnotationReviewState.NeedsReview, t.ReviewState); });
        Assert.True(new ContentDocumentValidator().Validate(first).IsValid);
    }
}
