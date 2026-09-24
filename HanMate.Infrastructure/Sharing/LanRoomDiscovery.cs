using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace HanMate.Infrastructure.Sharing;

public sealed record LanDiscoveredRoom(string Name, LanInvite Invite);

/// <summary>Finds a room on the local network by a two-digit label. The label is not an authentication secret.</summary>
public static class LanRoomDiscovery
{
    public const int Port = 36849;
    private const int MaxDatagramBytes = 1024;
    private sealed record Probe(string Type, string Code, string Nonce);
    private sealed record Reply(string Type, string Code, string Nonce, string Name, string Invite);

    public static bool IsValidCode(string? code) => code is { Length: 2 } &&
        char.IsAsciiDigit(code[0]) && char.IsAsciiDigit(code[1]);

    public static async Task<IReadOnlyList<LanDiscoveredRoom>> FindAsync(string code,
        CancellationToken token = default, IEnumerable<IPAddress>? targets = null)
    {
        if (!IsValidCode(code)) throw new ArgumentException("Room number must have two digits.", nameof(code));
        using var client = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var probe = JsonSerializer.SerializeToUtf8Bytes(new Probe("find", code, nonce));
        var destinations = (targets ?? BroadcastTargets()).Distinct().ToArray();
        if (destinations.Length == 0) return [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var sending = SendProbesAsync();
        var found = new Dictionary<string, LanDiscoveredRoom>(StringComparer.Ordinal);
        try
        {
            while (true)
            {
                UdpReceiveResult packet;
                try { packet = await client.ReceiveAsync(timeout.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { break; }
                if (packet.Buffer.Length > MaxDatagramBytes || !LanAddresses.IsPrivate(packet.RemoteEndPoint.Address)) continue;
                Reply? reply;
                try { reply = JsonSerializer.Deserialize<Reply>(packet.Buffer); }
                catch (JsonException) { continue; }
                if (reply is null || reply.Type != "room" || reply.Code != code || reply.Nonce != nonce ||
                    string.IsNullOrWhiteSpace(reply.Name) || reply.Name.Length > 80 ||
                    !LanInvite.TryParse(reply.Invite, out var invite) ||
                    invite!.Host != packet.RemoteEndPoint.Address.ToString()) continue;
                found[reply.Invite] = new(reply.Name.Trim(), invite);
                if (found.Count == 1) timeout.CancelAfter(TimeSpan.FromMilliseconds(500));
            }
        }
        finally { try { await sending; } catch (OperationCanceledException) when (timeout.IsCancellationRequested) { } }
        return found.Values.OrderBy(room => room.Name, StringComparer.Ordinal).ThenBy(room => room.Invite.Host).ToArray();

        async Task SendProbesAsync()
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                foreach (var destination in destinations)
                {
                    try { await client.SendAsync(probe.AsMemory(), new IPEndPoint(destination, Port), timeout.Token); }
                    catch (SocketException) { /* Another interface may still be reachable. */ }
                }
                if (attempt < 2) await Task.Delay(550, timeout.Token);
            }
        }
    }

    private static IEnumerable<IPAddress> BroadcastTargets()
    {
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            foreach (var address in network.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork ||
                    !LanAddresses.IsPrivate(address.Address)) continue;
                var ip = address.Address.GetAddressBytes();
                var mask = address.IPv4Mask?.GetAddressBytes();
                if (mask is null && address.PrefixLength is > 0 and < 31)
                {
                    var prefix = address.PrefixLength;
                    mask = Enumerable.Range(0, 4).Select(i => (byte)(uint.MaxValue << (32 - prefix) >> (24 - i * 8))).ToArray();
                }
                if (mask is not { Length: 4 }) continue;
                targets.Add(new IPAddress(Enumerable.Range(0, 4).Select(i => (byte)(ip[i] | ~mask[i])).ToArray()));
            }
        }
        catch (NetworkInformationException) { /* Global broadcast can still work. */ }
        catch (PlatformNotSupportedException) { /* Global broadcast can still work. */ }
        return targets;
    }

    internal sealed class Host : IAsyncDisposable
    {
        private readonly UdpClient _client = new(new IPEndPoint(IPAddress.Any, Port));
        private readonly CancellationTokenSource _lifetime = new();
        private readonly string _code, _name;
        private readonly LanInvite _invite;
        private readonly Task _loop;

        public Host(string code, string name, LanInvite invite)
        {
            if (!IsValidCode(code)) throw new ArgumentException("Room number must have two digits.", nameof(code));
            _code = code; _name = string.IsNullOrWhiteSpace(name) ? "HanMate" : name.Trim()[..Math.Min(name.Trim().Length, 80)];
            _invite = invite;
            _loop = ListenAsync(_lifetime.Token);
        }

        private async Task ListenAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult packet;
                try { packet = await _client.ReceiveAsync(token); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (token.IsCancellationRequested) { break; }
                if (packet.Buffer.Length > MaxDatagramBytes || !LanAddresses.IsPrivate(packet.RemoteEndPoint.Address)) continue;
                Probe? probe;
                try { probe = JsonSerializer.Deserialize<Probe>(packet.Buffer); }
                catch (JsonException) { continue; }
                if (probe is null || probe.Type != "find" || probe.Code != _code ||
                    probe.Nonce is not { Length: 32 } || !probe.Nonce.All(Uri.IsHexDigit)) continue;
                var routedHost = RoutedAddress(packet.RemoteEndPoint) ?? _invite.Host;
                var response = JsonSerializer.SerializeToUtf8Bytes(new Reply("room", _code, probe.Nonce,
                    _name, (_invite with { Host = routedHost }).ToString()));
                if (response.Length > MaxDatagramBytes) continue;
                try { await _client.SendAsync(response.AsMemory(), packet.RemoteEndPoint, token); }
                catch (SocketException) { /* The requester may have left the network. */ }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) when (token.IsCancellationRequested) { break; }
            }
        }

        private static string? RoutedAddress(IPEndPoint remote)
        {
            try
            {
                using var route = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                route.Connect(remote);
                var address = ((IPEndPoint)route.LocalEndPoint!).Address;
                return LanAddresses.IsPrivate(address) ? address.ToString() : null;
            }
            catch (SocketException) { return null; }
        }

        public async ValueTask DisposeAsync()
        {
            await _lifetime.CancelAsync(); _client.Dispose();
            try { await _loop; } catch (OperationCanceledException) { }
            _lifetime.Dispose();
        }
    }
}
