using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Pinyin;
using HanMate.Core.Search;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Dictionary;

public sealed record DictionaryMatch(Guid Id, string Title, SearchMatchTier Tier, int SenseOrder);

/// <summary>Immutable publisher text, separate from personal content and backup databases.</summary>
public sealed class DefaultDictionaryStore(string cacheDirectory)
{
    public const string SourceId = "chinese-xinhua";
    public const string PreferenceKey = "dictionary.xinhua.enabled";
    public const string Name = "简体汉语字典（chinese-xinhua）";
    public const string Version = "fe6d6c2e8baa";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _readyPath;
    private bool _enabled = true;
    private long _revision;
    public bool IsEnabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; Interlocked.Increment(ref _revision); } }
    public long Revision => Interlocked.Read(ref _revision);
    private static Stream Asset(string name) => typeof(DefaultDictionaryStore).Assembly.GetManifestResourceStream("Dictionary." + name)
        ?? throw new InvalidDataException("Default dictionary asset missing.");
    public static string ReadNotice() { using var stream = Asset("XINHUA-NOTICE.json"); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    public static string ReadUsage()
    {
        using var stream = Asset("XINHUA-README.txt"); using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd(); var start = text.IndexOf("## Copyright", StringComparison.Ordinal);
        return start < 0 ? text : text[(start + "## Copyright".Length)..].Trim();
    }
    public static string ReadPinyinLicense() { using var stream = Asset("PYPINYIN-LICENSE.txt"); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    public static string ReadFrequencyLicense() { using var stream = Asset("JIEBA-LICENSE.txt"); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    public static string ReadIndexLicense() { using var stream = Asset("OPENCC-LICENSE.txt"); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    public static int EntryCount { get { using var info = JsonDocument.Parse(ReadNotice()); return info.RootElement.GetProperty("entries").GetInt32(); } }

    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_readyPath is null)
            {
                using var info = JsonDocument.Parse(ReadNotice());
                var hash = info.RootElement.GetProperty("sqliteSha256").GetString()!;
                Directory.CreateDirectory(cacheDirectory);
                var target = Path.Combine(cacheDirectory, "xinhua-" + hash + ".sqlite");
                if (!File.Exists(target) || await HashAsync(target, token).ConfigureAwait(false) != hash)
                {
                    var temporary = target + "." + Guid.NewGuid().ToString("N") + ".part";
                    try
                    {
                        await using (var input = Asset("xinhua.sqlite.gz"))
                        await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                        await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            await gzip.CopyToAsync(output, token).ConfigureAwait(false);
                        if (await HashAsync(temporary, token).ConfigureAwait(false) != hash) throw new InvalidDataException("Default dictionary hash mismatch.");
                        File.Move(temporary, target, overwrite: true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                _readyPath = target;
            }
        }
        finally { _gate.Release(); }
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = _readyPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        try { await connection.OpenAsync(token).ConfigureAwait(false); return connection; }
        catch { connection.Dispose(); throw; }
    }
    public async Task PrepareAsync(CancellationToken token = default)
    {
        using var connection = await OpenAsync(token).ConfigureAwait(false);
    }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    { await using var file = File.OpenRead(path); return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, token).ConfigureAwait(false)); }

    public async Task<IReadOnlyList<DictionaryMatch>> FindAsync(SearchQuery query, CancellationToken token = default)
    {
        if (!IsEnabled || !query.IsValid) return [];
        using var connection = await OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT e.id,e.title,e.simple,e.separated,e.tones,e.sense_order FROM entry e ";
        if (!query.IsPinyin && query.Key.EnumerateRunes().Count() == 1)
        {
            command.CommandText += "JOIN character_index i ON i.id=e.id WHERE i.character=$key";
            command.Parameters.AddWithValue("$key", query.Key);
        }
        else
        {
            command.CommandText += query.IsPinyin ? "WHERE e.joined LIKE $pattern ESCAPE '\\'" : "WHERE e.title LIKE $pattern ESCAPE '\\' OR e.simple LIKE $pattern ESCAPE '\\'";
            command.Parameters.AddWithValue("$pattern", "%" + query.Key.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
        }
        var results = new List<DictionaryMatch>();
        using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            token.ThrowIfCancellationRequested();
            var original = query.Match(reader.GetString(1), reader.GetString(3), reader.GetString(4));
            var simple = query.Match(reader.GetString(2), reader.GetString(3), reader.GetString(4));
            var tier = original is null ? simple : simple is null ? original : (SearchMatchTier)Math.Min((int)original, (int)simple);
            if (tier is { } match) results.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), match, reader.GetInt32(5)));
        }
        return results;
    }

    public async Task<ContentDocument> GetAsync(Guid id, CancellationToken token = default)
    {
        using var connection = await OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand(); command.CommandText = "SELECT raw_json FROM entry WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        var raw = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string ?? throw new KeyNotFoundException();
        return Project(id, JsonSerializer.Deserialize<DictionaryEntry>(raw)!);
    }

    public async Task<IReadOnlyList<DictionaryMatch>> FindExactAsync(string headword, CancellationToken token = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(headword)) return [];
        using var connection = await OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        // entry_title is already shipped in the immutable dictionary. No full-table LIKE scan.
        command.CommandText = "SELECT id,title,sense_order FROM entry WHERE title=$title";
        command.Parameters.AddWithValue("$title", headword);
        var results = new List<DictionaryMatch>();
        using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            token.ThrowIfCancellationRequested();
            results.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), SearchMatchTier.HanziExact, reader.GetInt32(2)));
        }
        return results;
    }

    public sealed record DictionaryEntry(string Title, string Pinyin, bool PinyinIsAutomatic,
        IReadOnlyList<string> Definitions, IReadOnlyList<string> Examples, IReadOnlyList<string> Notes);

    public static Guid MetadataUnitId(Guid id) => new(SHA256.HashData(Encoding.UTF8.GetBytes(id + ":metadata")).AsSpan(0, 16));

    public static ContentDocument Project(Guid id, DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Title) || entry.Definitions.Count == 0) throw new InvalidDataException("Dictionary entry missing text.");
        var units = new List<TextUnit> { Unit(entry.Title, TextUnitRole.Headword, "headword", entry.Pinyin) };
        for (var i = 0; i < entry.Definitions.Count; i++)
            units.Add(Unit(entry.Definitions[i], TextUnitRole.Definition, "definition:" + i));
        for (var i = 0; i < entry.Examples.Count; i++)
            units.Add(Unit(entry.Examples[i], TextUnitRole.Example, "example:" + i));
        var metadata = string.Join("\n", entry.Notes);
        if (entry.PinyinIsAutomatic) metadata = "拼音为自动补注，尚未人工校对。\n" + metadata;
        if (metadata.Length > 0) units.Add(Unit(metadata, TextUnitRole.Definition, "metadata"));
        return new ContentDocument
        {
            Id = id, Kind = ContentKind.Word, Origin = ContentOrigin.Builtin, Title = entry.Title,
            ContentRevision = 1, AnnotationRevision = 1, MetadataRevision = 1,
            Difficulty = null, SchoolStage = null, Grade = null,
            CreatedAtUtc = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero), UpdatedAtUtc = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
            Source = new() { SourceId = SourceId, Type = SourceType.Imported,
                AuthorProvider = "pwxcoo / chinese-xinhua", Reference = "https://github.com/pwxcoo/chinese-xinhua",
                LicenseIdentifier = "NOASSERTION", PermissionNotes = Name + " · " + Version + "。保留上游来源声明；内容未经全面审校。",
                CanDistribute = false, CanShare = false, BackupPolicy = BackupPolicy.Restricted, ReviewStatus = ReviewStatus.NeedsReview },
            TextUnits = units
        };

        TextUnit Unit(string text, TextUnitRole role, string name, string? pinyin = null)
        {
            var bounds = TextElementMap.CreateUtf16Boundaries(text); var tokens = new List<TextToken>();
            var syllables = pinyin?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            var elements = Enumerable.Range(0, bounds.Count - 1).Select(i => TextElementMap.Slice(text, bounds, i, 1)).ToArray();
            var aligned = syllables.Length == elements.Length && elements.All(DeterministicPinyinCandidateEngine.IsHanziElement);
            for (var i = 0; i < elements.Length; i++)
            {
                var hanzi = DeterministicPinyinCandidateEngine.IsHanziElement(elements[i]);
                PinyinSyllable? reading = null;
                if (aligned && PinyinSyllableParser.TryParseDictionarySyllable(syllables[i], out var parsed)) reading = parsed;
                tokens.Add(new() { Id = Stable(name + ":token:" + i), Start = i, Length = 1, Text = elements[i],
                    Kind = hanzi ? TokenKind.Hanzi : string.IsNullOrWhiteSpace(elements[i]) ? TokenKind.Whitespace : TokenKind.Symbol,
                    Pinyin = reading, AnnotationSource = reading is null ? AnnotationSource.None : entry.PinyinIsAutomatic ? AnnotationSource.PhraseDictionary : AnnotationSource.Reviewed,
                    Locked = reading is not null && !entry.PinyinIsAutomatic,
                    ReviewState = reading is not null ? entry.PinyinIsAutomatic ? AnnotationReviewState.NeedsReview : AnnotationReviewState.Confirmed
                        : hanzi ? AnnotationReviewState.Unknown : AnnotationReviewState.NotApplicable });
            }
            return new() { Id = Stable(name), Role = role, Text = text, ElementBoundariesUtf16 = bounds, Tokens = tokens,
                Segments = [new() { Id = Stable(name + ":segment"), Start = 0, Length = elements.Length, Text = text,
                    Kind = SegmentKind.Speech, BoundarySource = BoundarySource.Auto, TokenIds = tokens.Select(t => t.Id).ToArray() }] };
        }
        Guid Stable(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(id + ":" + key)).AsSpan(0, 16));
    }
}
