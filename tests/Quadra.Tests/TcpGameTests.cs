using System.Net;
using System.Net.Sockets;
using Quadra.Core.Net;

namespace Quadra.Tests;

public sealed class TcpGameTests : IAsyncLifetime
{
    private GameHost _host = null!;
    private TcpGameServer _server = null!;
    private readonly List<TcpGameLink> _links = [];

    public Task InitializeAsync()
    {
        _host = new GameHost(new HostRules { Name = "Over TCP", Seed = 321 });
        _server = new TcpGameServer(_host, IPAddress.Loopback, 0);
        _server.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var link in _links)
            await link.DisposeAsync();
        await _server.DisposeAsync();
    }

    private async Task<GameClient> ConnectAsync()
    {
        var link = await TcpGameLink.ConnectAsync("127.0.0.1", _server.Port);
        _links.Add(link);
        link.Client.RequestJoin();
        await Until(() => link.Client.State is not null);
        return link.Client;
    }

    // Packets arrive on the network threads; the game loop takes them in by pumping, as done here.
    private async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 1000; i++)
        {
            foreach (var link in _links)
                link.Client.Pump();
            if (condition())
                return;
            await Task.Delay(5);
        }
        throw new TimeoutException("Nothing happened.");
    }

    private async Task<byte> AddPlayerAsync(GameClient client, string name, byte team)
    {
        byte? slot = null;
        client.PlayerAnswered += a => slot = a.Pos;
        client.RequestPlayer(new PlayerWantJoinPacket { Name = name, Team = team, HRepeat = 2, VRepeat = 2, PlayerHash = [(byte)name[0], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] });
        await Until(() => slot is not null);
        return slot!.Value;
    }

    [Fact]
    public async Task Two_machines_join_and_see_each_others_moves_over_real_sockets()
    {
        var ann = await ConnectAsync();
        var bob = await ConnectAsync();
        Assert.Equal("Over TCP", ann.Rules!.Name);
        Assert.Equal(321, bob.Rules!.GameSeed);

        byte a = await AddPlayerAsync(ann, "Ann", 1);
        byte b = await AddPlayerAsync(bob, "Bob", 2);
        await Until(() => ann.State!.Players.ContainsKey(b) && bob.State!.Players.ContainsKey(a));

        ann.Stamp(a, 7, 28, 0, 12);
        bob.Stamp(b, 5, 28, 1, 20);
        await Until(() => bob.State!.Players[a].Sim.Stamps == 1 && ann.State!.Players[b].Sim.Stamps == 1);

        Assert.Equal(ann.State!.Players[a].Sim.Score, bob.State!.Players[a].Sim.Score);
        Assert.Equal(bob.State.Players[b].Sim.Score, ann.State.Players[b].Sim.Score);
        Assert.Equal(1, _host.State.Players[a].Sim.Stamps);
        Assert.Equal(1, _host.State.Players[b].Sim.Stamps);
    }

    [Fact]
    public async Task Chat_reaches_the_other_machine_and_a_test_ping_comes_back_only_to_its_sender()
    {
        var ann = await ConnectAsync();
        var bob = await ConnectAsync();
        var heard = new List<string>();
        var annPings = new List<uint>();
        var bobPings = new List<uint>();
        bob.Received += p =>
        {
            if (p is ChatPacket c)
                heard.Add(c.Text);
            if (p is TestPingPacket t)
                bobPings.Add(t.Frame);
        };
        ann.Received += p =>
        {
            if (p is TestPingPacket t)
                annPings.Add(t.Frame);
        };

        ann.Chat("hello over there");
        ann.TestPing(77);
        await Until(() => heard.Count == 1 && annPings.Count == 1);

        Assert.Equal(["hello over there"], heard);
        Assert.Equal([77u], annPings);
        Assert.Empty(bobPings);
    }

    [Fact]
    public async Task A_machine_that_goes_away_takes_its_players_with_it_and_the_slot_is_free_again()
    {
        var ann = await ConnectAsync();
        var bob = await ConnectAsync();
        byte a = await AddPlayerAsync(ann, "Ann", 1);
        byte b = await AddPlayerAsync(bob, "Bob", 2);
        await Until(() => bob.State!.Players.ContainsKey(a));

        await _links[0].DisposeAsync();
        _links.RemoveAt(0);

        await Until(() => !bob.State!.Players.ContainsKey(a));
        Assert.True(bob.State!.Players.ContainsKey(b));
        Assert.False(_host.State.Players.ContainsKey(a));
    }

    [Fact]
    public async Task A_connection_that_sends_garbage_is_dropped()
    {
        using var raw = new TcpClient();
        await raw.ConnectAsync("127.0.0.1", _server.Port);
        var stream = raw.GetStream();

        await stream.WriteAsync(new byte[] { 0, 0, 1, 2, 3 });   // a packet of size zero

        var read = await stream.ReadAsync(new byte[16]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, read);
        for (int i = 0; i < 200 && _host.PeerCount > 0; i++)
            await Task.Delay(5);
        Assert.Equal(0, _host.PeerCount);
    }

    [Fact]
    public async Task Packets_split_across_writes_and_many_at_once_are_both_understood()
    {
        using var raw = new TcpClient { NoDelay = true };
        await raw.ConnectAsync("127.0.0.1", _server.Port);
        var stream = raw.GetStream();
        var join = PacketFraming.Frame(PacketEncoder.Encode(new WantJoinPacket { Id = (byte)PacketType.WantJoin, Uid = 9, NetVersion = 20 }));
        var chat = PacketFraming.Frame(PacketEncoder.Encode(new ChatPacket(-1, -1, "x") { Id = (byte)PacketType.ClientChat }));

        // the join in two pieces, then the chat glued to the tail of another chat
        await stream.WriteAsync(join.AsMemory(0, 3));
        await Task.Delay(20);
        await stream.WriteAsync(join.AsMemory(3));
        await stream.WriteAsync(chat.Concat(chat).ToArray());

        var buffer = new byte[4096];
        int count = 0;
        for (int i = 0; i < 400 && PacketCount(buffer.AsSpan(0, count)) < 3; i++)
        {
            if (raw.Available > 0)
                count += await stream.ReadAsync(buffer.AsMemory(count));
            else
                await Task.Delay(5);
        }
        Assert.Equal(3, PacketCount(buffer.AsSpan(0, count)));   // the game packet, then both chats
    }

    private static int PacketCount(ReadOnlySpan<byte> data)
    {
        int n = 0;
        while (PacketFraming.TryTake(data) is { } taken)
        {
            n++;
            data = data[taken.Used..];
        }
        return n;
    }
}
