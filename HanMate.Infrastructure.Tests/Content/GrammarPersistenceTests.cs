using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Infrastructure.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Tests.Database;

namespace HanMate.Infrastructure.Tests.Content;

public sealed class GrammarPersistenceTests
{
    [Fact]
    public async Task InvalidGrammarUpdateLeavesSavedTextAndRevisionUntouched()
    {
        using var directory = new TestDatabaseDirectory();
        var codec = new JsonContentDocumentCodec(new());
        using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "grammar-content.json"));
        var document = codec.Read(source);
        var store = new SqliteContentDocumentStore(new(directory.DatabasePath), new());
        await store.SaveAsync(document, 0);
        var invalid = document with { Grammar = document.Grammar! with { ExampleUnitIds = [Guid.NewGuid()] } };
        await Assert.ThrowsAsync<ContentDocumentValidationException>(() => store.SaveAsync(invalid, 1));
        var reopened = new SqliteContentDocumentStore(new(directory.DatabasePath), new());
        var saved = await reopened.GetAsync(document.Id);
        Assert.NotNull(saved);
        Assert.Equal(1, saved.RowRevision);
        Assert.Equal(JsonSerializer.Serialize(document, ContentJson.Options), JsonSerializer.Serialize(saved.Document, ContentJson.Options));
    }

    [Fact]
    public void CodecRoundTripsAllFourKindsWithRequiredNullsUtcAndGrammarReferences()
    {
        var source = JsonSerializer.Deserialize<ContentCollection>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "contents.json")), ContentJson.Options)!;
        var codec = new JsonContentDocumentCodec(new());
        Assert.Equal(4, source.Contents.Select(c => c.Kind).Distinct().Count());
        var exportDirectory = Path.Combine(AppContext.BaseDirectory, "ContractExports");
        Directory.CreateDirectory(exportDirectory);
        foreach (var document in source.Contents)
        {
            using var stream = new MemoryStream();
            codec.Write(stream, document);
            stream.Position = 0;
            var restored = codec.Read(stream);
            Assert.Equal(JsonSerializer.Serialize(document, ContentJson.Options), JsonSerializer.Serialize(restored, ContentJson.Options));
            using var json = JsonDocument.Parse(stream.ToArray());
            foreach (var field in new[] { "difficulty", "schoolStage", "grade" })
                Assert.True(json.RootElement.TryGetProperty(field, out _));
            Assert.EndsWith("Z", json.RootElement.GetProperty("createdAtUtc").GetString());
            foreach (var unit in json.RootElement.GetProperty("textUnits").EnumerateArray())
                foreach (var token in unit.GetProperty("tokens").EnumerateArray())
                    Assert.True(token.TryGetProperty("pinyin", out _));
            if (document.Kind != ContentKind.Grammar)
                Assert.False(json.RootElement.TryGetProperty("grammar", out _));
            // Explicit artifacts for independent validation against the planning JSON Schema.
            File.WriteAllBytes(Path.Combine(exportDirectory, document.Id.ToString("D") + ".json"), stream.ToArray());
        }
    }

    [Fact]
    public void ManualCorrectionTimestampAndLockedReadingSurviveCodecRoundTrip()
    {
        var codec = new JsonContentDocumentCodec(new());
        using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "grammar-content.json"));
        var document = codec.Read(source);
        var unit = document.TextUnits.First(u => u.Role == TextUnitRole.Example);
        var token = unit.Tokens.First(t => t.Pinyin is not null);
        var correction = token with { Locked = true, AnnotationSource = AnnotationSource.Manual, ModifiedAtUtc = DateTimeOffset.Parse("2026-09-15T00:00:00Z") };
        var edited = document with { TextUnits = document.TextUnits.Select(u => u.Id == unit.Id ? u with { Tokens = u.Tokens.Select(t => t.Id == token.Id ? correction : t).ToArray() } : u).ToArray() };
        using var stream = new MemoryStream();
        codec.Write(stream, edited); stream.Position = 0;
        var restored = codec.Read(stream).TextUnits.Single(u => u.Id == unit.Id).Tokens.Single(t => t.Id == token.Id);
        Assert.Equal(correction, restored);
    }
}
