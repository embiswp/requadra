using Quadra.Core.Net;
using Quadra.Core.Rules;

namespace Quadra.Core.Game;

/// <summary>Where a player is in a survivor round (<c>Canvas::State</c>); the host moves everyone through these in step.</summary>
public enum RoundState : byte
{
    Playing,
    WaitForWinner,
    WaitForRestart,
    /// <summary>No round change is going on (also the host's sync point while a round runs).</summary>
    Last,
}

/// <summary>A player of a network game as every machine in it sees them.</summary>
public sealed class RemotePlayer(byte slot, string name, byte team, int handicap, PlayerSim sim, uint playerId = 0, byte[]? playerHash = null)
{
    /// <summary>The host-assigned id that stays with the player for the whole game.</summary>
    public uint PlayerId { get; } = playerId;

    /// <summary>Identifies who is behind a name; a name is taken only by the same hash.</summary>
    public byte[] PlayerHash { get; } = playerHash ?? new byte[16];

    public byte Slot { get; } = slot;
    public string Name { get; } = name;
    public byte Team { get; } = team;
    public int Handicap { get; } = handicap;
    public PlayerSim Sim { get; } = sim;
    public int Frags { get; internal set; }

    /// <summary>False from a death (or the winner's first frag) until the respawn.</summary>
    public bool Alive { get; internal set; } = true;

    public RoundState Round { get; internal set; }
}

/// <summary>
/// The replicated game: one <see cref="PlayerSim"/> per slot, kept up to date by the packets the host
/// relays. Every client and the host apply the same packets in the same order and so agree on every
/// board. This is what <see cref="Demos.DemoReplay"/> does with a recording; a live game does it with
/// what arrives from the network.
/// </summary>
public sealed class MultiplayerState(GameServerPacket rules)
{
    private readonly SortedDictionary<byte, RemotePlayer> _players = [];

    public GameServerPacket Rules { get; } = rules;

    public IReadOnlyDictionary<byte, RemotePlayer> Players => _players;

    /// <summary>Whether <see cref="MovesPacket"/>s drive the falling pieces. Stamps alone are enough for the figures.</summary>
    public bool PlayMoves { get; init; }

    /// <summary>Keep the passes of every clear, so a client can time what the attack of a clear does (<see cref="PlayerSim.LastClear"/>).</summary>
    public bool RecordClears { get; init; }

    private static void Download(RemotePlayer p, DownloadPacket d)
    {
        for (int row = 0; row < DownloadPacket.Rows; row++)
            for (int col = 0; col < DownloadPacket.Columns; col++)
            {
                byte cell = d.Cells[row * DownloadPacket.Columns + col];
                p.Sim.Board.Load(row, Board.FirstColumn + col, cell, cell != 0);
            }
        int Piece(byte b) => b == DownloadPacket.NoPiece ? -1 : b;
        p.Sim.Restore(d.Seed, [Piece(d.Bloc), Piece(d.Next), Piece(d.Next2), Piece(d.Next3)],
            d.Garbage.Select(g => (int)g.X), [.. d.Attacks.Select(a => (int)a)], d.LastAttacker);
        p.Alive = d.Idle < 2;
        p.Round = (RoundState)d.State;
    }

    /// <summary>The state of a player as a connection that joins late is told it: board, queue, garbage and who attacked.</summary>
    public (DownloadPacket Board, StatPacket Stats) Snapshot(RemotePlayer p)
    {
        var cells = new byte[DownloadPacket.Rows * DownloadPacket.Columns];
        for (int row = 0; row < DownloadPacket.Rows; row++)
            for (int col = 0; col < DownloadPacket.Columns; col++)
            {
                int column = Board.FirstColumn + col;
                byte cell = p.Sim.Board.Cell(row, column);
                // An occupied cell with no colour would arrive as an empty one; give it some.
                cells[row * DownloadPacket.Columns + col] = !p.Sim.Board.IsOccupied(row, column) ? (byte)0 : cell == 0 ? (byte)0x7F : (byte)(cell & 0x7F);
            }
        byte Piece(int i) => p.Alive && p.Sim.QueuedPiece(i) >= 0 ? (byte)p.Sim.QueuedPiece(i) : (byte)DownloadPacket.NoPiece;
        var download = new DownloadPacket
        {
            Id = (byte)PacketType.Download, Slot = p.Slot, Cells = cells, Seed = p.Sim.RandomSeed,
            Idle = p.Alive ? (byte)1 : (byte)2, State = (byte)p.Round,
            Bloc = Piece(0), Next = Piece(1), Next2 = Piece(2), Next3 = Piece(3),
            Garbage = [.. p.Sim.PendingGarbage.Select(x => new DownloadedGarbage((byte)x, 0, 0, 0, false))],
            Attacks = [.. p.Sim.Attacks.Select(a => (byte)a)], LastAttacker = (byte)p.Sim.LastAttacker,
        };
        var stats = new StatPacket(
        [
            new StatValue(StatValue.Death, p.Sim.Deaths), new StatValue(StatValue.Frag, p.Frags),
            new StatValue(StatValue.LinesTotal, p.Sim.Lines), new StatValue(StatValue.Score, p.Sim.Score),
        ]) { Id = (byte)PacketType.Stat, Slot = p.Slot };
        return (download, stats);
    }

    /// <summary>Takes a player out of the game (they dropped or their connection ended).</summary>
    public void Remove(byte slot) => _players.Remove(slot);

    /// <summary>Raised after a stamp has been applied, with what it did to the board.</summary>
    public event Action<RemotePlayer, StampResult>? Stamped;

    /// <summary>
    /// Applies a packet the host relayed. Packets that do not concern the boards are ignored.
    /// For a death, returns the slot credited with the frag, or <see cref="PlayerSim.NoAttacker"/>.
    /// </summary>
    public int Apply(Packet packet, byte[]? playerHash = null)
    {
        switch (packet)
        {
            case PlayerPacket p:
                var sim = new PlayerSim(Rules.GameSeed, Rules.LevelStart, p.VRepeat, levelUp: !Rules.NoLevelUp);
                sim.RecordClearAnimation = RecordClears;
                _players[p.Pos] = new RemotePlayer(p.Pos, p.Name, p.Team, p.Handicap, sim, p.PlayerId, playerHash);
                break;
            case MovesPacket m when PlayMoves && _players.TryGetValue(m.Slot, out var p):
                foreach (byte frame in m.Moves)
                    p.Sim.Move((MoveFlags)frame);
                break;
            case StampBlockPacket s when _players.TryGetValue(s.Slot, out var p):
                var result = p.Sim.Stamp(s.X, s.Y, s.Rotate, s.Score);   // not inside ?.Invoke: that skips its arguments when nobody listens
                Stamped?.Invoke(p, result);
                break;
            case RespawnPacket r when _players.TryGetValue(r.Slot, out var p):
                p.Sim.Restart();
                p.Alive = true;
                p.Round = RoundState.Playing;
                break;
            case FirstFragPacket f when _players.TryGetValue(f.Slot, out var p):
                p.Alive = false;       // a survivor of the round steps aside for the next one; not a death
                p.Round = RoundState.WaitForWinner;
                break;
            case StatePacket s when s.Id == (byte)PacketType.State && _players.TryGetValue(s.Slot, out var p) && s.State <= (byte)RoundState.Last:
                p.Round = (RoundState)s.State;
                break;
            case DownloadPacket d when _players.TryGetValue(d.Slot, out var p):
                Download(p, d);
                break;
            case StatPacket st when _players.TryGetValue(st.Slot, out var p):
                int score = p.Sim.Score, lines = p.Sim.Lines, deaths = p.Sim.Deaths;
                foreach (var (stat, value) in st.Stats)
                    switch (stat)
                    {
                        case StatValue.Score: score = value; break;
                        case StatValue.LinesTotal: lines = value; break;
                        case StatValue.Death: deaths = value; break;
                        case StatValue.Frag: p.Frags = value; break;
                    }
                p.Sim.RestoreCounters(score, lines, deaths);
                break;
            case ServerRandomPacket random:
                foreach (var player in _players.Values)
                    player.Sim.Reseed(unchecked((int)random.Seed));
                break;
            case LinesPacket l when l.Sender != PlayerSim.NoAttacker && _players.ContainsKey(l.Sender)
                                    && _players.TryGetValue(l.Slot, out var p):
                if (l.Attack.Type != AttackType.None)
                    p.Sim.ReceiveLines(l.Sender, l.Nb, l.Nc, l.Lx);
                break;
            case DeadPacket d when _players.TryGetValue(d.Slot, out var p):
                p.Alive = false;
                p.Round = RoundState.WaitForWinner;
                int fragger = p.Sim.Die();
                if (fragger != PlayerSim.NoAttacker && _players.TryGetValue((byte)fragger, out var killer))
                    killer.Frags++;
                return fragger;
        }
        return PlayerSim.NoAttacker;
    }
}
