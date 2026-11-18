using Quadra.Core.Net;
using Quadra.Core.Rules;

namespace Quadra.Core.Game;

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
                _players[p.Pos] = new RemotePlayer(p.Pos, p.Name, p.Team, p.Handicap,
                    new PlayerSim(Rules.GameSeed, Rules.LevelStart, p.VRepeat), p.PlayerId, playerHash);
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
                break;
            case LinesPacket l when l.Sender != PlayerSim.NoAttacker && _players.ContainsKey(l.Sender)
                                    && _players.TryGetValue(l.Slot, out var p):
                if (l.Attack.Type != AttackType.None)
                    p.Sim.ReceiveLines(l.Sender, l.Nb, l.Nc, l.Lx);
                break;
            case DeadPacket d when _players.TryGetValue(d.Slot, out var p):
                int fragger = p.Sim.Die();
                if (fragger != PlayerSim.NoAttacker && _players.TryGetValue((byte)fragger, out var killer))
                    killer.Frags++;
                return fragger;
        }
        return PlayerSim.NoAttacker;
    }
}
