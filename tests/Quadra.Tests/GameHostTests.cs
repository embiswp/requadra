using Quadra.Core.Net;

namespace Quadra.Tests;

internal sealed class TestSink : IPacketSink
{
    public List<Packet> Received { get; } = [];
    public bool Closed { get; private set; }

    public void Send(Packet packet) => Received.Add(packet);

    public void Close() => Closed = true;

    public IEnumerable<T> Of<T>() where T : Packet => Received.OfType<T>();
}

public class GameHostTests
{
    private static readonly HostRules Rules = new() { Name = "Test", Seed = 1234, Survivor = false };

    private static (HostPeer, TestSink) Join(GameHost host, uint uid = 1)
    {
        var sink = new TestSink();
        var peer = host.Connect(sink);
        host.Receive(peer, new WantJoinPacket { Id = (byte)PacketType.WantJoin, Uid = uid, NetVersion = 20 });
        return (peer, sink);
    }

    private static PlayerWantJoinPacket Want(string name, byte team = 0, uint uid = 7, byte hash = 0) => new()
    {
        Id = (byte)PacketType.PlayerWantJoin, Uid = uid, Name = name, Team = team, PlayerHash = [hash, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        HRepeat = 2, VRepeat = 2,
    };

    private static byte AddPlayer(GameHost host, HostPeer peer, TestSink sink, string name, byte team = 0, byte hash = 0)
    {
        host.Receive(peer, Want(name, team, hash: hash));
        var answer = sink.Of<PlayerAcceptedPacket>().Last();
        Assert.Equal(JoinAnswer.Accepted, answer.Accepted);
        return answer.Pos;
    }

    private static StampBlockPacket Stamp(byte slot, byte x = 7, byte y = 28) => new()
    {
        Id = (byte)PacketType.ClientStampBlock, Slot = slot, X = x, Y = y, Rotate = 0, Score = 10,
    };

    [Fact]
    public void A_joining_connection_gets_the_game_rules_answering_its_own_uid()
    {
        var host = new GameHost(Rules);

        var (_, sink) = Join(host, uid: 42);

        var game = Assert.Single(sink.Of<GameServerPacket>());
        Assert.Equal(42u, game.Uid);
        Assert.Equal(("Test", 1234, 20), (game.Name, game.GameSeed, (int)game.Version));
        Assert.Equal(GameHost.WaitingForStart, game.DelayStart);
        Assert.True(game.Paused);
        Assert.Equal(GameHost.SyncPointNone, game.SyncPoint);
    }

    [Fact]
    public void A_1_1_0_client_is_ignored()
    {
        var host = new GameHost(Rules);
        var sink = new TestSink();
        var peer = host.Connect(sink);

        host.Receive(peer, new WantJoinPacket { Id = (byte)PacketType.WantJoin, Current = false });

        Assert.Empty(sink.Received);
        Assert.False(peer.Joined);
    }

    [Fact]
    public void Nothing_but_chat_gets_through_before_joining()
    {
        var host = new GameHost(Rules);
        var sink = new TestSink();
        var other = new TestSink();
        var peer = host.Connect(sink);
        var (_, _) = Join(host);
        host.Connect(other);

        host.Receive(peer, Want("Early"));
        host.Receive(peer, new ChatPacket(-1, -1, "hello") { Id = (byte)PacketType.ClientChat });

        Assert.Empty(sink.Of<PlayerAcceptedPacket>());
        Assert.Equal("hello", Assert.Single(sink.Of<ChatPacket>()).Text);
        Assert.Equal("hello", Assert.Single(other.Of<ChatPacket>()).Text);
    }

    [Fact]
    public void Players_get_the_lowest_free_slot_and_the_others_are_told_about_them()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);
        var (b, sb) = Join(host);

        Assert.Equal(0, AddPlayer(host, a, sa, "Ann", team: 1));
        Assert.Equal(1, AddPlayer(host, b, sb, "Bob", team: 2));

        Assert.DoesNotContain(sa.Of<PlayerPacket>(), p => p.Name == "Ann");   // the asker is told by the answer
        var told = Assert.Single(sa.Of<PlayerPacket>());
        Assert.Equal(("Bob", (byte)2, (byte)1), (told.Name, told.Team, told.Pos));
        Assert.DoesNotContain(sb.Of<PlayerPacket>(), p => p.Name == "Bob");
        Assert.Equal(2, host.State.Players.Count);
        Assert.NotEqual(host.State.Players[0].PlayerId, host.State.Players[1].PlayerId);
    }

    [Fact]
    public void A_later_connection_learns_the_players_already_in_from_the_game_packet()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);
        AddPlayer(host, a, sa, "Ann", team: 3);

        var (_, sc) = Join(host);

        var players = Assert.Single(sc.Of<GameServerPacket>()).Players;
        Assert.Equal(new NetPlayer(0, 3, "Ann", host.State.Players[0].PlayerId, 0), Assert.Single(players));
    }

    [Fact]
    public void A_name_is_taken_by_whoever_has_it_first_unless_the_same_hash_comes_back()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);
        var (b, sb) = Join(host);
        AddPlayer(host, a, sa, "Ann", hash: 1);

        host.Receive(b, Want("Ann", hash: 1));
        Assert.Equal(JoinAnswer.NameTaken, sb.Of<PlayerAcceptedPacket>().Last().Accepted);

        host.Receive(b, Want("Ann", hash: 2));
        Assert.Equal(JoinAnswer.Accepted, sb.Of<PlayerAcceptedPacket>().Last().Accepted);
    }

    [Fact]
    public void The_game_fills_up()
    {
        var host = new GameHost(Rules with { MaxPlayers = 2 });
        var (a, sa) = Join(host);
        AddPlayer(host, a, sa, "A", hash: 1);
        AddPlayer(host, a, sa, "B", hash: 2);

        host.Receive(a, Want("C", hash: 3));

        Assert.Equal(JoinAnswer.Full, sa.Of<PlayerAcceptedPacket>().Last().Accepted);
    }

    [Fact]
    public void A_request_with_out_of_range_values_is_ignored()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);

        host.Receive(a, Want("Bad") with { Handicap = 9 });
        host.Receive(a, Want("Bad") with { Team = 8 });

        Assert.Empty(sa.Of<PlayerAcceptedPacket>());
    }

    [Fact]
    public void What_a_client_does_goes_to_everyone_else_under_the_server_packet_type_and_into_the_hosts_copy()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);
        var (b, sb) = Join(host);
        byte slotA = AddPlayer(host, a, sa, "Ann", team: 1);
        AddPlayer(host, b, sb, "Bob", team: 2);

        host.Receive(a, Stamp(slotA));

        var stamp = Assert.Single(sb.Of<StampBlockPacket>());
        Assert.Equal((byte)PacketType.StampBlock, stamp.Id);
        Assert.Empty(sa.Of<StampBlockPacket>());
        Assert.Equal(1, host.State.Players[slotA].Sim.Stamps);
    }

    [Fact]
    public void A_connection_cannot_act_for_a_player_that_is_not_its_own()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);
        var (b, sb) = Join(host);
        AddPlayer(host, a, sa, "Ann");
        byte slotB = AddPlayer(host, b, sb, "Bob", team: 1, hash: 1);

        host.Receive(a, Stamp(slotB));
        host.Receive(a, Stamp(5));

        Assert.Empty(sb.Of<StampBlockPacket>());
        Assert.Equal(0, host.State.Players[slotB].Sim.Stamps);
    }

    [Fact]
    public void Garbage_for_a_player_comes_from_that_players_connection_and_reaches_the_others()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);
        var (b, sb) = Join(host);
        byte slotA = AddPlayer(host, a, sa, "Ann", team: 1);
        byte slotB = AddPlayer(host, b, sb, "Bob", team: 2, hash: 1);

        host.Receive(b, new LinesPacket
        {
            Id = (byte)PacketType.ClientLines, Slot = slotB, Nb = 2, Nc = 1, Lx = 7, Sender = slotA,
            Attack = new Attack(AttackType.Lines, 0), HolePos = [1, 2],
        });

        Assert.Equal((byte)PacketType.Lines, Assert.Single(sa.Of<LinesPacket>()).Id);
        Assert.Equal(2, host.State.Players[slotB].Sim.PendingGarbage.Count);
    }

    [Fact]
    public void Chat_reaches_everyone_including_the_sender()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);
        var (_, sb) = Join(host);

        host.Receive(a, new ChatPacket(0, -1, "hi") { Id = (byte)PacketType.ClientChat });

        Assert.Single(sa.Of<ChatPacket>());
        Assert.Single(sb.Of<ChatPacket>());
    }

    [Fact]
    public void A_test_ping_is_sent_straight_back()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);
        var (_, sb) = Join(host);

        host.Receive(a, new TestPingPacket(99) { Id = (byte)PacketType.ClientTestPing });

        Assert.Equal(99u, Assert.Single(sa.Of<TestPingPacket>()).Frame);
        Assert.Empty(sb.Of<TestPingPacket>());
    }

    [Fact]
    public void Starting_runs_a_countdown_and_then_the_game_is_on_and_closed_to_newcomers()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);
        var (_, sb) = Join(host);
        AddPlayer(host, a, sa, "Ann");

        host.Receive(a, new RawPacket([]) { Id = (byte)PacketType.ClientPause });

        Assert.Equal(-1, Assert.Single(sa.Of<PausePacket>()).Player);   // starting is the server's doing
        Assert.Single(sb.Of<PausePacket>());
        Assert.False(host.Started);

        for (int i = 0; i < GameHost.WaitingForStart - 2; i++)
            host.Tick();
        Assert.False(host.Started);
        host.Tick();
        Assert.True(host.Started);
        Assert.False(host.Paused);

        var late = new TestSink();
        var peer = host.Connect(late);
        host.Receive(peer, new WantJoinPacket { Id = (byte)PacketType.WantJoin, Uid = 3, NetVersion = 20 });
        Assert.True(late.Closed);
        Assert.Empty(late.Of<GameServerPacket>());
    }

    [Fact]
    public void A_start_request_during_the_countdown_is_ignored_and_after_it_pauses_and_resumes()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);
        AddPlayer(host, a, sa, "Ann");
        var pause = new RawPacket([]) { Id = (byte)PacketType.ClientPause };

        host.Receive(a, pause);
        host.Receive(a, pause);
        Assert.Single(sa.Of<PausePacket>());

        for (int i = 0; i < GameHost.WaitingForStart; i++)
            host.Tick();
        host.Receive(a, pause);
        Assert.True(host.Paused);
        Assert.Equal(0, sa.Of<PausePacket>().Last().Player);   // named: it was a player who paused

        host.Receive(a, pause);
        Assert.False(host.Paused);
        Assert.Equal(-1, sa.Of<PausePacket>().Last().Player);   // resuming is the server's
    }

    [Fact]
    public void A_connection_that_goes_away_takes_its_players_with_it()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);
        var (b, sb) = Join(host);
        byte slotA = AddPlayer(host, a, sa, "Ann");
        AddPlayer(host, b, sb, "Bob", hash: 1);

        host.Disconnect(a);

        var dropped = Assert.Single(sb.Of<DropPlayerPacket>());
        Assert.Equal(slotA, dropped.Slot);
        Assert.False(host.State.Players.ContainsKey(slotA));
        Assert.Equal(1, host.PeerCount);

        // and the slot is free for the next player
        var (c, sc) = Join(host);
        Assert.Equal(slotA, AddPlayer(host, c, sc, "Cy", hash: 2));
    }

    [Fact]
    public void Bye_closes_the_connection()
    {
        var host = new GameHost(Rules);
        var (a, sa) = Join(host);

        host.Receive(a, new RawPacket([]) { Id = (byte)PacketType.Bye });

        Assert.True(sa.Closed);
    }
}
