using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HanMate.Infrastructure.Sharing;

public sealed class LanShareReceiver : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly SslStream _stream;
    private bool _received;
    private bool _completed;

    private LanShareReceiver(TcpClient client, SslStream stream) { _client = client; _stream = stream; }

    public static async Task<LanShareReceiver> ConnectAsync(LanInvite invite, string deviceName, CancellationToken token = default)
    {
        if (!LanInvite.TryParse(invite.ToString(), out _)) throw new ArgumentException("Invalid invite.", nameof(invite));
        var client = new TcpClient(); SslStream? secure = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await client.ConnectAsync(invite.Host, invite.Port, timeout.Token);
            secure = new SslStream(client.GetStream(), false, (_, certificate, _, _) => certificate is not null &&
                LanWire.SameHex(invite.CertificateSha256,
                    Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())).ToLowerInvariant()));
            await secure.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = LanWire.TlsHost, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, timeout.Token);
            await LanWire.WriteAsync(secure, new LanFrame { Type = "join", Token = invite.Token,
                Name = string.IsNullOrWhiteSpace(deviceName) ? "HanMate" : deviceName.Trim()[..Math.Min(deviceName.Trim().Length, 80)] }, timeout.Token);
            if ((await LanWire.ReadAsync(secure, timeout.Token)).Type != "ready") throw new InvalidDataException("LAN_JOIN_REJECTED");
            return new(client, secure);
        }
        catch
        {
            if (secure is not null) await secure.DisposeAsync();
            client.Dispose(); throw;
        }
    }

    public async Task<LanReceivedPackage> ReceiveAsync(Stream destination, IProgress<long>? progress = null,
        CancellationToken token = default)
    {
        if (_received) throw new InvalidOperationException("LAN_RECEIVE_ALREADY_STARTED");
        _received = true;
        var header = await LanWire.ReadAsync(_stream, token);
        if (header.Type != "package" || header.Length is < 1 or > LanWire.MaxPackageBytes ||
            header.Sha256 is not { Length: 64 } || !header.Sha256.All(Uri.IsHexDigit) ||
            header.Engine is not ("Melo" or "Kokoro" or "System") || header.IncludedRecordings is < 0 or > 1000 ||
            header.OfflineSpeaker is < 0 or > 1024 || header.Engine == "System" && header.OfflineSpeaker is not null ||
            header.WordCategories is { Count: > 100 } ||
            header.WordCategories is { } categories && (categories.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != categories.Count ||
                categories.Any(x => x.Id.Length > 50 || x.Name.Length is < 1 or > 40 ||
                    x.Name.Any(char.IsControl) || x.Id != "other" &&
                    !HanMate.Core.Content.WordCategories.All.Contains(x.Id) &&
                    !HanMate.Infrastructure.Database.CustomWordCategoryStore.IsCustom(x.Id))))
            throw new InvalidDataException("LAN_PACKAGE_INVALID");
        var buffer = new byte[64 * 1024]; long received = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            while (received < header.Length)
            {
                var count = await _stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, header.Length - received)), token);
                if (count == 0) throw new EndOfStreamException();
                await destination.WriteAsync(buffer.AsMemory(0, count), token);
                hash.AppendData(buffer, 0, count); received += count;
                progress?.Report(received);
                await LanWire.WriteAsync(_stream, new LanFrame { Type = "progress", Received = received }, token);
            }
            await destination.FlushAsync(token);
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!LanWire.SameHex(header.Sha256, actual)) throw new InvalidDataException("LAN_PACKAGE_HASH_MISMATCH");
            await LanWire.WriteAsync(_stream, new LanFrame { Type = "received", Received = received }, token);
            return new(received, actual, new(header.Engine, header.IncludedRecordings, header.OfflineSpeaker,
                header.WordCategories));
        }
        catch
        {
            try { await CompleteAsync(false, "Transfer failed", CancellationToken.None); } catch { }
            throw;
        }
    }

    public async Task CompleteAsync(bool accepted, string? detail = null, CancellationToken token = default)
    {
        if (_completed) return;
        await LanWire.WriteAsync(_stream, new LanFrame { Type = accepted ? "completed" : "rejected",
            Detail = detail is null ? null : detail[..Math.Min(detail.Length, 200)] }, token);
        _completed = true;
    }

    public async ValueTask DisposeAsync() { await _stream.DisposeAsync(); _client.Dispose(); }
}
