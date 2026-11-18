using Quadra.Core.Net;
using Quadra.Core.Rules;

namespace Quadra.Core.Demos;

/// <summary>What the rules produced for one player of a replayed demo.</summary>
public sealed record PlayerReplay(byte Slot, string Name, int Score, int Lines, int Stamps, int InvalidStamps, int GarbagePacketsIgnored)
{
    /// <summary>
    /// True when nothing the replay left out can have affected this player, so score and lines
    /// are expected to equal the demo's summary.
    /// </summary>
    public bool ExpectedExact => GarbagePacketsIgnored == 0;
}

/// <summary>
/// Replays a demo through the C# rules (<see cref="PlayerSim"/>). Not covered yet: garbage rows
/// sent by opponents (<see cref="LinesPacket"/>), net versions from 23, and level-up games.
/// </summary>
public static class DemoReplay
{
    public static IReadOnlyList<PlayerReplay> Run(DemoFile demo)
    {
        var gs = demo.GameServer ?? throw new InvalidOperationException("Demo has no game packet.");
        if (gs.Version >= 23)
            throw new NotSupportedException($"Net version {gs.Version} rules are not ported yet.");
        if (!gs.NoLevelUp)
            throw new NotSupportedException("Games with level-up are not ported yet.");

        var sims = new SortedDictionary<byte, (string Name, PlayerSim Sim, int Garbage)>();

        foreach (var (_, packet) in demo.Packets)
        {
            switch (packet)
            {
                case PlayerPacket p:
                    sims[p.Pos] = (p.Name, new PlayerSim(gs.GameSeed, gs.LevelStart), 0);
                    break;
                case StampBlockPacket s when sims.TryGetValue(s.Slot, out var entry):
                    entry.Sim.Stamp(s.X, s.Y, s.Rotate, s.Score);
                    break;
                case RespawnPacket r when sims.TryGetValue(r.Slot, out var entry):
                    entry.Sim.Restart();
                    break;
                case LinesPacket l when l.Sender != 255 && l.Nb > 0 && sims.TryGetValue(l.Slot, out var entry):
                    sims[l.Slot] = entry with { Garbage = entry.Garbage + 1 };
                    break;
            }
        }

        return sims
            .Select(kv => new PlayerReplay(kv.Key, kv.Value.Name, kv.Value.Sim.Score, kv.Value.Sim.Lines,
                kv.Value.Sim.Stamps, kv.Value.Sim.InvalidStamps, kv.Value.Garbage))
            .ToList();
    }
}
