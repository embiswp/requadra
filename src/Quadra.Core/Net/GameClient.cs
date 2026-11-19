using System.Collections.Concurrent;
using Quadra.Core.Game;
using Quadra.Core.Rules;

namespace Quadra.Core.Net;

/// <summary>
/// The client side of a network game: the join handshake, the replicated game
/// (<see cref="MultiplayerState"/>) and the packets for the players this machine plays. A port of the
/// client halves of legacy/source/net_server.cc (<c>Net_client</c>) and net_list.cc.
/// <para>
/// Packets can arrive on any thread (<see cref="Enqueue"/>) but are applied on the caller's own thread
/// by <see cref="Pump"/>, so the boards are only ever touched by the game loop. What a local player
/// does is applied to <see cref="State"/> at once and sent to the host, which relays it; so is the
/// garbage this machine's players receive, which every client works out for its own players
/// (<c>Net_list::send</c>).
/// </para>
/// </summary>
public sealed class GameClient
{
    private readonly Action<Packet> _send;
    private readonly ConcurrentQueue<Packet> _incoming = new();
    private readonly Dictionary<uint, PlayerWantJoinPacket> _pending = [];
    private readonly HashSet<byte> _local = [];
    private uint _nextUid = 1;

    public GameClient(Action<Packet> send) => _send = send;

    /// <summary>The rules and players, known once the host has answered the join.</summary>
    public MultiplayerState? State { get; private set; }

    public GameServerPacket? Rules => State?.Rules;

    public IReadOnlyCollection<byte> LocalSlots => _local;

    public int DelayStart { get; private set; } = GameHost.WaitingForStart;
    public bool Paused { get; private set; } = true;
    public bool Started => DelayStart == 0;

    /// <summary>The host has ended the game: its end condition was met.</summary>
    public bool Terminated { get; private set; }

    private readonly Queue<RoundState> _syncPoints = new();

    /// <summary>The round change the player acts on (survivor games); moved on by <see cref="NextSyncPoint"/>.</summary>
    public RoundState SyncPoint { get; private set; } = RoundState.Last;

    /// <summary>
    /// Takes the next sync point the host sent, if any. The game loop asks every few frames, as the original does,
    /// so that a point that the host moves past quickly is still seen.
    /// </summary>
    public void NextSyncPoint()
    {
        if (_syncPoints.TryDequeue(out var point))
            SyncPoint = point;
    }

    /// <summary>Raised for every packet after it has been applied.</summary>
    public event Action<Packet>? Received;

    /// <summary>Raised when the host answers a request to add a player.</summary>
    public event Action<PlayerAcceptedPacket>? PlayerAnswered;

    /// <summary>Called from the connection when a packet arrives.</summary>
    public void Enqueue(Packet packet) => _incoming.Enqueue(packet);

    public bool HasPending => !_incoming.IsEmpty;

    /// <summary>Applies the packets that have arrived, at most <paramref name="max"/> of them; returns how many.</summary>
    public int Pump(int max = int.MaxValue)
    {
        int n = 0;
        while (n < max && _incoming.TryDequeue(out var packet))
        {
            Apply(packet);
            Received?.Invoke(packet);
            n++;
        }
        return n;
    }

    /// <summary>Advances the start countdown by one frame; keep it in step with the host's.</summary>
    public void Tick()
    {
        if (DelayStart is > 0 and not GameHost.WaitingForStart && --DelayStart == 0)
            Paused = false;
    }

    // --- joining

    public void RequestJoin(byte language = 0, byte os = 0) =>
        _send(new WantJoinPacket
        {
            Id = (byte)PacketType.WantJoin, Uid = _nextUid++, NetVersion = PacketEncoder.CurrentVersion, Language = language, Os = os,
        });

    /// <summary>Asks to add a player; the answer arrives as <see cref="PlayerAnswered"/>.</summary>
    public void RequestPlayer(PlayerWantJoinPacket want)
    {
        want = want with { Id = (byte)PacketType.PlayerWantJoin, Uid = _nextUid++ };
        _pending[want.Uid] = want;
        _send(want);
    }

    // --- what the host sends

    private void Apply(Packet packet)
    {
        switch (packet)
        {
            case GameServerPacket gs:
                State = new MultiplayerState(gs);
                State.Stamped += SendAttacks;
                foreach (var p in gs.Players)
                    State.Apply(new PlayerPacket
                    {
                        Id = (byte)PacketType.Player, Pos = p.Slot, Team = p.Team, Name = p.Name, Handicap = p.Handicap,
                        PlayerId = p.PlayerId, HRepeat = 2, VRepeat = 2,
                    });
                DelayStart = gs.DelayStart;
                Paused = gs.Paused;
                break;

            case PlayerAcceptedPacket accepted:
                if (_pending.Remove(accepted.Uid, out var want) && accepted.Accepted == JoinAnswer.Accepted && State is not null)
                {
                    State.Apply(new PlayerPacket
                    {
                        Id = (byte)PacketType.Player, Pos = accepted.Pos, Team = want.Team, Name = want.Name, Player = want.Player,
                        Handicap = want.Handicap, HRepeat = want.HRepeat, VRepeat = want.VRepeat, Smooth = want.Smooth, Shadow = want.Shadow,
                    }, want.PlayerHash);
                    _local.Add(accepted.Pos);
                }
                PlayerAnswered?.Invoke(accepted);
                break;

            case PausePacket:
                if (Paused && DelayStart == GameHost.WaitingForStart)
                    DelayStart = GameHost.WaitingForStart - 1;   // the countdown begins; it stays paused until it ends
                else if (Started)
                    Paused = !Paused;
                break;

            case StatePacket { Id: (byte)PacketType.ServerState } s when s.State <= (byte)RoundState.Last:
                _syncPoints.Enqueue((RoundState)s.State);
                break;

            case EndGamePacket:
                Terminated = true;
                break;

            case DropPlayerPacket drop:
                State?.Remove(drop.Slot);
                _local.Remove(drop.Slot);
                break;

            default:
                State?.Apply(packet);
                break;
        }
    }

    // After a stamp is applied, the lines it sends to this machine's own players (Net_list::send). The other
    // clients work out the same for theirs, so each board gets its garbage once, from its own machine.
    private void SendAttacks(RemotePlayer attacker, StampResult result)
    {
        if (State is null)
            return;
        foreach (var clear in result.Clears)
            foreach (var attack in AttackRules.For(clear, State.Rules.ComboMin))
                foreach (byte slot in _local.OrderBy(s => s))
                {
                    if (!State.Players.TryGetValue(slot, out var victim) || victim.Team == attacker.Team)
                        continue;
                    var (count, combo) = AttackRules.ForReceiver(attack, attacker.Handicap, victim.Handicap);
                    if (count == 0)
                        continue;
                    Do(new LinesPacket
                    {
                        Id = (byte)PacketType.ClientLines, Slot = slot, Nb = (byte)count, Nc = (byte)combo, Lx = (byte)result.LastColumn,
                        Sender = attacker.Slot, Attack = new Attack(AttackType.Lines, 0), HolePos = new ushort[count],
                    });
                }
    }

    // --- what a local player does

    /// <summary>Applies the packet to the local copy of the game and sends it to the host.</summary>
    private void Do(Packet clientPacket)
    {
        State?.Apply(clientPacket with { Id = (byte)Relayed((PacketType)clientPacket.Id) });
        _send(clientPacket);
    }

    private static PacketType Relayed(PacketType client) => client switch
    {
        PacketType.ClientStampBlock => PacketType.StampBlock,
        PacketType.ClientDead => PacketType.Dead,
        PacketType.ClientRespawn => PacketType.Respawn,
        PacketType.ClientLines => PacketType.Lines,
        PacketType.ClientFirstFrag => PacketType.FirstFrag,
        PacketType.ClientState => PacketType.State,
        _ => client,
    };

    /// <summary>A piece of local player <paramref name="slot"/> has landed.</summary>
    public void Stamp(byte slot, int x, int y, int rotation, int dropScore, int frame = 0, bool blockRotated = false, int timeHeld = 0) =>
        Do(new StampBlockPacket
        {
            Id = (byte)PacketType.ClientStampBlock, Slot = slot, X = (byte)x, Y = (byte)y, Rotate = (byte)rotation, Score = (byte)dropScore,
            Date = (ushort)frame, BlockRotated = blockRotated ? (byte)1 : (byte)0, TimeHeld = (ushort)timeHeld,
        });

    public void Die(byte slot, bool thenGone = false) =>
        Do(new DeadPacket(thenGone) { Id = (byte)PacketType.ClientDead, Slot = slot });

    public void Respawn(byte slot) =>
        Do(new RespawnPacket { Id = (byte)PacketType.ClientRespawn, Slot = slot });

    /// <summary>The local player won the round and steps aside for the next one (survivor games).</summary>
    public void FirstFrag(byte slot) =>
        Do(new FirstFragPacket { Id = (byte)PacketType.ClientFirstFrag, Slot = slot });

    /// <summary>Tells the host where the local player is in the round change.</summary>
    public void SendState(byte slot, RoundState state) =>
        Do(new StatePacket((byte)state) { Id = (byte)PacketType.ClientState, Slot = slot });

    public void Chat(string text, int team = -1, int toTeam = -1) =>
        _send(new ChatPacket((sbyte)team, (sbyte)toTeam, text) { Id = (byte)PacketType.ClientChat });

    /// <summary>Asks the host to send <paramref name="frame"/> straight back (a <see cref="TestPingPacket"/> arrives), to time the trip.</summary>
    public void TestPing(uint frame) =>
        _send(new TestPingPacket(frame) { Id = (byte)PacketType.ClientTestPing });

    /// <summary>Asks the host to start the game, or to pause or resume it.</summary>
    public void RequestPause() =>
        _send(new RawPacket([]) { Id = (byte)PacketType.ClientPause });

    public void Leave() => _send(new RawPacket([]) { Id = (byte)PacketType.Bye });
}
