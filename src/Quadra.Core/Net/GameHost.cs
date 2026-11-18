using Quadra.Core.Game;

namespace Quadra.Core.Net;

/// <summary>Where the host sends packets for one connection (a TCP socket, or an in-process pipe in tests).</summary>
public interface IPacketSink
{
    void Send(Packet packet);

    /// <summary>The host is done with this connection.</summary>
    void Close();
}

/// <summary>One connection to a <see cref="GameHost"/>.</summary>
public sealed class HostPeer
{
    internal HostPeer(IPacketSink sink) => Sink = sink;

    internal IPacketSink Sink { get; }

    /// <summary>The peer has asked to join and been answered; until then only chat gets through.</summary>
    public bool Joined { get; internal set; }

    /// <summary>The slots this connection plays; only it may send packets for them.</summary>
    public HashSet<byte> Slots { get; } = [];

    internal bool Closed { get; set; }
}

/// <summary>What the host announces and enforces; the fields of the game packet that do not change during a game.</summary>
public sealed record HostRules
{
    public string Name { get; init; } = "Quadra";
    public int NetVersion { get; init; } = PacketEncoder.CurrentVersion;
    public int Seed { get; init; }
    public byte LevelStart { get; init; } = 1;
    public bool NoLevelUp { get; init; }
    public bool Survivor { get; init; }
    public byte ComboMin { get; init; }
    public bool AllowHandicap { get; init; } = true;
    public int MaxPlayers { get; init; } = PacketDecoder.MaxPlayers;
    public GameEnd GameEnd { get; init; }
    public int GameEndValue { get; init; }
}

/// <summary>
/// The server side of a network game: a port of <c>Net_server</c> in legacy/source/net_server.cc. It
/// answers joins, hands out player slots, and relays what each client does to all the others, checking
/// that a client only acts for its own players. It keeps its own copy of every board
/// (<see cref="State"/>) by applying what it relays, as every client does.
/// <para>
/// Like the original it is the one place players are admitted, but a player joins only before the game
/// starts: the original sends a late joiner every board as it stands, which this port does not yet.
/// </para>
/// All calls are serialised by one lock, so connections can call in from their own threads.
/// </summary>
public sealed class GameHost
{
    /// <summary>The game is waiting for someone to start it (<c>delay_start</c> of 500 in the original).</summary>
    public const int WaitingForStart = 500;

    /// <summary>The <c>SyncPoint</c> that says no round synchronisation is going on (<c>Canvas::LAST</c>).</summary>
    public const int SyncPointNone = 3;

    private readonly object _lock = new();
    private readonly List<HostPeer> _peers = [];
    private uint _nextPlayerId = 1;

    public GameHost(HostRules rules)
    {
        Rules = rules;
        State = new MultiplayerState(new GameServerPacket
        {
            Id = (byte)PacketType.GameServer,
            Version = (byte)rules.NetVersion,
            Name = rules.Name,
            GameSeed = rules.Seed,
            LevelStart = rules.LevelStart,
            NoLevelUp = rules.NoLevelUp,
            Survivor = rules.Survivor,
            ComboMin = rules.ComboMin,
            AllowHandicap = rules.AllowHandicap,
            GameEnd = rules.GameEnd,
            GameEndValue = rules.GameEndValue,
        });
    }

    public HostRules Rules { get; }

    public MultiplayerState State { get; }

    /// <summary>Frames left before the game starts: <see cref="WaitingForStart"/> while waiting, 0 once it has.</summary>
    public int DelayStart { get; private set; } = WaitingForStart;

    public bool Paused { get; private set; } = true;

    public bool Started => DelayStart == 0;

    public int PeerCount
    {
        get
        {
            lock (_lock)
                return _peers.Count;
        }
    }

    /// <summary>What the game list shows about this game right now (<c>Game::addgameinfo</c>).</summary>
    public Qserv.GameAnnouncement Announcement(int port, IEnumerable<string>? addresses = null)
    {
        lock (_lock)
        {
            return new Qserv.GameAnnouncement
            {
                Name = Rules.Name,
                Version = Rules.NetVersion,
                Addresses = [.. addresses ?? []],
                Port = port,
                Started = Started,
                Survivor = Rules.Survivor,
                RulesType = Rules.Survivor ? 1 : 0,
                LevelStart = Rules.LevelStart,
                LevelUp = !Rules.NoLevelUp,
                MinCombo = Rules.ComboMin,
                AllowHandicap = Rules.AllowHandicap,
                EndType = Rules.GameEnd,
                EndValue = Rules.GameEndValue,
                Players = [.. State.Players.Values.Select(p => new Qserv.AnnouncedPlayer(p.Name, p.Team, 0, p.Handicap)
                {
                    Frags = p.Frags, Deaths = p.Sim.Deaths, Lines = p.Sim.Lines, Score = p.Sim.Score,
                })],
            };
        }
    }

    public HostPeer Connect(IPacketSink sink)
    {
        lock (_lock)
        {
            var peer = new HostPeer(sink);
            _peers.Add(peer);
            return peer;
        }
    }

    /// <summary>A connection went away: its players leave the game.</summary>
    public void Disconnect(HostPeer peer)
    {
        lock (_lock)
        {
            if (!_peers.Remove(peer))
                return;
            foreach (var slot in peer.Slots.ToList())
                Drop(slot, DropReason.Auto, except: peer);
        }
    }

    /// <summary>Advances the game by one frame (10 ms): runs the start countdown.</summary>
    public void Tick()
    {
        lock (_lock)
        {
            if (DelayStart is > 0 and not WaitingForStart && --DelayStart == 0)
                Paused = false;
        }
    }

    public void Receive(HostPeer peer, Packet packet)
    {
        lock (_lock)
        {
            if (peer.Closed || !_peers.Contains(peer))
                return;

            // Until it has joined a connection may only join or chat.
            if (!peer.Joined && packet is not (WantJoinPacket or ChatPacket))
                return;

            switch ((PacketType)packet.Id)
            {
                case PacketType.WantJoin:
                    WantJoin(peer, (WantJoinPacket)packet);
                    break;
                case PacketType.PlayerWantJoin:
                    PlayerWantJoin(peer, (PlayerWantJoinPacket)packet);
                    break;
                case PacketType.ClientChat:
                    Dispatch(packet with { Id = (byte)PacketType.Chat }, except: null, everyone: true);
                    break;
                case PacketType.ClientPause:
                    ClientPause(peer);
                    break;
                case PacketType.ClientTestPing:
                    peer.Sink.Send(packet with { Id = (byte)PacketType.TestPing });
                    break;
                case PacketType.Bye:
                    Close(peer);
                    break;
                case PacketType.ClientDropPlayer:
                    if (Owns(peer, packet) && packet is DropPlayerPacket drop)
                        Drop(drop.Slot, drop.Reason, except: peer);
                    break;
                case PacketType.ClientStampBlock:
                case PacketType.ClientDead:
                case PacketType.ClientRespawn:
                case PacketType.ClientLines:
                case PacketType.ClientFirstFrag:
                case PacketType.ClientGone:
                case PacketType.ClientState:
                case PacketType.ClientRemoveBonus:
                    if (Owns(peer, packet))
                        Relay(peer, packet);
                    break;
                    // Moves are only sent on to spectators, which this port does not have.
            }
        }
    }

    // Packets about a player must come from the connection that plays it.
    private bool Owns(HostPeer peer, Packet packet) =>
        packet is PlayerPacketBase p && p.Slot < PacketDecoder.MaxPlayers && peer.Slots.Contains(p.Slot);

    private static PacketType RelayedAs(PacketType client) => client switch
    {
        PacketType.ClientStampBlock => PacketType.StampBlock,
        PacketType.ClientDead => PacketType.Dead,
        PacketType.ClientRespawn => PacketType.Respawn,
        PacketType.ClientLines => PacketType.Lines,
        PacketType.ClientFirstFrag => PacketType.FirstFrag,
        PacketType.ClientGone => PacketType.Gone,
        PacketType.ClientState => PacketType.State,
        PacketType.ClientRemoveBonus => PacketType.RemoveBonus,
        _ => client,
    };

    private void Relay(HostPeer from, Packet packet)
    {
        var relayed = packet with { Id = (byte)RelayedAs((PacketType)packet.Id) };
        State.Apply(relayed);
        Dispatch(relayed, except: from);
    }

    // Sends to every joined connection but the one it came from (which already did it itself).
    private void Dispatch(Packet packet, HostPeer? except, bool everyone = false)
    {
        foreach (var peer in _peers.ToList())
            if (peer != except && (peer.Joined || everyone))
                Send(peer, packet);
    }

    private void Send(HostPeer peer, Packet packet)
    {
        if (!peer.Closed)
            peer.Sink.Send(packet);
    }

    private void Close(HostPeer peer)
    {
        if (peer.Closed)
            return;
        peer.Closed = true;
        peer.Sink.Close();
    }

    private void WantJoin(HostPeer peer, WantJoinPacket want)
    {
        // 1.1.0 clients are ignored, and nobody can come in once the game has begun.
        if (!want.Current || peer.Joined)
            return;
        if (Started)
        {
            Close(peer);
            return;
        }

        peer.Joined = true;
        Send(peer, new GameServerPacket
        {
            Id = (byte)PacketType.GameServer,
            Uid = want.Uid,
            Version = (byte)Rules.NetVersion,
            Name = Rules.Name,
            Accepted = true,
            GameSeed = Rules.Seed,
            Paused = Paused,
            NoLevelUp = Rules.NoLevelUp,
            LevelStart = Rules.LevelStart,
            Survivor = Rules.Survivor,
            ComboMin = Rules.ComboMin,
            DelayStart = (ushort)DelayStart,
            GameEnd = Rules.GameEnd,
            GameEndValue = Rules.GameEndValue,
            Players = [.. State.Players.Values.Select(p => new NetPlayer(p.Slot, p.Team, p.Name, p.PlayerId, p.Handicap))],
            SyncPoint = SyncPointNone,
            AllowHandicap = Rules.AllowHandicap,
        });
    }

    private void PlayerWantJoin(HostPeer peer, PlayerWantJoinPacket want)
    {
        bool valid = want.Team < PacketDecoder.MaxTeams && want.Player <= 2 && want.Smooth is 0 or 1 && want.Shadow is 0 or 1
                     && want.Handicap is >= 0 and <= 4 && want.HRepeat is >= 0 and <= 3 && want.VRepeat is >= 0 and <= 3;
        if (!valid)
            return;

        var answer = new PlayerAcceptedPacket { Id = (byte)PacketType.PlayerAccepted, Uid = want.Uid };

        if (State.Players.Values.Any(p => p.Name == want.Name && p.PlayerHash.AsSpan().SequenceEqual(want.PlayerHash)))
            answer = answer with { Accepted = JoinAnswer.NameTaken };
        else if (Started)
            answer = answer with { Accepted = JoinAnswer.GameFinished };
        else if (State.Players.Count >= Math.Min(Rules.MaxPlayers, PacketDecoder.MaxPlayers))
            answer = answer with { Accepted = JoinAnswer.Full };

        if (answer.Accepted == JoinAnswer.Accepted)
        {
            byte slot = Enumerable.Range(0, PacketDecoder.MaxPlayers).Select(i => (byte)i).First(i => !State.Players.ContainsKey(i));
            var player = new PlayerPacket
            {
                Id = (byte)PacketType.Player,
                Team = want.Team,
                Name = want.Name,
                Player = want.Player,
                HRepeat = want.HRepeat,
                VRepeat = want.VRepeat,
                Smooth = want.Smooth,
                Shadow = want.Shadow,
                Pos = slot,
                Handicap = want.Handicap,
                PlayerId = _nextPlayerId++,
            };
            State.Apply(player with { }, want.PlayerHash);
            peer.Slots.Add(slot);
            Dispatch(player, except: peer);
            answer = answer with { Pos = slot };
        }
        Send(peer, answer);
    }

    // The first connection to ask, or any when the game is waiting, may start it; afterwards a request pauses and resumes.
    private void ClientPause(HostPeer peer)
    {
        if (DelayStart is > 0 and not WaitingForStart)
            return;   // never interrupt the countdown

        // A pause names the player who asked for it; resuming and starting are the server's doing.
        sbyte who = -1;
        if (Started && !Paused)
            foreach (var slot in peer.Slots)
            {
                who = (sbyte)slot;
                break;
            }

        if (DelayStart == WaitingForStart)
        {
            DelayStart = WaitingForStart - 1;
        }
        else
        {
            Paused = !Paused;
        }
        Dispatch(new PausePacket(who) { Id = (byte)PacketType.Pause }, except: null);
    }

    private void Drop(byte slot, DropReason reason, HostPeer? except)
    {
        if (!State.Players.ContainsKey(slot))
            return;
        State.Remove(slot);
        foreach (var peer in _peers)
            peer.Slots.Remove(slot);
        Dispatch(new DropPlayerPacket(reason) { Id = (byte)PacketType.DropPlayer, Slot = slot }, except);
    }
}
