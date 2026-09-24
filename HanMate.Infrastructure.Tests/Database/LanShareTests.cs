using System.Collections.Concurrent;
using HanMate.Infrastructure.Sharing;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class LanShareTests
{
    [Fact]
    public async Task OneRoomSendsToSelectedReceiversAndReportsTheirProgress()
    {
        using var dir = new TestDatabaseDirectory();
        var path = Path.Combine(Path.GetDirectoryName(dir.DatabasePath)!, "sample.hanpack");
        var bytes = Enumerable.Range(0, 180_000).Select(i => (byte)(i * 17)).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        await using var room = await LanShareRoom.StartAsync("127.0.0.1");
        Assert.True(LanInvite.TryParse(room.Invite.ToString(), out var invite));
        var updates = new ConcurrentBag<LanPeerProgress>(); room.ProgressChanged += updates.Add;
        var joinErrors = new ConcurrentBag<string>(); room.JoinFailed += joinErrors.Add;
        LanShareReceiver firstClient;
        try { firstClient = await LanShareReceiver.ConnectAsync(invite!, "phone A"); }
        catch (Exception error) { throw new InvalidOperationException(string.Join("; ", joinErrors), error); }
        await using var first = firstClient;
        await using var second = await LanShareReceiver.ConnectAsync(invite!, "phone B");
        await UntilAsync(() => room.Peers.Count == 2);
        var selected = room.Peers.Select(peer => peer.Id).ToArray();
        using var firstBytes = new MemoryStream(); using var secondBytes = new MemoryStream();
        var categories = new[] { new LanWordCategory("custom-" + Guid.NewGuid().ToString("N"), "Practice words") };
        var send = room.SendAsync(selected, path, new("Melo", 2, 1, categories));
        var receiveFirst = first.ReceiveAsync(firstBytes);
        var receiveSecond = second.ReceiveAsync(secondBytes);
        var packages = await Task.WhenAll(receiveFirst, receiveSecond);
        Assert.All(packages, package =>
        {
            Assert.Equal("Melo", package.AudioSettings.Engine);
            Assert.Equal(2, package.AudioSettings.IncludedRecordings);
            Assert.Equal(1, package.AudioSettings.OfflineSpeaker);
            Assert.Equal(categories, package.AudioSettings.WordCategories);
        });
        await first.CompleteAsync(true); await second.CompleteAsync(true);
        await send;
        Assert.Equal(bytes, firstBytes.ToArray()); Assert.Equal(bytes, secondBytes.ToArray());
        Assert.Equal(2, updates.Count(update => update.State == LanPeerState.Completed));
        Assert.Contains(updates, update => update.State == LanPeerState.Receiving && update.ReceivedBytes == bytes.Length);
    }

    [Fact]
    public async Task InvitePinRejectsAnotherCertificate()
    {
        await using var room = await LanShareRoom.StartAsync("127.0.0.1");
        var wrong = room.Invite with { CertificateSha256 = new string('0', 64) };
        await Assert.ThrowsAnyAsync<Exception>(() => LanShareReceiver.ConnectAsync(wrong, "phone"));
        Assert.Empty(room.Peers);
    }

    [Fact]
    public async Task UnselectedReceiverWaitsWhileSelectedReceiverCanRejectImport()
    {
        using var dir = new TestDatabaseDirectory();
        var path = Path.Combine(Path.GetDirectoryName(dir.DatabasePath)!, "selected.hanpack");
        await File.WriteAllBytesAsync(path, new byte[4096]);
        await using var room = await LanShareRoom.StartAsync("127.0.0.1");
        await using var selected = await LanShareReceiver.ConnectAsync(room.Invite, "selected");
        await using var waiting = await LanShareReceiver.ConnectAsync(room.Invite, "waiting");
        await UntilAsync(() => room.Peers.Count == 2);
        var selectedId = Assert.Single(room.Peers, peer => peer.Name == "selected").Id;
        var waitingId = Assert.Single(room.Peers, peer => peer.Name == "waiting").Id;
        var updates = new ConcurrentBag<LanPeerProgress>(); room.ProgressChanged += updates.Add;
        var send = room.SendAsync([selectedId], path, new("System", 0));
        using var output = new MemoryStream();
        await selected.ReceiveAsync(output);
        await selected.CompleteAsync(false, "not imported");
        await send;
        Assert.Contains(updates, update => update.Id == selectedId && update.State == LanPeerState.Rejected);
        Assert.Contains(room.Peers, peer => peer.Id == waitingId);
        Assert.DoesNotContain(updates, update => update.Id == waitingId);
    }

    [Fact]
    public void MalformedAndRepeatedInviteFieldsAreRejectedWithoutThrowing()
    {
        const string invite = "hanmate://join?host=192.168.1.2&port=1234&key=0123456789abcdef0123456789abcdef&cert=";
        Assert.False(LanInvite.TryParse(invite + new string('0', 64) + "&host=192.168.1.3", out _));
        Assert.False(LanInvite.TryParse(invite + new string('0', 64) + "&key=bad", out _));
        Assert.False(LanInvite.TryParse("hanmate://join?host=8.8.8.8&port=1234&key=" +
            new string('0', 32) + "&cert=" + new string('0', 64), out _));
    }

    [Fact]
    public async Task TwoDigitRoomDiscoveryFindsOnlyMatchingLocalRoom()
    {
        Assert.True(LanRoomDiscovery.IsValidCode("07"));
        Assert.False(LanRoomDiscovery.IsValidCode("7"));
        Assert.False(LanRoomDiscovery.IsValidCode("ab"));
        await using var room = await LanShareRoom.StartAsync("127.0.0.1", "07", "sender");
        var matches = await LanRoomDiscovery.FindAsync("07", targets: [System.Net.IPAddress.Loopback]);
        var match = Assert.Single(matches);
        Assert.Equal("sender", match.Name);
        Assert.Equal(room.Invite, match.Invite);
        Assert.Empty(await LanRoomDiscovery.FindAsync("08", targets: [System.Net.IPAddress.Loopback]));
    }

    [Fact]
    public async Task DiscoveryAdvertisesTheInterfaceThatCanReachTheReceiver()
    {
        await using var room = await LanShareRoom.StartAsync("127.0.0.2", "41", "sender");
        var match = Assert.Single(await LanRoomDiscovery.FindAsync("41", targets: [System.Net.IPAddress.Loopback]));
        Assert.Equal("127.0.0.1", match.Invite.Host);
        await using var receiver = await LanShareReceiver.ConnectAsync(match.Invite, "receiver");
        await UntilAsync(() => room.Peers.Count == 1);
    }

    [Fact]
    public async Task InvalidDiscoveryPacketDoesNotStopTheRoom()
    {
        await using var room = await LanShareRoom.StartAsync("127.0.0.1", "42", "sender");
        using var client = new System.Net.Sockets.UdpClient();
        var invalid = System.Text.Encoding.UTF8.GetBytes("{\"Type\":\"find\",\"Code\":\"42\",\"Nonce\":null}");
        await client.SendAsync(invalid, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, LanRoomDiscovery.Port));
        await Task.Delay(50);
        Assert.Single(await LanRoomDiscovery.FindAsync("42", targets: [System.Net.IPAddress.Loopback]));
    }

    private static async Task UntilAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ready()) await Task.Delay(20, timeout.Token);
    }
}
