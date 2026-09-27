using System.Text.Json;
using HanMate.Core.Content;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

internal static class WordSearchIndex
{
    internal static async Task ReplaceAsync(SqliteConnection connection, SqliteTransaction transaction,
        ContentDocument document, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM search_index WHERE content_id=$id;";
        command.Parameters.AddWithValue("$id", document.Id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (document.Kind != ContentKind.Word) return;
        var headword = document.TextUnits.Single(u => u.Role == TextUnitRole.Headword);
        var confirmed = headword.Tokens.Where(t => t.Kind == TokenKind.Hanzi)
            .All(t => t.Pinyin is not null && t.ReviewState == AnnotationReviewState.Confirmed);
        var tokens = confirmed ? headword.Tokens.Where(t => t.Pinyin is not null).ToArray() : [];
        var readings = tokens.Select(t => t.Pinyin).OfType<PinyinSyllable>().ToArray();
        var syllables = readings.Select(p => p.Base.Replace('ü', 'v') + (p.Erhua ? "r" : "")).ToArray();
        var tones = readings.Select((p, i) => syllables[i] + p.Tone).ToArray();
        command.CommandText = """
            INSERT INTO search_index(content_id,alias_ordinal,hanzi_key,pinyin_joined,pinyin_separated,
                tone_joined,tone_separated,syllables_json,frequency_rank,index_version)
            VALUES($id,0,$hanzi,$joined,$separated,$tones,$toneSeparated,$syllables,NULL,1);
            """;
        command.Parameters.AddWithValue("$hanzi", headword.Text.Normalize());
        command.Parameters.AddWithValue("$joined", string.Concat(syllables));
        command.Parameters.AddWithValue("$separated", string.Join(' ', syllables));
        command.Parameters.AddWithValue("$tones", string.Concat(tones));
        command.Parameters.AddWithValue("$toneSeparated", string.Join(' ', tones));
        command.Parameters.AddWithValue("$syllables", JsonSerializer.Serialize(readings.Select(p => new { @base = p.Base, tone = p.Tone })));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
