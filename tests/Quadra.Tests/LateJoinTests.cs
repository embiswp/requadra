using Quadra.Core.Demos;
using Quadra.Core.Game;
using Quadra.Core.Net;

namespace Quadra.Tests;

public class LateJoinTests
{
    [Fact]
    public void A_download_survives_the_wire()
    {
        var cells = new byte[DownloadPacket.Rows * DownloadPacket.Columns];
        cells[5] = 0x35;
        cells[319] = 0x7F;
        var packet = new DownloadPacket
        {
            Id = (byte)PacketType.Download, Slot = 3, Cells = cells, Seed = -12345, Idle = 2, State = 1,
            Bloc = 4, Next = 0, Next2 = 6, Next3 = 2,
            Garbage = [new DownloadedGarbage(7, 3, 0, 0x155, true), new DownloadedGarbage(9, 1, 5, 2, false)],
            Attacks = [0, 4, 0, 0, 0, 0, 0, 200], LastAttacker = 7,
        };

        var back = Assert.IsType<DownloadPacket>(PacketDecoder.Decode(PacketEncoder.Encode(packet)));

        Assert.Equal(cells, back.Cells);
        Assert.Equal((packet.Slot, packet.Seed, packet.Idle, packet.State, packet.Bloc, packet.Next, packet.Next2, packet.Next3, packet.LastAttacker),
                     (back.Slot, back.Seed, back.Idle, back.State, back.Bloc, back.Next, back.Next2, back.Next3, back.LastAttacker));
        Assert.Equal(packet.Garbage, back.Garbage);
        Assert.Equal(packet.Attacks, back.Attacks);
    }

    [Fact]
    public void A_stat_packet_survives_the_wire()
    {
        var packet = new StatPacket([new StatValue(StatValue.Score, 123456), new StatValue(StatValue.Frag, 3)])
            { Id = (byte)PacketType.Stat, Slot = 2 };
        var back = Assert.IsType<StatPacket>(PacketDecoder.Decode(PacketEncoder.Encode(packet)));
        Assert.Equal(packet.Stats, back.Stats);
        Assert.Equal(2, back.Slot);
    }

    [Fact]
    public void Someone_who_joins_a_game_in_progress_sees_every_board_as_it_stands()
    {
        var demo = DemoFile.Load(Path.Combine(NetworkGameTests.DemoDir(), "demo00.rec"));
        var gs = demo.GameServer!;
        var net = new LocalNetwork(new HostRules
        {
            Name = gs.Name, Seed = gs.GameSeed, LevelStart = gs.LevelStart, NoLevelUp = gs.NoLevelUp,
            ComboMin = gs.ComboMin, AllowHandicap = gs.AllowHandicap,
        });
        var owner = new Dictionary<byte, GameClient>();
        foreach (var p in demo.Packets.Select(d => d.Packet).OfType<PlayerPacket>().DistinctBy(p => p.Pos).OrderBy(p => p.Pos))
        {
            var client = net.AddClient();
            owner[net.AddPlayer(client, p.Name, p.Team, p.Handicap, p.HRepeat, p.VRepeat)] = client;
        }
        owner.Values.First().RequestPause();
        net.Settle();
        for (int i = 0; i < GameHost.WaitingForStart; i++)
        {
            net.Host.Tick();
            foreach (var c in net.Clients)
                c.Tick();
        }
        net.Settle();

        // Play the first part of the game, with the garbage going out, then someone comes in.
        int played = 0;
        foreach (var packet in demo.Packets.Select(d => d.Packet))
        {
            switch (packet)
            {
                case StampBlockPacket s when owner.TryGetValue(s.Slot, out var c): c.Stamp(s.Slot, s.X, s.Y, s.Rotate, s.Score); played++; break;
                case DeadPacket d when owner.TryGetValue(d.Slot, out var c): c.Die(d.Slot); break;
                case RespawnPacket r when owner.TryGetValue(r.Slot, out var c): c.Respawn(r.Slot); break;
            }
            net.Settle();
            if (played >= 60)
                break;
        }
        for (int i = 0; i < 300; i++)
        {
            foreach (var c in net.Clients)
                c.Tick();
            net.Settle();
        }

        var late = net.AddClient();
        net.Settle();

        Assert.True(late.Started);
        foreach (var (slot, want) in net.Host.State.Players)
        {
            var got = late.State!.Players[slot];
            Assert.Equal((want.Sim.Score, want.Sim.Lines, want.Sim.Deaths, want.Frags, want.Alive), (got.Sim.Score, got.Sim.Lines, got.Sim.Deaths, got.Frags, got.Alive));
            for (int row = 0; row < 32; row++)
                for (int col = 4; col < 14; col++)
                    Assert.True(want.Sim.Board.IsOccupied(row, col) == got.Sim.Board.IsOccupied(row, col)
                                && (!want.Sim.Board.IsOccupied(row, col) || want.Sim.Board.Cell(row, col) == got.Sim.Board.Cell(row, col)),
                        $"slot {slot} differs at {row},{col}");
            Assert.Equal(want.Sim.CurrentPiece, got.Sim.CurrentPiece);
            Assert.Equal(want.Sim.Upcoming, got.Sim.Upcoming);
            Assert.Equal(want.Sim.PendingGarbage, got.Sim.PendingGarbage);
        }
    }
}
