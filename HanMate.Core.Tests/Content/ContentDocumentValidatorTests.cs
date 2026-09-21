using System.Text.Json;
using HanMate.Core.Content;

namespace HanMate.Core.Tests.Content;

public sealed class ContentDocumentValidatorTests
{
    private readonly ContentDocumentValidator _validator = new();

    [Theory]
    [InlineData("grammar-content.json")]
    [InlineData("dictionary-entry.json")]
    public void Validate_AcceptsPlanningFixtures(string name)
    {
        var result = _validator.Validate(ReadFixture(name));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_RejectsPersistedBoundaryDrift()
    {
        var document = ReadFixture("dictionary-entry.json");
        var first = document.TextUnits[0];
        var changed = document with
        {
            TextUnits = [first with { ElementBoundariesUtf16 = [0, 2] }, .. document.TextUnits.Skip(1)]
        };
        Assert.Contains(_validator.Validate(changed).Errors, error => error.Code == "TEXT_BOUNDARY_MISMATCH");
    }

    [Fact]
    public void Validate_RejectsGrammarRoleMismatch()
    {
        var document = ReadFixture("grammar-content.json");
        var grammar = document.Grammar!;
        var changed = document with { Grammar = grammar with { ExampleUnitIds = grammar.ExplanationUnitIds } };
        Assert.Contains(_validator.Validate(changed).Errors, error => error.Code == "GRAMMAR_REFERENCE_INVALID");
    }

    [Fact]
    public void Validate_AcceptsAllPlanningContentFixtures()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "contents.json"));
        var collection = JsonSerializer.Deserialize<ContentCollection>(json, ContentJson.Options)!;
        Assert.Equal(24, collection.Contents.Count);
        foreach (var document in collection.Contents)
        {
            Assert.Empty(_validator.Validate(document).Errors);
        }
    }

    private static ContentDocument ReadFixture(string name)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        return JsonSerializer.Deserialize<ContentDocument>(json, ContentJson.Options)!;
    }
}
