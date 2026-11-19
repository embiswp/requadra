using Quadra.Core.Demos;
using Quadra.Core.Game;
using Quadra.Core.Net;

namespace Quadra.Tests;

public class GarbageTimingTests
{
    // The first attack in a recording: its stamps are fed to a started game without time passing. The victims'
    // machines hold the garbage back until the attacker's clear has played out (Canvas::give_line runs when the
    // animation ends), then send it.
    [Fact]
    public void Garbage_leaves_when_the_attackers_clear_animation_has_played_out()
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

        byte attacker = demo.Packets.Select(d => d.Packet).OfType<LinesPacket>().First(l => l.Sender != 255).Sender;
        foreach (var packet in demo.Packets.Select(d => d.Packet))
        {
            if (packet is LinesPacket l && l.Sender == attacker)
                break;
            switch (packet)
            {
                case StampBlockPacket s when owner.TryGetValue(s.Slot, out var c): c.Stamp(s.Slot, s.X, s.Y, s.Rotate, s.Score); break;
                case DeadPacket d when owner.TryGetValue(d.Slot, out var c): c.Die(d.Slot); break;
                case RespawnPacket r when owner.TryGetValue(r.Slot, out var c): c.Respawn(r.Slot); break;
            }
            net.Settle();
        }

        var passes = net.Clients[0].State!.Players[attacker].Sim.LastClear;
        Assert.NotEmpty(passes);
        int first = ClearAnimation.PassFrames(passes[0]);
        Assert.Equal(0, net.LinesSent.Values.Sum());      // the stamp is in, the animation is not over

        for (int i = 0; i < first - 1; i++)
        {
            foreach (var c in net.Clients)
                c.Tick();
            net.Settle();
        }
        Assert.Equal(0, net.LinesSent.Values.Sum());

        for (int i = 0; i < passes.Sum(ClearAnimation.PassFrames); i++)
        {
            foreach (var c in net.Clients)
                c.Tick();
            net.Settle();
        }
        Assert.True(net.LinesSent.Values.Sum() > 0);
    }
}
