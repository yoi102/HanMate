using System.Text.Json;
using HanMate.Core.Content;

namespace HanMate.Core.Tests.Content;

public sealed class GrammarValidatorTests
{
    [Theory]
    [InlineData("missing-grammar", "GRAMMAR_REQUIRED")]
    [InlineData("no-pattern", "GRAMMAR_PATTERN_INVALID")]
    [InlineData("null-pattern", "GRAMMAR_PATTERN_INVALID")]
    [InlineData("null-part", "GRAMMAR_PATTERN_INVALID")]
    [InlineData("blank-pattern", "GRAMMAR_PATTERN_INVALID")]
    [InlineData("large-pattern", "GRAMMAR_PATTERN_INVALID")]
    [InlineData("long-part", "GRAMMAR_PATTERN_INVALID")]
    [InlineData("invalid-kind", "GRAMMAR_PATTERN_INVALID")]
    [InlineData("invalid-unicode", "GRAMMAR_PATTERN_INVALID")]
    [InlineData("no-explanation", "GRAMMAR_REFERENCE_INVALID")]
    [InlineData("no-example", "GRAMMAR_REFERENCE_INVALID")]
    [InlineData("null-notes", "GRAMMAR_REFERENCE_INVALID")]
    [InlineData("wrong-role", "GRAMMAR_REFERENCE_INVALID")]
    [InlineData("foreign-id", "GRAMMAR_REFERENCE_INVALID")]
    [InlineData("duplicate-id", "GRAMMAR_REFERENCE_INVALID")]
    [InlineData("omitted-unit", "GRAMMAR_REFERENCE_INVALID")]
    [InlineData("too-many-examples", "GRAMMAR_REFERENCE_INVALID")]
    [InlineData("empty-explanation", "GRAMMAR_REFERENCE_INVALID")]
    [InlineData("punctuation-explanation", "GRAMMAR_REFERENCE_INVALID")]
    [InlineData("null-translations", "GRAMMAR_TRANSLATION_INVALID")]
    [InlineData("chinese-translation", "GRAMMAR_TRANSLATION_INVALID")]
    [InlineData("null-translation", "GRAMMAR_TRANSLATION_INVALID")]
    [InlineData("long-translation", "GRAMMAR_TRANSLATION_INVALID")]
    [InlineData("bad-topic", "GRAMMAR_TOPIC_INVALID")]
    [InlineData("too-many-topics", "GRAMMAR_TOPIC_INVALID")]
    [InlineData("null-topics", "GRAMMAR_TOPIC_INVALID")]
    public void RejectsMalformedGrammarWithoutMutatingInput(string scenario, string code)
    {
        var original = Load();
        var g = original.Grammar!;
        var changed = scenario switch
        {
            "missing-grammar" => original with { Grammar = null },
            "no-pattern" => original with { Grammar = g with { PatternParts = [] } },
            "null-pattern" => original with { Grammar = g with { PatternParts = null! } },
            "null-part" => original with { Grammar = g with { PatternParts = [null!] } },
            "blank-pattern" => Part(" \t"),
            "large-pattern" => original with { Grammar = g with { PatternParts = Enumerable.Repeat(g.PatternParts[0], 41).ToArray() } },
            "long-part" => Part(new string('中', 81)),
            "invalid-kind" => original with { Grammar = g with { PatternParts = [g.PatternParts[0] with { Kind = (GrammarPatternPartKind)99 }] } },
            "invalid-unicode" => Part("\ud800"),
            "no-explanation" => original with { Grammar = g with { ExplanationUnitIds = [] } },
            "no-example" => original with { Grammar = g with { ExampleUnitIds = [] } },
            "null-notes" => original with { Grammar = g with { NoteUnitIds = null! } },
            "wrong-role" => original with { Grammar = g with { ExampleUnitIds = g.ExplanationUnitIds } },
            "foreign-id" => original with { Grammar = g with { ExampleUnitIds = [Guid.NewGuid()] } },
            "duplicate-id" => original with { Grammar = g with { ExampleUnitIds = [.. g.ExampleUnitIds, g.ExampleUnitIds[0]] } },
            "omitted-unit" => original with { Grammar = g with { ExampleUnitIds = g.ExampleUnitIds.Skip(1).ToArray() } },
            "too-many-examples" => original with { Grammar = g with { ExampleUnitIds = Enumerable.Repeat(g.ExampleUnitIds[0], 21).ToArray() } },
            "empty-explanation" => Explanation(" "),
            "punctuation-explanation" => Explanation("？！"),
            "null-translations" => original with { Grammar = g with { PatternTranslations = null! } },
            "chinese-translation" => Translation("zh-Hans", "说明"),
            "null-translation" => Translation("ja", null!),
            "long-translation" => Translation("en", new string('a', 20_001)),
            "bad-topic" => original with { Grammar = g with { TopicCodes = ["Time"] } },
            "too-many-topics" => original with { Grammar = g with { TopicCodes = Enumerable.Range(0, 21).Select(i => $"topic-{i}").ToArray() } },
            "null-topics" => original with { Grammar = g with { TopicCodes = null! } },
            _ => throw new ArgumentException(scenario)
        };
        var before = scenario == "invalid-kind" ? null : JsonSerializer.Serialize(changed, ContentJson.Options);
        Assert.Contains(new ContentDocumentValidator().Validate(changed).Errors, error => error.Code == code);
        if (before is not null) Assert.Equal(before, JsonSerializer.Serialize(changed, ContentJson.Options));
        Assert.Empty(GrammarValidator.Validate(original).Errors);

        ContentDocument Part(string text) => original with { Grammar = g with { PatternParts = [g.PatternParts[0] with { Text = text }] } };
        ContentDocument Translation(string language, string text) => original with { Grammar = g with { PatternTranslations = new Dictionary<string, string> { [language] = text } } };
        ContentDocument Explanation(string text) => original with { TextUnits = original.TextUnits.Select(u => u.Id == g.ExplanationUnitIds[0] ? u with { Text = text } : u).ToArray() };
    }

    [Fact]
    public void PatternLimitCountsUnicodeScalarsNotUtf16CodeUnits()
    {
        var document = Load();
        var pattern = document.Grammar!.PatternParts[0] with { Text = string.Concat(Enumerable.Repeat("𠀀", 80)) };
        Assert.Empty(GrammarValidator.Validate(document with { Grammar = document.Grammar with { PatternParts = [pattern] } }).Errors);
    }

    [Fact]
    public void ReferenceOrderMayDifferFromStorageOrderWithoutRewritingTheLesson()
    {
        var document = Load();
        var changed = document with { TextUnits = document.TextUnits.Reverse().ToArray() };
        Assert.Empty(new ContentDocumentValidator().Validate(changed).Errors);
        Assert.Equal(document.Grammar!.ExampleUnitIds, changed.Grammar!.ExampleUnitIds);
    }

    [Theory]
    [InlineData(ContentKind.Word)]
    [InlineData(ContentKind.Text)]
    [InlineData(ContentKind.Poem)]
    public void OtherContentKindsCannotCarryGrammar(ContentKind kind)
    {
        Assert.Contains(GrammarValidator.Validate(Load() with { Kind = kind }).Errors, e => e.Code == "GRAMMAR_KIND_INVALID");
    }

    [Theory]
    [InlineData("patternParts")]
    [InlineData("patternTranslations")]
    [InlineData("explanationUnitIds")]
    [InlineData("exampleUnitIds")]
    [InlineData("noteUnitIds")]
    [InlineData("topicCodes")]
    public void MissingRequiredGrammarFieldsAreNotSilentlyDefaulted(string field)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FixturePath))!;
        node["grammar"]!.AsObject().Remove(field);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContentDocument>(node.ToJsonString(), ContentJson.Options));
    }

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "grammar-content.json");
    private static ContentDocument Load() => JsonSerializer.Deserialize<ContentDocument>(File.ReadAllText(FixturePath), ContentJson.Options)!;
}
