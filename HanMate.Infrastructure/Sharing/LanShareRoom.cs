using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HanMate.Infrastructure.Sharing;

/// <summary>A temporary TLS room. Discovery advertises a pinned invitation only to a matching local room-number probe.</summary>
public sealed class LanShareRoom : IAsyncDisposable
{
    public const long MaxPackageBytes = LanWire.MaxPackageBytes;
    private sealed class Peer(Guid id, string name, TcpClient client, SslStream stream)
    {
        public Guid Id { get; } = id;
        public string Name { get; } = name;
        public TcpClient Client { get; } = client;
        public SslStream Stream { get; } = stream;
    }

    private readonly TcpListener _listener;
    private readonly RSA _key;
    private readonly X509Certificate2 _certificate;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<Guid, Peer> _peers = new();
    private readonly string _joinKey;
    private readonly Task _acceptLoop;
    private LanRoomDiscovery.Host? _discovery;
    private int _sending;
    private bool _disposed;

    public LanInvite Invite { get; }
    public event Action<LanPeerProgress>? ProgressChanged;
    public event Action<string>? JoinFailed;
    public IReadOnlyList<LanPeerProgress> Peers => _peers.Values.Select(p =>
        new LanPeerProgress(p.Id, p.Name, LanPeerState.Joined, 0, 0)).OrderBy(p => p.Name).ToArray();

    private LanShareRoom(string advertisedHost)
    {
        if (!IPAddress.TryParse(advertisedHost, out var address) || !LanAddresses.IsPrivate(address))
            throw new ArgumentException("A local IPv4 address is required.", nameof(advertisedHost));
        _key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + LanWire.TlsHost, _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(LanWire.TlsHost);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(12));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12, password),
            password, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
        _joinKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start(16);
        Invite = new(advertisedHost, ((IPEndPoint)_listener.LocalEndpoint).Port, _joinKey,
            Convert.ToHexString(SHA256.HashData(_certificate.RawData)).ToLowerInvariant());
        _acceptLoop = AcceptLoopAsync(_lifetime.Token);
    }

    public static async Task<LanShareRoom> StartAsync(string advertisedHost, string? roomCode = null, string? deviceName = null)
    {
        if (roomCode is not null && !LanRoomDiscovery.IsValidCode(roomCode))
            throw new ArgumentException("Room number must have two digits.", nameof(roomCode));
        var room = new LanShareRoom(advertisedHost);
        try
        {
            if (roomCode is not null) room._discovery = new LanRoomDiscovery.Host(roomCode, deviceName ?? "HanMate", room.Invite);
            return room;
        }
        catch { await room.DisposeAsync(); throw; }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(token); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            _ = JoinAsync(client, token);
        }
    }

    private async Task JoinAsync(TcpClient client, CancellationToken lifetime)
    {
        SslStream? secure = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            secure = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await secure.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _certificate, ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, timeout.Token);
            var hello = await LanWire.ReadAsync(secure, timeout.Token);
            if (hello.Type != "join" || !LanWire.SameHex(_joinKey, hello.Token) ||
                string.IsNullOrWhiteSpace(hello.Name) || hello.Name.Length > 80 || _peers.Count >= 16)
            {
                await LanWire.WriteAsync(secure, new LanFrame { Type = "rejected" }, timeout.Token);
                return;
            }
            var peer = new Peer(Guid.NewGuid(), hello.Name.Trim(), client, secure);
            if (!_peers.TryAdd(peer.Id, peer)) return;
            await LanWire.WriteAsync(secure, new LanFrame { Type = "ready" }, timeout.Token);
            ProgressChanged?.Invoke(new(peer.Id, peer.Name, LanPeerState.Joined, 0, 0));
            secure = null; // The room now owns the connection.
        }
        catch (Exception error) when (error is IOException or AuthenticationException or OperationCanceledException or InvalidDataException or SocketException)
        { JoinFailed?.Invoke(error.Message); }
        finally { if (secure is not null) { await secure.DisposeAsync(); client.Dispose(); } }
    }

    public async Task SendAsync(IReadOnlyCollection<Guid> selected, string packagePath, LanAudioSettings settings,
        CancellationToken token = default)
    {
        if (selected.Count == 0 || selected.Distinct().Count() != selected.Count ||
            selected.Any(id => !_peers.ContainsKey(id))) throw new InvalidOperationException("LAN_PEER_SELECTION_INVALID");
        if (Interlocked.CompareExchange(ref _sending, 1, 0) != 0) throw new InvalidOperationException("LAN_SEND_BUSY");
        try
        {
            var file = new FileInfo(packagePath);
            if (!file.Exists || file.Length is < 1 or > LanWire.MaxPackageBytes) throw new InvalidDataException("LAN_PACKAGE_LIMIT");
            string hash;
            await using (var input = file.OpenRead()) hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token)).ToLowerInvariant();
            await Task.WhenAll(selected.Select(id => SendOneAsync(_peers[id], file.FullName, file.Length, hash, settings, token)));
        }
        finally { Interlocked.Exchange(ref _sending, 0); }
    }

    private async Task SendOneAsync(Peer peer, string path, long length, string hash, LanAudioSettings settings, CancellationToken token)
    {
        Task? acknowledgements = null;
        CancellationTokenSource? timeout = null;
        try
        {
            timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            var ct = timeout.Token;
            await LanWire.WriteAsync(peer.Stream, new LanFrame { Type = "package", Length = length, Sha256 = hash,
                Engine = settings.Engine, IncludedRecordings = settings.IncludedRecordings,
                OfflineSpeaker = settings.OfflineSpeaker, WordCategories = settings.WordCategories }, ct);
            acknowledgements = ReadAcknowledgementsAsync(peer, length, ct);
            await using var input = File.OpenRead(path);
            var buffer = new byte[64 * 1024]; long sent = 0;
            while (sent < length)
            {
                var count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - sent)), ct);
                if (count == 0) throw new EndOfStreamException();
                await peer.Stream.WriteAsync(buffer.AsMemory(0, count), ct);
                sent += count;
                ProgressChanged?.Invoke(new(peer.Id, peer.Name, LanPeerState.Sending, 0, length));
            }
            await peer.Stream.FlushAsync(ct);
            await acknowledgements;
        }
        catch (Exception error) when (error is IOException or AuthenticationException or InvalidDataException or
            OperationCanceledException or SocketException or EndOfStreamException)
        {
            if (timeout is not null) await timeout.CancelAsync();
            if (acknowledgements is not null)
                try { await acknowledgements; } catch (Exception) { /* The send failure is the user-facing result. */ }
            ProgressChanged?.Invoke(new(peer.Id, peer.Name, LanPeerState.Failed, 0, length, error.GetType().Name));
        }
        finally
        {
            timeout?.Dispose();
            _peers.TryRemove(peer.Id, out _);
            await peer.Stream.DisposeAsync(); peer.Client.Dispose();
        }
    }

    private async Task ReadAcknowledgementsAsync(Peer peer, long total, CancellationToken token)
    {
        long last = 0;
        while (true)
        {
            var message = await LanWire.ReadAsync(peer.Stream, token);
            switch (message.Type)
            {
                case "progress":
                    if (message.Received < last || message.Received > total) throw new InvalidDataException("LAN_PROGRESS_INVALID");
                    last = message.Received;
                    ProgressChanged?.Invoke(new(peer.Id, peer.Name, LanPeerState.Receiving, last, total));
                    break;
                case "received":
                    if (message.Received != total) throw new InvalidDataException("LAN_PROGRESS_INVALID");
                    ProgressChanged?.Invoke(new(peer.Id, peer.Name, LanPeerState.WaitingForImport, total, total));
                    break;
                case "completed":
                    ProgressChanged?.Invoke(new(peer.Id, peer.Name, LanPeerState.Completed, total, total));
                    return;
                case "rejected":
                    ProgressChanged?.Invoke(new(peer.Id, peer.Name, LanPeerState.Rejected, last, total, message.Detail));
                    return;
                default: throw new InvalidDataException("LAN_FRAME_INVALID");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true;
        if (_discovery is not null) await _discovery.DisposeAsync();
        _lifetime.Cancel(); _listener.Stop();
        foreach (var peer in _peers.Values) { await peer.Stream.DisposeAsync(); peer.Client.Dispose(); }
        _peers.Clear();
        try { await _acceptLoop; } catch (OperationCanceledException) { }
        _certificate.Dispose(); _key.Dispose(); _lifetime.Dispose();
    }
}
