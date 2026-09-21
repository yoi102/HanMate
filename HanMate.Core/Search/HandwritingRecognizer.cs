using System.IO.Compression;
using System.Text.Json;
using System.Text;
using System.Runtime.InteropServices;

namespace HanMate.Core.Search;

public readonly record struct InkPoint(float X, float Y);
public sealed record HandwritingCandidate(string Character, double Distance);

/// <summary>Offline single-character templates with ordered prefixes and a bounded, order-independent shape fallback.</summary>
public sealed class HandwritingRecognizer
{
    public const int MaximumStrokes = 64;
    private const int Samples = 16;
    private const int MaximumMissingLifts = 6;
    private readonly Template[] _templates;
    private readonly record struct Bounds(float MinX, float MinY, float Width, float Height, float Size);
    private sealed record Template(string Character, InkPoint[] Points, Bounds[] Prefixes, InkPoint[] Cloud, InkPoint[] Joined);
    private HandwritingRecognizer(Template[] templates) => _templates = templates;
    public int CharacterCount => _templates.Length;

    public static HandwritingRecognizer Load(Stream compressed)
    {
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new StreamReader(gzip);
        using var notice = JsonDocument.Parse(reader.ReadLine() ?? throw new InvalidDataException());
        if (notice.RootElement.GetProperty("format").GetInt32() != 1) throw new InvalidDataException("Handwriting data version.");
        var templates = new List<Template>(); var seen = new HashSet<string>();
        while (reader.ReadLine() is { } line)
        {
            if (templates.Count >= 20000 || line.Length > 200000) throw new InvalidDataException("Handwriting data limit.");
            using var json = JsonDocument.Parse(line);
            var character = json.RootElement.GetProperty("character").GetString()!;
            if (character.EnumerateRunes().Count() != 1 || !seen.Add(character)) throw new InvalidDataException("Handwriting character.");
            var strokes = json.RootElement.GetProperty("strokes").EnumerateArray().Select(s => s.EnumerateArray()
                .Select(p => new InkPoint(p[0].GetSingle(), p[1].GetSingle())).ToArray()).ToArray();
            Validate(strokes);
            var points = strokes.SelectMany(Resample).ToArray();
            var prefixes = new Bounds[strokes.Length];
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            for (var i = 0; i < points.Length; i++)
            {
                minX = Math.Min(minX, points[i].X); minY = Math.Min(minY, points[i].Y);
                maxX = Math.Max(maxX, points[i].X); maxY = Math.Max(maxY, points[i].Y);
                if ((i + 1) % Samples == 0)
                    prefixes[i / Samples] = new(minX, minY, maxX - minX, maxY - minY, Math.Max(Math.Max(maxX - minX, maxY - minY), .001f));
            }
            var normalized = Normalize(strokes.Select(Resample).ToArray(), strokes.Length);
            var cloud = Cloud(normalized);
            var joined = strokes.Length <= 12 ? new InkPoint[strokes.Length * 2 * Samples] : [];
            var full = prefixes[^1];
            for (var start = 0; start < strokes.Length && joined.Length > 0; start++)
                for (var length = 2; length <= 3 && start + length <= strokes.Length; length++)
                {
                    var merged = Resample(strokes.Skip(start).Take(length).SelectMany(s => s).ToArray());
                    for (var p = 0; p < Samples; p++)
                        joined[(start * 2 + length - 2) * Samples + p] = new(
                            (merged[p].X - full.MinX - full.Width / 2) / full.Size,
                            (merged[p].Y - full.MinY - full.Height / 2) / full.Size);
                }
            templates.Add(new(character, points, prefixes, cloud, joined));
        }
        if (templates.Count == 0) throw new InvalidDataException("Empty handwriting data.");
        return new(templates.ToArray());
    }

    // This derived, app-owned asset is generated offline from the licensed medians.
    // Loading does no JSON parsing, stroke resampling or template feature extraction.
    public void WritePrepared(Stream output)
    {
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x484D4934); writer.Write(_templates.Length);
        foreach (var t in _templates)
        {
            writer.Write(t.Character.EnumerateRunes().Single().Value); writer.Write(t.Prefixes.Length);
            foreach (var p in t.Points) { writer.Write(p.X); writer.Write(p.Y); }
            foreach (var b in t.Prefixes)
            { writer.Write(b.MinX); writer.Write(b.MinY); writer.Write(b.Width); writer.Write(b.Height); writer.Write(b.Size); }
            foreach (var p in t.Cloud) { writer.Write(p.X); writer.Write(p.Y); }
            foreach (var p in t.Joined) { writer.Write(p.X); writer.Write(p.Y); }
        }
    }
    public static HandwritingRecognizer LoadPrepared(Stream input)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Prepared handwriting uses little endian floats.");
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != 0x484D4934) throw new InvalidDataException("Handwriting prepared version.");
        var count = reader.ReadInt32();
        if (count is < 1 or > 20000) throw new InvalidDataException("Handwriting data limit.");
        var templates = new Template[count]; var seen = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            var rune = reader.ReadInt32(); var strokes = reader.ReadInt32();
            if (!Rune.IsValid(rune) || !seen.Add(rune) || strokes is < 1 or > MaximumStrokes)
                throw new InvalidDataException("Handwriting prepared character.");
            var points = new InkPoint[strokes * Samples];
            input.ReadExactly(MemoryMarshal.AsBytes(points.AsSpan()));
            foreach (var point in points) { CheckFinite(point.X); CheckFinite(point.Y); }
            var prefixes = new Bounds[strokes];
            input.ReadExactly(MemoryMarshal.AsBytes(prefixes.AsSpan()));
            for (var j = 0; j < strokes; j++)
            {
                CheckFinite(prefixes[j].MinX); CheckFinite(prefixes[j].MinY); CheckFinite(prefixes[j].Width);
                CheckFinite(prefixes[j].Height); CheckFinite(prefixes[j].Size);
                if (prefixes[j].Width < 0 || prefixes[j].Height < 0 || prefixes[j].Size < .001f)
                    throw new InvalidDataException("Handwriting prepared bounds.");
            }
            var cloud = new InkPoint[strokes * 8];
            input.ReadExactly(MemoryMarshal.AsBytes(cloud.AsSpan()));
            foreach (var point in cloud) { CheckFinite(point.X); CheckFinite(point.Y); }
            var joined = strokes <= 12 ? new InkPoint[strokes * 2 * Samples] : [];
            input.ReadExactly(MemoryMarshal.AsBytes(joined.AsSpan()));
            foreach (var point in joined) { CheckFinite(point.X); CheckFinite(point.Y); }
            templates[i] = new(new Rune(rune).ToString(), points, prefixes, cloud, joined);
        }
        if (reader.BaseStream.ReadByte() != -1) throw new InvalidDataException("Trailing handwriting data.");
        return new(templates);
        static void CheckFinite(float value)
        {
            if (!float.IsFinite(value) || Math.Abs(value) > 100000) throw new InvalidDataException("Handwriting prepared coordinate.");
        }
    }

    public IReadOnlyList<HandwritingCandidate> Recognize(IReadOnlyList<InkPoint[]> strokes, int count = 8, CancellationToken cancellationToken = default)
        => Recognize(strokes, count, cancellationToken, mergeSplitStroke: true);

    private IReadOnlyList<HandwritingCandidate> Recognize(IReadOnlyList<InkPoint[]> strokes, int count, CancellationToken cancellationToken, bool mergeSplitStroke)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (strokes.Count == 0) return [];
        Validate(strokes);
        var input = Normalize(strokes.Select(Resample).ToArray(), strokes.Count);
        var limit = Math.Clamp(count, 1, 12);
        var candidates = new List<HandwritingCandidate>(limit);
        var cloud = Cloud(input);
        var shapes = new List<(Template Template, double Score)>(48);
        foreach (var template in _templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (template.Joined.Length > 0 && template.Prefixes.Length > strokes.Count
                && template.Prefixes.Length <= Math.Min(strokes.Count + MaximumMissingLifts, strokes.Count * 3))
            {
                // Penalize the proportion of joined strokes, not every missing lift:
                // otherwise an eight-stroke character is excluded by ranking alone.
                var penalty = .012 * (template.Prefixes.Length - strokes.Count) / template.Prefixes.Length;
                var ceiling = candidates.Count == limit ? candidates[^1].Distance - penalty : double.PositiveInfinity;
                var joinedDistance = JoinedStrokeDistance(input, template, ceiling) + penalty;
                if (double.IsFinite(joinedDistance)) AddCandidate(template.Character, joinedDistance);
            }
            // A lower bound of unordered stroke matching supplies a bounded shortlist.
            // Unlike a coarse raster histogram it tolerates slanted or narrow boxes.
            if (template.Prefixes.Length == strokes.Count)
            {
                var shape = StrokeLowerBound(cloud, template.Cloud);
                var position = 0;
                while (position < shapes.Count && shape >= shapes[position].Score) position++;
                if (position < 48)
                {
                    if (shapes.Count == 48) shapes.RemoveAt(47);
                    shapes.Insert(position, (template, shape));
                }
            }
            // A prefix can suggest an unfinished character. Never splice several characters.
            if (template.Prefixes.Length < strokes.Count) continue;
            // Prefix bounds are immutable model data. Avoid allocating and scanning a
            // normalized point array for every one of the 9,574 templates on each pen-up.
            var bounds = template.Prefixes[strokes.Count - 1];
            double distance = 0;
            for (var i = 0; i < input.Length; i++)
            {
                var point = template.Points[i];
                var dx = input[i].X - (point.X - bounds.MinX - bounds.Width / 2) / bounds.Size;
                var dy = input[i].Y - (point.Y - bounds.MinY - bounds.Height / 2) / bounds.Size;
                distance += dx * dx + dy * dy;
            }
            distance /= input.Length;
            // Prefer complete characters while allowing partial-stroke suggestions.
            distance += .012 * (template.Prefixes.Length - strokes.Count) / (strokes.Count + 1);
            AddCandidate(template.Character, distance);
        }
        foreach (var (template, _) in shapes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var distance = StrokeAssignment(cloud, template.Cloud) + .006;
            AddCandidate(template.Character, distance);
        }
        if (mergeSplitStroke && strokes.Count >= 3)
        {
            var width = strokes.SelectMany(s => s).Max(p => p.X) - strokes.SelectMany(s => s).Min(p => p.X);
            var height = strokes.SelectMany(s => s).Max(p => p.Y) - strokes.SelectMany(s => s).Min(p => p.Y);
            var threshold = Math.Max(width, height) * .08f;
            var mergedCount = 0;
            for (var i = 0; i < strokes.Count - 1 && mergedCount < 2; i++)
            {
                var dx = strokes[i][^1].X - strokes[i + 1][0].X; var dy = strokes[i][^1].Y - strokes[i + 1][0].Y;
                if (dx * dx + dy * dy > threshold * threshold || strokes[i].Length + strokes[i + 1].Length > 512) continue;
                var merged = strokes.Take(i).Concat([strokes[i].Concat(strokes[i + 1]).ToArray()]).Concat(strokes.Skip(i + 2)).ToArray();
                foreach (var candidate in Recognize(merged, limit, cancellationToken, false))
                    AddCandidate(candidate.Character, candidate.Distance + .008);
                mergedCount++;
            }
        }
        return candidates.ToArray();

        void AddCandidate(string character, double distance)
        {
            var existing = -1;
            for (var c = 0; c < candidates.Count; c++) if (candidates[c].Character == character) { existing = c; break; }
            if (existing >= 0)
            {
                if (candidates[existing].Distance <= distance) return;
                candidates.RemoveAt(existing);
            }
            var index = 0;
            while (index < candidates.Count && (distance > candidates[index].Distance || distance == candidates[index].Distance
                && StringComparer.Ordinal.Compare(character, candidates[index].Character) >= 0)) index++;
            if (index >= limit) return;
            if (candidates.Count == limit) candidates.RemoveAt(limit - 1);
            candidates.Insert(index, new(character, distance));
        }
    }

    private static InkPoint[] Cloud(InkPoint[] points)
    {
        var cloud = new InkPoint[points.Length / 2];
        for (var i = 0; i < cloud.Length; i++) cloud[i] = points[i / 8 * Samples + (i % 8 * 15 + 3) / 7];
        return cloud;
    }
    private static double StrokeCost(InkPoint[] input, InkPoint[] template, int a, int b)
    {
        double forward = 0, reverse = 0;
        for (var k = 0; k < 8; k++)
        {
            var p = input[a * 8 + k]; var q = template[b * 8 + k]; var r = template[b * 8 + 7 - k];
            forward += (p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y);
            reverse += (p.X - r.X) * (p.X - r.X) + (p.Y - r.Y) * (p.Y - r.Y);
        }
        return Math.Min(forward, reverse) / 8;
    }
    // Align one pen trace to up to three consecutive reference strokes, including
    // the connecting movement. Bounded to six missing pen lifts and <=12 strokes;
    // this is a fallback for connected handwriting, not arbitrary cursive text.
    private static double JoinedStrokeDistance(InkPoint[] input, Template template, double ceiling)
    {
        var m = input.Length / Samples; var n = template.Prefixes.Length;
        if (ceiling < 0) return double.PositiveInfinity;
        Span<double> previous = stackalloc double[n + 1], next = stackalloc double[n + 1];
        previous.Fill(double.PositiveInfinity); previous[0] = 0;
        var bounds = template.Prefixes[^1];
        for (var i = 0; i < m; i++)
        {
            next.Fill(double.PositiveInfinity);
            var viable = false;
            for (var start = i; start <= Math.Min(i + n - m, n - 1); start++)
            {
                if (!double.IsFinite(previous[start])) continue;
                for (var length = 1; length <= 3 && start + length <= n; length++)
                {
                    if (n - start - length < m - i - 1) continue;
                    double distance = 0;
                    for (var p = 0; p < Samples; p++)
                    {
                        var q = length == 1 ? template.Points[start * Samples + p]
                            : template.Joined[(start * 2 + length - 2) * Samples + p];
                        if (length == 1) q = new((q.X - bounds.MinX - bounds.Width / 2) / bounds.Size,
                            (q.Y - bounds.MinY - bounds.Height / 2) / bounds.Size);
                        var point = input[i * Samples + p]; var dx = point.X - q.X; var dy = point.Y - q.Y;
                        distance += dx * dx + dy * dy;
                    }
                    var cost = previous[start] + distance / Samples;
                    if (cost > ceiling * m) continue;
                    next[start + length] = Math.Min(next[start + length], cost);
                    viable = true;
                }
            }
            if (!viable) return double.PositiveInfinity;
            next.CopyTo(previous);
        }
        return previous[n] / m;
    }
    private static double StrokeLowerBound(InkPoint[] input, InkPoint[] template)
    {
        var n = input.Length / 8; double sum = 0;
        for (var a = 0; a < n; a++)
        {
            var best = double.PositiveInfinity;
            for (var b = 0; b < n; b++) best = Math.Min(best, StrokeCost(input, template, a, b));
            sum += best;
        }
        return sum / n;
    }
    // Minimum-cost one-to-one stroke assignment (Hungarian), accepting either pen
    // direction. Spatial placement still matters; unrelated strokes cannot all
    // match the same template stroke as with a nearest-neighbour-only metric.
    private static double StrokeAssignment(InkPoint[] input, InkPoint[] template)
    {
        var n = input.Length / 8;
        Span<double> cost = stackalloc double[n * n];
        for (var a = 0; a < n; a++) for (var b = 0; b < n; b++)
            cost[a * n + b] = StrokeCost(input, template, a, b);
        Span<double> u = stackalloc double[n + 1], v = stackalloc double[n + 1], min = stackalloc double[n + 1];
        Span<int> pMatch = stackalloc int[n + 1], way = stackalloc int[n + 1];
        Span<bool> used = stackalloc bool[n + 1];
        u.Clear(); v.Clear(); pMatch.Clear(); way.Clear();
        for (var i = 1; i <= n; i++)
        {
            pMatch[0] = i; var j0 = 0; min.Fill(double.PositiveInfinity); used.Clear();
            do
            {
                used[j0] = true; var i0 = pMatch[j0]; double delta = double.PositiveInfinity; var j1 = 0;
                for (var j = 1; j <= n; j++) if (!used[j])
                {
                    var current = cost[(i0 - 1) * n + j - 1] - u[i0] - v[j];
                    if (current < min[j]) { min[j] = current; way[j] = j0; }
                    if (min[j] < delta) { delta = min[j]; j1 = j; }
                }
                for (var j = 0; j <= n; j++)
                    if (used[j]) { u[pMatch[j]] += delta; v[j] -= delta; } else min[j] -= delta;
                j0 = j1;
            } while (pMatch[j0] != 0);
            do { var j1 = way[j0]; pMatch[j0] = pMatch[j1]; j0 = j1; } while (j0 != 0);
        }
        return -v[0] / n;
    }

    private static void Validate(IReadOnlyList<InkPoint[]> strokes)
    {
        if (strokes.Count is < 1 or > MaximumStrokes || strokes.Any(s => s.Length is < 1 or > 512
            || s.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || Math.Abs(p.X) > 100000 || Math.Abs(p.Y) > 100000)))
            throw new ArgumentException("Invalid handwriting strokes.");
    }
    private static InkPoint[] Normalize(InkPoint[][] strokes, int count)
    {
        var points = strokes.Take(count).SelectMany(s => s).ToArray();
        var minX = points.Min(p => p.X); var minY = points.Min(p => p.Y);
        var width = points.Max(p => p.X) - minX; var height = points.Max(p => p.Y) - minY;
        var size = Math.Max(Math.Max(width, height), .001f);
        for (var i = 0; i < points.Length; i++)
            points[i] = new((points[i].X - minX - width / 2) / size, (points[i].Y - minY - height / 2) / size);
        return points;
    }
    private static InkPoint[] Resample(InkPoint[] points)
    {
        var lengths = new float[points.Length];
        for (var i = 1; i < points.Length; i++)
            lengths[i] = lengths[i - 1] + MathF.Sqrt(MathF.Pow(points[i].X - points[i - 1].X, 2) + MathF.Pow(points[i].Y - points[i - 1].Y, 2));
        var result = new InkPoint[Samples]; var segment = 1;
        for (var i = 0; i < Samples; i++)
        {
            var target = lengths[^1] * i / (Samples - 1);
            while (segment < points.Length - 1 && lengths[segment] < target) segment++;
            if (points.Length == 1 || lengths[^1] < .0001f) { result[i] = points[0]; continue; }
            var span = lengths[segment] - lengths[segment - 1];
            var ratio = span <= 0 ? 0 : (target - lengths[segment - 1]) / span;
            result[i] = new(points[segment - 1].X + (points[segment].X - points[segment - 1].X) * ratio,
                points[segment - 1].Y + (points[segment].Y - points[segment - 1].Y) * ratio);
        }
        return result;
    }
}
