using Quadra.Core.Game;

namespace Quadra.Core.Net;

/// <summary>
/// The server side of a network game: a port of <c>Net_server</c> in legacy/source/net_server.cc. It
/// answers joins, hands out player slots, and relays what each client does to all the others, checking
/// that a client only acts for its own players. It keeps its own copy of every board
/// (<see cref="State"/>) by applying what it relays, as every client does.
/// <para>
/// Like the original it is the one place players are admitted, and it lets them in while the game is on: a
/// connection that joins then is sent every board as it stands (<see cref="DownloadPacket"/> and
/// <see cref="StatPacket"/>), and the new player starts with an empty board.
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
    private readonly SurvivorRounds _rounds;
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
        _rounds = new SurvivorRounds(State, packet => Broadcast(packet));
    }

    public HostRules Rules { get; }

    /// <summary>When set, everything the host sends to its connections is also written here, with its frame.</summary>
    public Demos.DemoRecorder? Recorder { get; set; }

    public MultiplayerState State { get; }

    /// <summary>Frames left before the game starts: <see cref="WaitingForStart"/> while waiting, 0 once it has.</summary>
    public int DelayStart { get; private set; } = WaitingForStart;

    public bool Paused { get; private set; } = true;

    public bool Started => DelayStart == 0;

    /// <summary>Frames played since the countdown ended (time-limited games count against this).</summary>
    public int Frames { get; private set; }

    /// <summary>The end condition has been met and everyone has been told; play no longer counts.</summary>
    public bool Terminated { get; private set; }

    /// <summary>The team that won, or -1 when nobody scored.</summary>
    public int WinningTeam { get; private set; } = -1;

    /// <summary>Where a survivor game is in its round change (<c>Net_list::syncpoint</c>); <see cref="RoundState.Last"/> while a round runs.</summary>
    public RoundState SyncPoint => _rounds.SyncPoint;

    /// <summary>Rounds that have finished (survivor games).</summary>
    public int Rounds => _rounds.Rounds;

    /// <summary>Where the seed of a new survivor round comes from.</summary>
    public Func<uint> NewSeed
    {
        get => _rounds.NewSeed;
        set => _rounds.NewSeed = value;
    }

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
                Terminated = Terminated,
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
                DropPlayer(slot, DropReason.Auto, except: peer);
        }
    }

    /// <summary>Advances the game by one frame (10 ms): runs the start countdown.</summary>
    public void Tick()
    {
        lock (_lock)
        {
            if (DelayStart is > 0 and not WaitingForStart && --DelayStart == 0)
                Paused = false;
            if (Started && !Paused && !Terminated)
            {
                Frames++;
                CheckEnd(_rounds.Check());
            }
        }
    }

    /// <summary>
    /// Ends the game when its condition is met (<c>Net_list::check_end_game</c>): the leading team has the
    /// goal or the time is up. If the two best teams are level, play goes on until one is ahead.
    /// </summary>
    private void CheckEnd(bool roundEnded)
    {
        var end = Rules.GameEnd;
        if (!roundEnded || end == GameEnd.Never || (end == GameEnd.Frag && Rules.GameEndValue <= 0))
            return;

        var totals = GameGoals.TeamTotals(State, end);
        int leader = GameGoals.Leader(totals, out bool draw);
        bool reached = end == GameEnd.Time
            ? Frames >= Rules.GameEndValue * GameGoals.FramesPerMinute
            : leader >= 0 && totals[leader] >= GameGoals.Goal(end, Rules.GameEndValue);
        if (!reached || draw)
            return;

        Terminated = true;
        WinningTeam = leader;
        Broadcast(new EndGamePacket(true) { Id = (byte)PacketType.EndGame }, everyone: true);
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
                    HandleJoinRequest(peer, (WantJoinPacket)packet);
                    break;
                case PacketType.PlayerWantJoin:
                    HandlePlayerRequest(peer, (PlayerWantJoinPacket)packet);
                    break;
                case PacketType.ClientChat:
                    Broadcast(packet with { Id = (byte)PacketType.Chat }, everyone: true);
                    break;
                case PacketType.ClientPause:
                    HandlePauseRequest(peer);
                    break;
                case PacketType.ClientTestPing:
                    peer.Sink.Send(packet with { Id = (byte)PacketType.TestPing });
                    break;
                case PacketType.Bye:
                    Close(peer);
                    break;
                case PacketType.ClientDropPlayer:
                    if (PeerPlaysSlotOf(peer, packet) && packet is DropPlayerPacket drop)
                        DropPlayer(drop.Slot, drop.Reason, except: peer);
                    break;
                case PacketType.ClientStampBlock when Terminated:
                case PacketType.ClientDead when Terminated:
                case PacketType.ClientRespawn when Terminated:
                case PacketType.ClientLines when Terminated:
                    break;      // the game is over: whatever is still on its way no longer counts
                case PacketType.ClientStampBlock:
                case PacketType.ClientDead:
                case PacketType.ClientRespawn:
                case PacketType.ClientLines:
                case PacketType.ClientFirstFrag:
                case PacketType.ClientGone:
                case PacketType.ClientState:
                case PacketType.ClientRemoveBonus:
                    if (PeerPlaysSlotOf(peer, packet))
                        ApplyAndRelay(peer, packet);
                    break;
                    // Moves are only sent on to spectators, which this port does not have.
            }
        }
    }

    // Packets about a player must come from the connection that plays it.
    private bool PeerPlaysSlotOf(HostPeer peer, Packet packet) =>
        packet is PlayerPacketBase p && p.Slot < PacketDecoder.MaxPlayers && peer.Slots.Contains(p.Slot);

    private void ApplyAndRelay(HostPeer from, Packet packet)
    {
        var relayed = ClientPacketRelay.AsRelayed(packet);
        State.Apply(relayed);
        Broadcast(relayed, except: from);
    }

    // Sends to every joined connection (or every connection, with everyone) but the one it came from, which already did it itself.
    private void Broadcast(Packet packet, HostPeer? except = null, bool everyone = false)
    {
        Recorder?.Add((uint)Frames, packet);
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

    private void HandleJoinRequest(HostPeer peer, WantJoinPacket want)
    {
        // 1.1.0 clients are ignored, and nobody can come in once the game is over.
        if (!want.Current || peer.Joined)
            return;
        if (Terminated)
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

        // Whoever comes in while the game is on is told how every board stands (the original does this on a join).
        if (Started)
            foreach (var player in State.Players.Values)
            {
                var (board, stats) = State.Snapshot(player);
                Send(peer, board);
                Send(peer, stats);
            }
    }

    private void HandlePlayerRequest(HostPeer peer, PlayerWantJoinPacket want)
    {
        bool valid = want.Team < PacketDecoder.MaxTeams && want.Player <= 2 && want.Smooth is 0 or 1 && want.Shadow is 0 or 1
                     && want.Handicap is >= 0 and <= 4 && want.HRepeat is >= 0 and <= 3 && want.VRepeat is >= 0 and <= 3;
        if (!valid)
            return;

        var answer = new PlayerAcceptedPacket { Id = (byte)PacketType.PlayerAccepted, Uid = want.Uid };

        if (State.Players.Values.Any(p => p.Name == want.Name && p.PlayerHash.AsSpan().SequenceEqual(want.PlayerHash)))
            answer = answer with { Accepted = JoinAnswer.NameTaken };
        else if (Terminated)
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
            Broadcast(player, except: peer);
            answer = answer with { Pos = slot };
        }
        Send(peer, answer);
    }

    // The first connection to ask, or any when the game is waiting, may start it; afterwards a request pauses and resumes.
    private void HandlePauseRequest(HostPeer peer)
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
        Broadcast(new PausePacket(who) { Id = (byte)PacketType.Pause });
    }

    private void DropPlayer(byte slot, DropReason reason, HostPeer? except)
    {
        if (!State.Players.ContainsKey(slot))
            return;
        State.Remove(slot);
        foreach (var peer in _peers)
            peer.Slots.Remove(slot);
        Broadcast(new DropPlayerPacket(reason) { Id = (byte)PacketType.DropPlayer, Slot = slot }, except);
    }
}
