using Quadra.Core.Net;
using Quadra.Core.Rules;

namespace Quadra.Core.Demos;

/// <summary>What the rules produced for one player of a replayed demo.</summary>
public sealed record PlayerReplay(byte Slot, string Name, int Score, int Lines, int Frags, int Deaths, int Stamps, int InvalidStamps,
    int LandingMismatches, int MovesAfterLanding);

/// <summary>What drives the falling pieces during a replay.</summary>
public enum ReplayInput
{
    /// <summary>Use the final placement in each stamp packet only.</summary>
    Placements,

    /// <summary>
    /// Also play the <c>Moves</c> packets frame by frame and check that each piece lands where
    /// its stamp packet says (counted in <see cref="PlayerReplay.LandingMismatches"/>).
    /// </summary>
    Moves,
}

/// <summary>
/// Replays a demo through the C# rules (<see cref="PlayerSim"/>). The figures can be compared with
/// the summary stored in the demo. Supported: net versions below 23, survivor games, no level-up.
/// </summary>
public static class DemoReplay
{
    public static IReadOnlyList<PlayerReplay> Run(DemoFile demo, ReplayInput input = ReplayInput.Placements)
    {
        var gs = demo.GameServer ?? throw new InvalidOperationException("Demo has no game packet.");
        if (gs.Version >= 23)
            throw new NotSupportedException($"Net version {gs.Version} rules are not ported yet.");
        if (!gs.NoLevelUp)
            throw new NotSupportedException("Games with level-up are not ported yet.");
        if (!gs.Survivor)
            throw new NotSupportedException("Games without survivor rules are not ported yet.");

        var names = new Dictionary<byte, string>();
        var sims = new SortedDictionary<byte, PlayerSim>();
        var frags = new Dictionary<byte, int>();
        var mismatches = new Dictionary<byte, int>();

        foreach (var (_, packet) in demo.Packets)
        {
            switch (packet)
            {
                case PlayerPacket p:
                    names[p.Pos] = p.Name;
                    sims[p.Pos] = new PlayerSim(gs.GameSeed, gs.LevelStart, p.VRepeat);
                    frags[p.Pos] = 0;
                    mismatches[p.Pos] = 0;
                    break;
                case MovesPacket m when input == ReplayInput.Moves && sims.TryGetValue(m.Slot, out var sim):
                    foreach (byte frame in m.Moves)
                        sim.Move((MoveFlags)frame);
                    break;
                case StampBlockPacket s when sims.TryGetValue(s.Slot, out var sim):
                    if (input == ReplayInput.Moves && sim.Landed != new Placement(s.X, s.Y, s.Rotate))
                        mismatches[s.Slot]++;
                    sim.Stamp(s.X, s.Y, s.Rotate, s.Score);
                    break;
                case RespawnPacket r when sims.TryGetValue(r.Slot, out var sim):
                    sim.Restart();
                    break;
                case LinesPacket l when l.Sender != PlayerSim.NoAttacker && sims.ContainsKey(l.Sender)
                                        && sims.TryGetValue(l.Slot, out var sim):
                    if (l.Attack.Type != AttackType.None)
                        sim.ReceiveLines(l.Sender, l.Nb, l.Nc, l.Lx);
                    break;
                case DeadPacket d when sims.TryGetValue(d.Slot, out var sim):
                    int fragger = sim.Die();
                    if (fragger != PlayerSim.NoAttacker && frags.ContainsKey((byte)fragger))
                        frags[(byte)fragger]++;
                    break;
            }
        }

        return sims
            .Select(kv => new PlayerReplay(kv.Key, names[kv.Key], kv.Value.Score, kv.Value.Lines, frags[kv.Key],
                kv.Value.Deaths, kv.Value.Stamps, kv.Value.InvalidStamps, mismatches[kv.Key], kv.Value.MovesAfterLanding))
            .ToList();
    }
}
