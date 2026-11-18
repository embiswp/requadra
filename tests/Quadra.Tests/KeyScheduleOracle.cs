using Quadra.Core.Demos;
using Quadra.Core.Input;
using Quadra.Core.Net;
using Quadra.Core.Rules;

namespace Quadra.Tests;

/// <summary>Counters from an oracle run.</summary>
public sealed class OracleStats
{
    public int Pieces;
    public int Frames;
    public int DirectionAttempts;
    /// <summary>Attempts that the key repeat produced by itself (the key was already held).</summary>
    public int AutoRepeats;

    /// <summary>Pieces whose recorded frames the key layer could not reproduce, with the first difference.</summary>
    public List<string> Unexplained { get; } = [];
}

/// <summary>
/// The demos record what each frame did (a <c>Moves</c> byte), not which keys were down. This oracle
/// works backwards: it invents a key schedule that should produce each recorded byte, drives the real
/// <see cref="PieceController"/> with it, and checks that every byte, and the landing, comes out the
/// same. Taps are always possible, so the oracle prefers holding a direction key whenever the
/// controller's own key-repeat predicts the next recorded attempt; how often that works is counted in
/// <see cref="OracleStats.AutoRepeats"/> and is what tests the repeat model.
/// </summary>
internal static class KeyScheduleOracle
{
    public const int Left = 1, Right = 2, RotateLeft = 3, Down = 4, RotateRight = 5, RotateTwice = 6, Drop = 7;

    public static KeyBindings Bindings => new(Left, Right, RotateLeft, Down, RotateRight, RotateTwice, Drop);

    /// <summary>
    /// Plays all frames of one piece. Returns null if every recorded byte and the landing were
    /// reproduced, otherwise a description of the first difference.
    /// </summary>
    public static string? PlayPiece(PlayerSim sim, KeyStates keys, byte[] bytes, OracleStats stats, string label)
    {
        var held = new bool[2];           // [0] left, [1] right
        bool heldDown = false;
        stats.Pieces++;

        for (int i = 0; i < bytes.Length; i++)
        {
            var want = (MoveFlags)bytes[i];
            bool last = i == bytes.Length - 1;

            if (want.HasFlag(MoveFlags.RotateLeft)) { keys.Down(RotateLeft); keys.Up(RotateLeft); }
            if (want.HasFlag(MoveFlags.RotateRight)) { keys.Down(RotateRight); keys.Up(RotateRight); }
            if (want.HasFlag(MoveFlags.RotateTwice)) { keys.Down(RotateTwice); keys.Up(RotateTwice); }
            if (want.HasFlag(MoveFlags.Drop))
                keys.Down(Drop);
            if (want.HasFlag(MoveFlags.Down))
            {
                if (!heldDown)
                {
                    keys.Down(Down);
                    heldDown = true;
                }
                if (last || !((MoveFlags)bytes[i + 1]).HasFlag(MoveFlags.Down))
                {
                    keys.Up(Down);   // released before its last frame, which therefore sees Released
                    heldDown = false;
                }
            }

            var direction = want & (MoveFlags.Left | MoveFlags.Right);
            FrameResult result;
            if (direction == MoveFlags.None)
            {
                result = sim.Frame();
            }
            else
            {
                int d = direction == MoveFlags.Left ? 0 : 1;
                int key = d == 0 ? Left : Right;
                stats.DirectionAttempts++;
                if (held[d])
                    stats.AutoRepeats++;
                else
                {
                    keys.Down(key);
                    held[d] = true;
                }

                // Try the frame with the key still down. If the controller's repeat would then fire
                // exactly at the next recorded attempt, keep holding; otherwise this attempt must
                // be the last of the hold, so redo the frame with the key released first.
                var savedKeys = keys.Clone();
                var savedFrame = sim.SaveFrame();
                result = sim.Frame();
                int hold = d == 0 ? sim.Input!.HoldLeft : sim.Input!.HoldRight;
                int next = NextAttempt(bytes, i, direction);
                if (next < 0 || next != i + hold)
                {
                    sim.RestoreFrame(savedFrame);
                    keys.CopyFrom(savedKeys);
                    keys.Up(key);
                    held[d] = false;
                    result = sim.Frame();
                }
            }

            stats.Frames++;
            if (result.Moves != want)
                return $"{label}: frame {i}: recorded {want}, controller produced {result.Moves}; bytes {Window(bytes, i)}";
            if ((result.Landed is not null) != last)
                return $"{label}: frame {i} of {bytes.Length}: landed={result.Landed is not null}";
        }
        return null;
    }

    private static string Window(byte[] bytes, int at) =>
        string.Join(',', bytes.Skip(Math.Max(0, at - 12)).Take(18).Select((b, k) => (Math.Max(0, at - 12) + k == at ? "[" : "") + b + (Math.Max(0, at - 12) + k == at ? "]" : "")));

    private static int NextAttempt(byte[] bytes, int from, MoveFlags direction)
    {
        for (int j = from + 1; j < bytes.Length; j++)
            if (((MoveFlags)bytes[j] & direction) != 0)
                return j;
        return -1;
    }

    /// <summary>
    /// Replays a demo with the pieces driven through the key layer, and returns per-player figures.
    /// </summary>
    public static (IReadOnlyList<PlayerReplay> Players, OracleStats Stats) Replay(DemoFile demo)
    {
        var gs = demo.GameServer!;
        var stats = new OracleStats();

        // Pass 1: the recorded bytes of every piece, split at the stamp packets.
        var pieces = new Dictionary<byte, Queue<byte[]>>();
        var current = new Dictionary<byte, List<byte>>();
        foreach (var (_, packet) in demo.Packets)
        {
            switch (packet)
            {
                case PlayerPacket p:
                    pieces[p.Pos] = new Queue<byte[]>();
                    current[p.Pos] = [];
                    break;
                case MovesPacket m:
                    current[m.Slot].AddRange(m.Moves);
                    break;
                case StampBlockPacket s:
                    pieces[s.Slot].Enqueue(current[s.Slot].ToArray());
                    current[s.Slot].Clear();
                    break;
                case RespawnPacket r:
                    // A round can end while a piece is still falling: its moves are never stamped and
                    // the client throws the piece away on respawn.
                    current[r.Slot].Clear();
                    break;
                case DeadPacket d:
                    current[d.Slot].Clear();
                    break;
            }
        }

        // Pass 2: play them.
        var names = new Dictionary<byte, string>();
        var sims = new SortedDictionary<byte, PlayerSim>();
        var keys = new Dictionary<byte, KeyStates>();
        var frags = new Dictionary<byte, int>();

        foreach (var (_, packet) in demo.Packets)
        {
            switch (packet)
            {
                case PlayerPacket p:
                    names[p.Pos] = p.Name;
                    var sim = new PlayerSim(gs.GameSeed, gs.LevelStart, p.VRepeat);
                    keys[p.Pos] = new KeyStates();
                    sim.AttachInput(keys[p.Pos], Bindings, p.HRepeat, retryBlockedMoves: false); // 1.1.0 clients
                    sims[p.Pos] = sim;
                    frags[p.Pos] = 0;
                    break;
                case StampBlockPacket s:
                    var player = sims[s.Slot];
                    string? failure = PlayPiece(player, keys[s.Slot], pieces[s.Slot].Dequeue(), stats, $"{names[s.Slot]} stamp {player.Stamps + 1}");
                    if (failure is null && player.Landed != new Placement(s.X, s.Y, s.Rotate))
                        failure = $"{names[s.Slot]} stamp {player.Stamps + 1}: landed at {player.Landed}, stamp says {s.X},{s.Y},{s.Rotate}";
                    if (failure is not null)
                    {
                        stats.Unexplained.Add(failure);
                        keys[s.Slot].ClearAll();   // the key schedule of this piece is unknown; start the next one clean
                    }
                    player.Stamp(s.X, s.Y, s.Rotate, s.Score);
                    break;
                case RespawnPacket r:
                    sims[r.Slot].Restart();
                    break;
                case LinesPacket l when l.Sender != PlayerSim.NoAttacker && sims.ContainsKey(l.Sender):
                    sims[l.Slot].ReceiveLines(l.Sender, l.Nb, l.Nc, l.Lx);
                    break;
                case DeadPacket d:
                    int fragger = sims[d.Slot].Die();
                    if (fragger != PlayerSim.NoAttacker && frags.ContainsKey((byte)fragger))
                        frags[(byte)fragger]++;
                    break;
            }
        }

        var players = sims
            .Select(kv => new PlayerReplay(kv.Key, names[kv.Key], kv.Value.Score, kv.Value.Lines, frags[kv.Key],
                kv.Value.Deaths, kv.Value.Stamps, kv.Value.InvalidStamps, 0, kv.Value.MovesAfterLanding))
            .ToList();
        return (players, stats);
    }
}
