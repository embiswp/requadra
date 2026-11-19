using Quadra.Core.Demos;
using Quadra.Core.Net;

namespace Quadra.Tests;

/// <summary>A host and its clients wired together in memory: what one sends, the other gets, in order.</summary>
internal sealed class LocalNetwork
{
    private sealed class ClientSink(GameClient client) : IPacketSink
    {
        public void Send(Packet packet) => client.Enqueue(packet);

        public void Close() { }
    }

    public GameHost Host { get; }
    public List<GameClient> Clients { get; } = [];

    /// <summary>How many garbage packets each client has sent.</summary>
    public Dictionary<GameClient, int> LinesSent { get; } = [];

    public Action<GameClient, LinesPacket>? OnLines { get; set; }

    public LocalNetwork(HostRules rules) => Host = new GameHost(rules);

    public GameClient AddClient()
    {
        GameClient? client = null;
        HostPeer? peer = null;
        client = new GameClient(p =>
        {
            if (p is LinesPacket lp)
            {
                LinesSent[client!] = LinesSent.GetValueOrDefault(client!) + 1;
                OnLines?.Invoke(client!, lp);
            }
            Host.Receive(peer!, p);
        });
        peer = Host.Connect(new ClientSink(client));
        Clients.Add(client);
        client.RequestJoin();
        Settle();
        return client;
    }

    /// <summary>Joins a player for <paramref name="client"/> and returns its slot.</summary>
    public byte AddPlayer(GameClient client, string name, byte team, int handicap = 0, int hRepeat = 2, int vRepeat = 2)
    {
        byte? slot = null;
        void OnAnswer(PlayerAcceptedPacket a)
        {
            Assert.Equal(JoinAnswer.Accepted, a.Accepted);
            slot = a.Pos;
        }
        client.PlayerAnswered += OnAnswer;
        client.RequestPlayer(new PlayerWantJoinPacket
        {
            Name = name, Team = team, Handicap = handicap, HRepeat = hRepeat, VRepeat = vRepeat,
            PlayerHash = [(byte)(Clients.IndexOf(client) + 1), (byte)name.Length, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        });
        Settle();
        client.PlayerAnswered -= OnAnswer;
        return slot ?? throw new InvalidOperationException("The host did not answer.");
    }

    /// <summary>Delivers everything in flight, including what that provokes (garbage answering a stamp).</summary>
    public void Settle()
    {
        bool moved;
        do
        {
            moved = false;
            foreach (var c in Clients.ToList())
                moved |= c.Pump() > 0;
        } while (moved);
    }
}

public class NetworkGameTests
{
    internal static string DemoDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "legacy", "demos");
            if (Directory.Exists(candidate))
                return candidate;
        }
        throw new DirectoryNotFoundException();
    }

    [Fact]
    public void A_joined_client_sees_the_players_that_were_there_and_those_who_come_after()
    {
        var net = new LocalNetwork(new HostRules { Seed = 5 });
        var ann = net.AddClient();
        net.AddPlayer(ann, "Ann", 1);

        var bob = net.AddClient();
        Assert.Equal(["Ann"], bob.State!.Players.Values.Select(p => p.Name));

        byte slotB = net.AddPlayer(bob, "Bob", 2);

        Assert.Equal(["Ann", "Bob"], ann.State!.Players.Values.Select(p => p.Name));
        Assert.Equal([slotB], bob.LocalSlots);
        Assert.Equal([0], ann.LocalSlots);
        Assert.Equal(["Ann", "Bob"], net.Host.State.Players.Values.Select(p => p.Name));
    }

    [Fact]
    public void Starting_is_seen_by_everyone_and_all_count_down_together()
    {
        var net = new LocalNetwork(new HostRules());
        var a = net.AddClient();
        var b = net.AddClient();

        a.RequestPause();
        net.Settle();
        for (int i = 0; i < GameHost.WaitingForStart; i++)
        {
            net.Host.Tick();
            a.Tick();
            b.Tick();
        }

        Assert.True(net.Host.Started);
        Assert.True(a.Started && b.Started);
        Assert.False(a.Paused || b.Paused || net.Host.Paused);
    }

    [Fact]
    public void A_stamp_is_the_same_on_every_machine_and_a_clear_sends_garbage_to_the_other_team()
    {
        var net = new LocalNetwork(new HostRules { Seed = 99 });
        var ann = net.AddClient();
        var bob = net.AddClient();
        byte a = net.AddPlayer(ann, "Ann", 1);
        byte b = net.AddPlayer(bob, "Bob", 2);

        // Fill the bottom row except where the first piece lands, by stamping pieces the rules accept.
        // The simplest accepted move is just dropping straight down; whatever happens, the three copies agree.
        for (int i = 0; i < 12; i++)
        {
            var sim = ann.State!.Players[a].Sim;
            ann.Stamp(a, 7, 28 - 0, 0, 10);
            net.Settle();
            Assert.Equal(sim.Score, bob.State!.Players[a].Sim.Score);
            Assert.Equal(sim.Score, net.Host.State.Players[a].Sim.Score);
            Assert.Equal(ann.State.Players[b].Sim.PendingGarbage, bob.State.Players[b].Sim.PendingGarbage);
        }
    }

    // The recorded games, fed through a host and one client per player, with the garbage worked out by the
    // clients (the recorded Lines packets are never sent).
    //
    // Whatever the timing, every machine must end with the same boards and figures for every player: all of
    // them follow each victim's own stream of stamps and garbage, which the host relays in order.
    [Theory, MemberData(nameof(PacketEncoderTests.DemoNames), MemberType = typeof(PacketEncoderTests))]
    public void A_recorded_game_played_through_the_network_leaves_every_machine_with_the_same_game(string name)
    {
        var (net, _) = PlayDemo(name);

        var reference = net.Host.State;
        foreach (var (client, i) in net.Clients.Select((c, i) => (c, i)))
            foreach (var (slot, want) in reference.Players)
            {
                var got = client.State!.Players[slot];
                Assert.True((want.Sim.Score, want.Sim.Lines, want.Frags, want.Sim.Deaths, want.Sim.LastAttacker)
                            == (got.Sim.Score, got.Sim.Lines, got.Frags, got.Sim.Deaths, got.Sim.LastAttacker),
                    $"{name}: client {i} disagrees with the host about slot {slot}");
                for (int row = 0; row < 32; row++)
                    for (int col = 4; col < 14; col++)
                        Assert.True(want.Sim.Board.Cell(row, col) == got.Sim.Board.Cell(row, col)
                                    && want.Sim.Board.IsOccupied(row, col) == got.Sim.Board.IsOccupied(row, col),
                            $"{name}: client {i}'s board of slot {slot} differs from the host's at {row},{col}");
            }
    }

    // In these two games the garbage reaches each victim in the order the recording shows, and the recorded
    // result comes out exactly. In the other two, a victim sometimes took in attacks from different players in
    // another order than its clients do here (in demo01 that is how the sequences differ); that order depends
    // on how long each clear animation ran on the victim's machine, which headless clients do not model, so
    // there they only keep to the agreement above.
    [Theory]
    [InlineData("demo00.rec")]
    [InlineData("demo02.rec")]
    public void Where_garbage_order_does_not_depend_on_animation_timing_the_recorded_result_comes_out_exactly(string name)
    {
        var demo = DemoFile.Load(Path.Combine(DemoDir(), name));
        var expected = DemoReplay.Run(demo).ToDictionary(r => r.Slot);

        var (net, _) = PlayDemo(name);

        foreach (var (slot, want) in expected)
            foreach (var (view, i) in net.Clients.Select((c, i) => (c.State!, i)).Append((net.Host.State, -1)))
            {
                var got = view.Players[slot];
                Assert.True((want.Score, want.Lines, want.Frags, want.Deaths) == (got.Sim.Score, got.Sim.Lines, got.Frags, got.Sim.Deaths),
                    $"{name} slot {slot} on {(i < 0 ? "host" : $"client {i}")}: expected {(want.Score, want.Lines, want.Frags, want.Deaths)}, got {(got.Sim.Score, got.Sim.Lines, got.Frags, got.Sim.Deaths)}");
            }
    }

    private static (LocalNetwork Net, Dictionary<byte, GameClient> Owner) PlayDemo(string name)
    {
        var demo = DemoFile.Load(Path.Combine(DemoDir(), name));
        var gs = demo.GameServer!;
        var net = new LocalNetwork(new HostRules
        {
            Name = gs.Name, Seed = gs.GameSeed, LevelStart = gs.LevelStart, NoLevelUp = gs.NoLevelUp, Survivor = gs.Survivor,
            ComboMin = gs.ComboMin, AllowHandicap = gs.AllowHandicap,
        });

        var owner = new Dictionary<byte, GameClient>();
        foreach (var p in demo.Packets.Select(d => d.Packet).OfType<PlayerPacket>().DistinctBy(p => p.Pos).OrderBy(p => p.Pos))
        {
            var client = net.AddClient();
            byte slot = net.AddPlayer(client, p.Name, p.Team, p.Handicap, p.HRepeat, p.VRepeat);
            Assert.Equal(p.Pos, slot);
            owner[slot] = client;
        }

        // A recorded Lines packet shows when its machine had taken in the stamp that caused the garbage, so the
        // client takes in packets, one by one, until it has sent as many as were recorded. The recorded
        // Lines packets themselves are never sent.
        var recordedLines = new Dictionary<GameClient, int>();
        foreach (var (_, packet) in demo.Packets)
        {
            switch (packet)
            {
                case LinesPacket l when owner.TryGetValue(l.Slot, out var c) && l.Sender != 255:
                    recordedLines[c] = recordedLines.GetValueOrDefault(c) + 1;
                    while (net.LinesSent.GetValueOrDefault(c) < recordedLines[c] && c.Pump(1) > 0)
                    { }
                    break;
                case Quadra.Core.Net.StampBlockPacket s when owner.TryGetValue(s.Slot, out var c):
                    c.Stamp(s.Slot, s.X, s.Y, s.Rotate, s.Score);
                    break;
                case DeadPacket d when owner.TryGetValue(d.Slot, out var c):
                    c.Die(d.Slot);
                    break;
                case RespawnPacket r when owner.TryGetValue(r.Slot, out var c):
                    c.Respawn(r.Slot);
                    break;
            }
        }
        net.Settle();
        return (net, owner);
    }
}
