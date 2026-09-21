using System.Security.Cryptography;
using System.Text.Json;

namespace HanMate.Infrastructure.Voices;

public sealed record VoiceFile(string Name, string Url, long Size, string Sha256);
public sealed record VoiceSpeaker(int Id, string Name, string Gender);
public sealed record VoicePack(string Id, string Name, string Version, int Speakers, int DefaultSpeaker, string Source, string License, VoiceFile[] Files, string? ReplacesPackId = null, VoiceSpeaker[]? Voices = null, bool PreserveSpeakerOnReplace = false)
{
    public long Bytes => Files.Sum(f => f.Size);
    public static VoicePack Bundled { get; } = JsonSerializer.Deserialize<VoicePack>(typeof(VoicePack).Assembly.GetManifestResourceStream("Voices.catalog.json")!)!;
    public static VoicePack Kokoro { get; } = JsonSerializer.Deserialize<VoicePack>(typeof(VoicePack).Assembly.GetManifestResourceStream("Voices.catalog-kokoro.json")!)!;
}
public sealed record VoicePackState(bool Installed, int? Speaker, bool HasFiles = false);

/// <summary>Only the shipped, pinned catalog is accepted. No model code, archives, or user text goes over the network.</summary>
public sealed class VoicePackStore
{
    private readonly string _root, _installed, _selection;
    private readonly HttpClient _http;
    private readonly Func<string, Task<Stream>>? _bundledFile;
    public bool HasBundledFiles => _bundledFile is not null;
    private readonly TimeSpan _downloadIdleTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Private installed files are immutable through this store. Cache only verified
    // bytes, and invalidate on file metadata changes or our own install/remove.
    private readonly Dictionary<string, (long Length, DateTime Written, DateTime Created)> _verified = new(StringComparer.Ordinal);
    public VoicePack Pack { get; }
    public VoicePackStore(string root, HttpClient http, VoicePack? pack = null, TimeSpan? downloadIdleTimeout = null, Func<string, Task<Stream>>? bundledFile = null)
    {
        _downloadIdleTimeout = downloadIdleTimeout ?? TimeSpan.FromSeconds(30);
        if (_downloadIdleTimeout <= TimeSpan.Zero || _downloadIdleTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(downloadIdleTimeout));
        Pack = pack ?? VoicePack.Bundled; _bundledFile = bundledFile;
        if (Pack.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || Pack.Files.Length is < 1 or > 1024 || Pack.Bytes > 512L * 1024 * 1024
            || Pack.Files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Pack.Files.Length
            || Pack.Files.Any(f => !SafeRelativeFile(f.Name)
                || Pack.Files.Any(other => other.Name.StartsWith(f.Name + "/", StringComparison.OrdinalIgnoreCase))
                || f.Size <= 0 || f.Sha256.Length != 64 || f.Sha256.Any(c => !char.IsAsciiHexDigit(c))
                || !Uri.TryCreate(f.Url, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "huggingface.co"))
            throw new InvalidDataException("Invalid voice catalog.");
        if (Pack.ReplacesPackId is { } old && (old.Length == 0 || old.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || old == Pack.Id))
            throw new InvalidDataException("Invalid previous voice pack.");
        _root = Path.GetFullPath(root); _installed = Path.Combine(_root, Pack.Id); _selection = Path.Combine(_root, "selection.json"); _http = http;
    }
    private static bool SafeRelativeFile(string name) => !string.IsNullOrEmpty(name) && !name.Contains('\\') &&
        name.Split('/').All(part => part.Length > 0 && part is not "." and not ".." &&
            part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !part.Contains(':'));
    public async Task<VoicePackState> StateAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { await EnsureBundledAsync(token); var installed = await ValidAsync(_installed, token); return new(installed, installed ? await SelectedAsync(token) : null, Directory.Exists(_installed)); }
        finally { _gate.Release(); }
    }
    private async Task<int?> SelectedAsync(CancellationToken token)
    {
        if (!File.Exists(_selection)) return null;
        try
        {
            if (new FileInfo(_selection).Length > 256) return null;
            var selected = JsonSerializer.Deserialize<Selection>(await File.ReadAllTextAsync(_selection, token));
            return selected?.Pack == Pack.Id && selected.Speaker >= 0 && selected.Speaker < Pack.Speakers ? selected.Speaker : null;
        }
        catch (JsonException) { return null; }
    }
    private sealed record Selection(string Pack, int Speaker);
    public async Task SelectAsync(int? speaker, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            await EnsureBundledAsync(token);
            if (speaker is null) { File.Delete(_selection); return; }
            if (speaker < 0 || speaker >= Pack.Speakers || !await ValidAsync(_installed, token)) throw new InvalidDataException("Voice not installed.");
            Directory.CreateDirectory(_root);
            var pending = _selection + ".part";
            try { await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(new Selection(Pack.Id, speaker.Value)), token); File.Move(pending, _selection, true); }
            finally { File.Delete(pending); }
        }
        finally { _gate.Release(); }
    }
    public async Task InstallAsync(IProgress<double>? progress, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { await EnsureBundledAsync(token); await InstallCoreAsync(progress, token); }
        finally { _gate.Release(); }
    }
    private async Task InstallCoreAsync(IProgress<double>? progress, CancellationToken token)
    {
        string? stage = null;
        try
        {
            if (await ValidAsync(_installed, token)) { progress?.Report(1); return; }
            Directory.CreateDirectory(_root);
            // Recover only our own interrupted install directories, never learning data.
            foreach (var old in Directory.EnumerateDirectories(_root, "stage-*"))
                if (Guid.TryParseExact(Path.GetFileName(old)[6..], "N", out _)) DeleteOwnedDirectory(old);
            stage = Path.Combine(_root, "stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            long total = 0;
            foreach (var file in Pack.Files)
            {
                token.ThrowIfCancellationRequested();
                using var response = _bundledFile is null ? await _http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, token) : null;
                if (response is not null)
                {
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage?.RequestUri?.Scheme != "https" || (response.Content.Headers.ContentLength is { } size && size != file.Size))
                        throw new InvalidDataException("Voice download size or transport changed.");
                }
                await using var input = response is null ? await _bundledFile!(file.Name) : await response.Content.ReadAsStreamAsync(token);
                var destination = Path.Combine(stage, file.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    var buffer = new byte[65536]; long count = 0; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    while (true)
                    {
                        // ResponseHeadersRead ends HttpClient.Timeout at the headers. Bound body stalls separately.
                        int n;
                        using (var idle = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            idle.CancelAfter(_downloadIdleTimeout);
                            try { n = await input.ReadAsync(buffer, idle.Token); }
                            catch (OperationCanceledException e) when (!token.IsCancellationRequested)
                            { throw new TimeoutException("Voice download stopped receiving data.", e); }
                        }
                        if (n == 0) break;
                        count += n; if (count > file.Size) throw new InvalidDataException("Voice download exceeds limit.");
                        hash.AppendData(buffer, 0, n); await output.WriteAsync(buffer.AsMemory(0, n), token);
                        progress?.Report((double)(total + count) / Pack.Bytes);
                    }
                    if (count != file.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != file.Sha256) throw new InvalidDataException("Voice download checksum mismatch.");
                    await output.FlushAsync(token); output.Flush(true); total += count;
                }
            }
            token.ThrowIfCancellationRequested();
            _verified.Clear();
            if (Directory.Exists(_installed)) DeleteOwnedDirectory(_installed); // Only a broken version can reach this branch.
            Directory.Move(stage, _installed); stage = null; progress?.Report(1);
        }
        catch (OperationCanceledException e) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Voice download timed out.", e); }
        finally { if (stage is not null) DeleteOwnedDirectory(stage); }
    }
    public async Task RemoveAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            // Explicit removal must survive restarts instead of triggering automatic extraction.
            if (_bundledFile is not null) await WriteBootstrapAsync(new(false, true), token);
            _verified.Clear();
            File.Delete(_selection); if (Directory.Exists(_installed)) DeleteOwnedDirectory(_installed);
        }
        finally { _gate.Release(); }
    }
    public async Task<T> UseAsync<T>(int speaker, Func<string, Task<T>> use, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            await EnsureBundledAsync(token);
            if (speaker < 0 || speaker >= Pack.Speakers || !await ValidAsync(_installed, token)) throw new InvalidDataException("Voice not installed or damaged.");
            return await use(_installed);
        }
        finally { _gate.Release(); }
    }
    private sealed record Bootstrap(bool EnableDefault, bool Complete);
    private async Task EnsureBundledAsync(CancellationToken token)
    {
        if (_bundledFile is null) return;
        var marker = BootstrapPath;
        Bootstrap state;
        if (File.Exists(marker))
            state = JsonSerializer.Deserialize<Bootstrap>(await File.ReadAllTextAsync(marker, token))
                ?? throw new InvalidDataException("Invalid bundled voice state.");
        else
        {
            // An existing voice directory may represent a deliberately disabled or
            // removed old pack. Only a genuinely new voice installation auto-enables.
            var existing = Directory.Exists(_root);
            var replaceSelected = await HasPreviousSelectionAsync(token);
            var previousInstalled = Pack.ReplacesPackId is { } old && Directory.Exists(Path.Combine(_root, old));
            state = new(!existing || replaceSelected,
                existing && !previousInstalled && !Directory.Exists(_installed) && !File.Exists(_selection));
            await WriteBootstrapAsync(state, token);
        }
        if (state.Complete) return;
        await InstallCoreAsync(null, token);
        if (state.EnableDefault && (!File.Exists(_selection) || await HasPreviousSelectionAsync(token)))
        {
            var speaker = Pack.DefaultSpeaker;
            if (Pack.PreserveSpeakerOnReplace && await HasPreviousSelectionAsync(token))
            {
                var previous = JsonSerializer.Deserialize<Selection>(await File.ReadAllTextAsync(_selection, token));
                if (previous is not null && previous.Speaker < Pack.Speakers && Pack.Voices?.Any(v => v.Id == previous.Speaker) == true)
                    speaker = previous.Speaker;
            }
            var pending = _selection + ".part";
            try
            {
                await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(new Selection(Pack.Id, speaker)), token);
                File.Move(pending, _selection, true);
            }
            finally { File.Delete(pending); }
        }
        await WriteBootstrapAsync(state with { Complete = true }, token);
    }
    private string BootstrapPath => Path.Combine(_root, Pack.ReplacesPackId is null ? "bundled.json" : "bundled-" + Pack.Id + ".json");
    private async Task<bool> HasPreviousSelectionAsync(CancellationToken token)
    {
        if (Pack.ReplacesPackId is null || !File.Exists(_selection) || new FileInfo(_selection).Length > 256) return false;
        try
        {
            var value = JsonSerializer.Deserialize<Selection>(await File.ReadAllTextAsync(_selection, token));
            return value?.Pack == Pack.ReplacesPackId && value.Speaker >= 0;
        }
        catch (JsonException) { return false; }
    }
    private async Task WriteBootstrapAsync(Bootstrap state, CancellationToken token)
    {
        Directory.CreateDirectory(_root);
        var target = BootstrapPath; var pending = target + ".part";
        try { await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(state), token); File.Move(pending, target, true); }
        finally { File.Delete(pending); }
    }
    private async Task<bool> ValidAsync(string directory, CancellationToken token)
    {
        foreach (var file in Pack.Files)
        {
            token.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, file.Name);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Size) { _verified.Remove(path); return false; }
            var stamp = (info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc);
            if (_verified.TryGetValue(path, out var verified) && verified == stamp) continue;
            _verified.Remove(path);
            await using var stream = File.OpenRead(path);
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token)) != file.Sha256) return false;
            info.Refresh();
            if (!info.Exists || (info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc) != stamp) return false;
            _verified[path] = stamp;
        }
        return true;
    }
    private void DeleteOwnedDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || full == _root
            || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException("Invalid voice directory.");
        Directory.Delete(full, true);
    }
}
