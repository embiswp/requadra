using Quadra.Core.Game;
using Quadra.Core.Net;
using Quadra.Core.Rules;

namespace Quadra.Core.Demos;

/// <summary>What the rules produced for one player of a replayed demo.</summary>
public sealed record PlayerReplay(byte Slot, string Name, int Score, int Lines, int Frags, int Deaths, int Stamps, int InvalidStamps,
    int LandingMismatches, int MovesAfterLanding, int DeathMismatches);

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

        var state = new MultiplayerState(gs) { PlayMoves = input == ReplayInput.Moves };
        var mismatches = new Dictionary<byte, int>();
        var deathMismatches = new Dictionary<byte, int>();
        var awaitingDeath = new HashSet<byte>();   // the sim says this player's new piece is blocked

        foreach (var (_, packet) in demo.Packets)
        {
            // What the recording says is checked against what the rules predict around each packet.
            switch (packet)
            {
                case PlayerPacket p:
                    mismatches[p.Pos] = 0;
                    deathMismatches[p.Pos] = 0;
                    break;
                case StampBlockPacket s when state.Players.TryGetValue(s.Slot, out var player):
                    if (input == ReplayInput.Moves && player.Sim.Landed != new Placement(s.X, s.Y, s.Rotate))
                        mismatches[s.Slot]++;
                    break;
                case RespawnPacket r when awaitingDeath.Remove(r.Slot):
                    deathMismatches[r.Slot]++;   // predicted a death that was never recorded
                    break;
                case DeadPacket d when state.Players.ContainsKey(d.Slot) && !awaitingDeath.Remove(d.Slot):
                    deathMismatches[d.Slot]++;   // a recorded death the sim did not predict
                    break;
            }

            state.Apply(packet);

            if (packet is StampBlockPacket stamped && state.Players.TryGetValue(stamped.Slot, out var after) && after.Sim.SpawnBlocked)
                awaitingDeath.Add(stamped.Slot);
        }

        return state.Players.Values
            .Select(p => new PlayerReplay(p.Slot, p.Name, p.Sim.Score, p.Sim.Lines, p.Frags, p.Sim.Deaths, p.Sim.Stamps,
                p.Sim.InvalidStamps, mismatches[p.Slot], p.Sim.MovesAfterLanding,
                deathMismatches[p.Slot] + (awaitingDeath.Contains(p.Slot) ? 1 : 0)))
            .ToList();
    }
}
