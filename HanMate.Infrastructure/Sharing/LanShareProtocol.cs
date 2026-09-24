using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace HanMate.Infrastructure.Sharing;

public sealed record LanInvite(string Host, int Port, string Token, string CertificateSha256)
{
    public override string ToString() => $"hanmate://join?host={Uri.EscapeDataString(Host)}&port={Port}&key={Token}&cert={CertificateSha256}";

    public static bool TryParse(string? value, out LanInvite? invite)
    {
        invite = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "hanmate" || uri.Host != "join") return false;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || !fields.TryAdd(pair[0], Uri.UnescapeDataString(pair[1]))) return false;
        }
        if (!fields.TryGetValue("host", out var host) || !IPAddress.TryParse(host, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork || !LanAddresses.IsPrivate(address) ||
            !fields.TryGetValue("port", out var portText) || !int.TryParse(portText, out var port) || port is < 1 or > 65535 ||
            !fields.TryGetValue("key", out var key) || !Hex(key, 32) ||
            !fields.TryGetValue("cert", out var certificate) || !Hex(certificate, 64)) return false;
        invite = new(host, port, key.ToLowerInvariant(), certificate.ToLowerInvariant());
        return true;
    }

    private static bool Hex(string value, int length) => value.Length == length && value.All(Uri.IsHexDigit);
}

public static class LanAddresses
{
    public static IReadOnlyList<string> Candidates() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(network => network.OperationalStatus == OperationalStatus.Up)
        .SelectMany(network => network.GetIPProperties().UnicastAddresses.Select(address => (network, address.Address)))
        .Where(pair => pair.network.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            pair.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(pair.Address) &&
            !IPAddress.IsLoopback(pair.Address))
        .OrderBy(pair => pair.network.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
        .ThenBy(pair => pair.network.Name, StringComparer.Ordinal)
        .Select(pair => pair.Address.ToString()).Distinct().ToArray();

    public static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 127;
    }
}

public sealed record LanWordCategory(string Id, string Name);
public sealed record LanAudioSettings(string Engine, int IncludedRecordings, int? OfflineSpeaker = null,
    IReadOnlyList<LanWordCategory>? WordCategories = null);
public sealed record LanReceivedPackage(long Bytes, string Sha256, LanAudioSettings AudioSettings);
public enum LanPeerState { Joined, Sending, Receiving, WaitingForImport, Completed, Rejected, Failed }
public sealed record LanPeerProgress(Guid Id, string Name, LanPeerState State, long ReceivedBytes, long TotalBytes, string? Detail = null);

internal sealed record LanFrame
{
    public required string Type { get; init; }
    public string? Token { get; init; }
    public string? Name { get; init; }
    public string? Detail { get; init; }
    public string? Sha256 { get; init; }
    public string? Engine { get; init; }
    public int? OfflineSpeaker { get; init; }
    public long Length { get; init; }
    public long Received { get; init; }
    public int IncludedRecordings { get; init; }
    public IReadOnlyList<LanWordCategory>? WordCategories { get; init; }
}

internal static class LanWire
{
    internal const string TlsHost = "hanmate.local";
    public const long MaxPackageBytes = 32L * 1024 * 1024;
    private const int MaxFrameBytes = 32768;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(Stream stream, LanFrame frame, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions);
        if (bytes.Length > MaxFrameBytes) throw new InvalidDataException("LAN_FRAME_TOO_LARGE");
        var length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        await stream.WriteAsync(length, token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    public static async Task<LanFrame> ReadAsync(Stream stream, CancellationToken token)
    {
        var length = new byte[4]; await stream.ReadExactlyAsync(length, token);
        var count = BinaryPrimitives.ReadInt32BigEndian(length);
        if (count is < 2 or > MaxFrameBytes) throw new InvalidDataException("LAN_FRAME_INVALID");
        var bytes = new byte[count]; await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<LanFrame>(bytes, JsonOptions) ?? throw new InvalidDataException("LAN_FRAME_INVALID");
    }

    public static bool SameHex(string expected, string? actual)
    {
        if (actual is null || expected.Length != actual.Length || !actual.All(Uri.IsHexDigit)) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual));
    }
}
