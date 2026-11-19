namespace Quadra.Core.Net;

/// <summary>
/// Decodes packets as stored in demos. Ports Packet_*::read from source/packets.cc.
/// Packets not yet ported come back as <see cref="RawPacket"/>.
/// </summary>
public static class PacketDecoder
{
    public const int MaxPlayers = 8;
    public const int MaxTeams = 8;

    public static Packet Decode(byte[] payload)
    {
        if (payload.Length == 0)
            throw new InvalidDataException("Empty packet.");

        var r = new NetReader(payload);
        byte id = r.ReadByte();

        Packet p = (PacketType)id switch
        {
            PacketType.Chat or PacketType.ClientChat => ReadChat(r),
            PacketType.GameServer => ReadGameServer(r),
            PacketType.Player => ReadPlayer(r),
            PacketType.Pause => new PausePacket(unchecked((sbyte)r.ReadByte())),
            PacketType.StampBlock or PacketType.ClientStampBlock => ReadStampBlock(r),
            PacketType.Dead or PacketType.ClientDead => ReadPlayerBase(r, s => new DeadPacket(r.ReadBool()) { Slot = s }),
            PacketType.Respawn or PacketType.ClientRespawn => ReadPlayerBase(r, s => new RespawnPacket { Slot = s }),
            PacketType.FirstFrag or PacketType.ClientFirstFrag => ReadPlayerBase(r, s => new FirstFragPacket { Slot = s }),
            PacketType.Gone or PacketType.ClientGone => ReadPlayerBase(r, s => new GonePacket(!r.ReadBool()) { Slot = s }),
            PacketType.Lines or PacketType.ClientLines => ReadLines(r),
            PacketType.Moves or PacketType.ClientMoves => ReadPlayerBase(r, s => ReadMoves(r, s)),
            PacketType.State or PacketType.ClientState or PacketType.ServerState => ReadPlayerBase(r, s => new StatePacket(r.ReadByte()) { Slot = s }),
            PacketType.Stat => ReadPlayerBase(r, s => ReadStat(r, s)),
            PacketType.Download => ReadPlayerBase(r, s => ReadDownload(r, s)),
            PacketType.WantJoin => ReadWantJoin(r),
            PacketType.PlayerWantJoin => ReadPlayerWantJoin(r),
            PacketType.PlayerAccepted => new PlayerAcceptedPacket { Uid = r.ReadDword(), Pos = r.ReadByte(), Accepted = (JoinAnswer)r.ReadByte() },
            PacketType.DropPlayer or PacketType.ClientDropPlayer or PacketType.ServerDropPlayer =>
                ReadPlayerBase(r, s => new DropPlayerPacket((DropReason)r.ReadByte()) { Slot = s }),
            PacketType.ClientTestPing or PacketType.TestPing or PacketType.ServerTestPing => new TestPingPacket(r.ReadDword()),
            PacketType.EndGame => new EndGamePacket(r.ReadBool()),
            PacketType.ServerRandom => new ServerRandomPacket(r.ReadDword()),
            _ => new RawPacket(payload[1..]),
        };

        return p with
        {
            Id = id,
            Truncated = r.Overrun,
            Unread = p is RawPacket ? 0 : r.Unread,
        };
    }

    private static ChatPacket ReadChat(NetReader r)
    {
        sbyte team = unchecked((sbyte)r.ReadByte());
        string text = r.ReadString(256, out _);
        sbyte toTeam = unchecked((sbyte)r.ReadByte());
        return new ChatPacket(team, toTeam, text);
    }

    private static Attack ReadAttack(NetReader r)
    {
        var type = (AttackType)r.ReadByte();
        return new Attack(type, r.ReadInt());
    }

    private static GameServerPacket ReadGameServer(NetReader r)
    {
        uint uid = r.ReadDword();
        byte version = r.ReadByte();
        string name = r.ReadString(32, out _);
        bool accepted = r.ReadBool();
        int seed = r.ReadInt();
        bool paused = r.ReadBool();
        int numPlayers = r.ReadByte();
        bool noLevelUp = r.ReadBool();
        byte levelStart = r.ReadByte();
        bool survivor = r.ReadBool();
        byte comboMin = r.ReadByte();
        ushort delayStart = r.ReadWord();
        var gameEnd = (GameEnd)r.ReadByte();
        int gameEndValue = r.ReadInt();

        var slots = new List<(byte Slot, byte Team, string Name)>();
        for (int i = 0; i < numPlayers; i++)
        {
            byte slot = r.ReadByte();
            byte team = r.ReadByte();
            slots.Add((slot, team, r.ReadString(40, out _)));
        }

        bool wantsMoves = r.ReadBool();
        byte syncPoint = r.ReadByte();
        byte potatoTeam = r.ReadByte();

        // Handicaps are stored after the rest to stay compatible with net_version <= 20.
        var handicaps = new int[numPlayers];
        for (int i = 0; i < numPlayers; i++)
            handicaps[i] = r.ReadInt();

        bool allowHandicap = !r.ReadBool();
        bool hotPotato = r.ReadBool();

        Attack normal = default, clean = default, potatoNormal = default, potatoClean = default;
        if (version >= 22)
        {
            normal = ReadAttack(r);
            clean = ReadAttack(r);
            potatoNormal = ReadAttack(r);
            potatoClean = ReadAttack(r);
        }

        bool single = r.ReadBool();
        bool terminated = r.ReadBool();
        var ids = new uint[numPlayers];
        for (int i = 0; i < numPlayers; i++)
            ids[i] = r.ReadDword();
        bool boring = r.ReadBool();

        var players = new List<NetPlayer>(numPlayers);
        for (int i = 0; i < numPlayers; i++)
            players.Add(new NetPlayer(slots[i].Slot, slots[i].Team, slots[i].Name, ids[i], handicaps[i]));

        return new GameServerPacket
        {
            Uid = uid, Version = version, Name = name, Accepted = accepted, GameSeed = seed,
            Paused = paused, NoLevelUp = noLevelUp, LevelStart = levelStart, Survivor = survivor,
            ComboMin = comboMin, DelayStart = delayStart, GameEnd = gameEnd, GameEndValue = gameEndValue,
            Players = players, WantsMoves = wantsMoves, SyncPoint = syncPoint, PotatoTeam = potatoTeam,
            AllowHandicap = allowHandicap, HotPotato = hotPotato,
            NormalAttack = normal, CleanAttack = clean,
            PotatoNormalAttack = potatoNormal, PotatoCleanAttack = potatoClean,
            Single = single, Terminated = terminated, BoringRules = boring,
        };
    }

    private static WantJoinPacket ReadWantJoin(NetReader r)
    {
        uint uid = r.ReadDword();
        bool current = r.ReadByte() != 0;
        byte version = r.ReadByte();
        byte language = r.ReadByte();
        byte os = r.ReadByte();
        r.ReadBool();
        return new WantJoinPacket { Uid = uid, Current = current, NetVersion = version, Language = language, Os = os };
    }

    private static PlayerWantJoinPacket ReadPlayerWantJoin(NetReader r)
    {
        uint uid = r.ReadDword();
        byte team = r.ReadByte();
        string name = r.ReadString(40, out _);
        byte player = r.ReadByte();
        int repeat = r.ReadInt();
        int smooth = r.ReadInt();
        int shadow = r.ReadInt();
        int handicap = r.ReadInt();
        var playerHash = r.ReadBytes(16);
        string teamName = r.ReadString(40, out _);
        var teamHash = r.ReadBytes(16);
        int h = r.ReadInt();
        int v = r.ReadInt();
        // Older peers send one shared repeat speed and no separate ones.
        if (repeat != -1 && h == 0)
            h = repeat;
        if (repeat != -1 && v == 0)
            v = repeat;
        return new PlayerWantJoinPacket
        {
            Uid = uid, Team = team, Name = name, Player = player, Smooth = smooth, Shadow = shadow, Handicap = handicap,
            PlayerHash = playerHash, TeamName = teamName, TeamHash = teamHash, HRepeat = h, VRepeat = v,
        };
    }

    private static PlayerPacket ReadPlayer(NetReader r)
    {
        uint uid = r.ReadDword();
        byte team = r.ReadByte();
        string name = r.ReadString(40, out _);
        byte player = r.ReadByte();
        int repeat = r.ReadInt();
        int hRepeat = 0, vRepeat = 0;
        if (repeat != -1)
            hRepeat = vRepeat = repeat;
        int smooth = r.ReadInt();
        int shadow = r.ReadInt();
        byte pos = r.ReadByte();
        int handicap = r.ReadInt();
        int h = r.ReadInt();
        if (repeat == -1 || h != 0)
            hRepeat = h;
        int v = r.ReadInt();
        if (repeat == -1 || v != 0)
            vRepeat = v;
        uint playerId = r.ReadDword();

        return new PlayerPacket
        {
            Uid = uid, Team = team, Name = name, Player = player, HRepeat = hRepeat, VRepeat = vRepeat,
            Smooth = smooth, Shadow = shadow, Pos = pos, Handicap = handicap, PlayerId = playerId,
        };
    }

    private static StatPacket ReadStat(NetReader r, byte slot)
    {
        int count = r.ReadByte();
        var stats = new List<StatValue>(count);
        for (int i = 0; i < count; i++)
        {
            byte stat = r.ReadByte();
            stats.Add(new StatValue(stat, r.ReadInt()));
        }
        return new StatPacket(stats) { Slot = slot };
    }

    private static DownloadPacket ReadDownload(NetReader r, byte slot)
    {
        const int size = DownloadPacket.Rows * DownloadPacket.Columns;
        var cells = r.ReadBytes(size);
        for (int i = 0; i < size; i++)
            cells[i] &= 0x7F;
        int seed = r.ReadInt();
        byte idle = r.ReadByte(), state = r.ReadByte();
        byte bloc = r.ReadByte(), next = r.ReadByte(), next2 = r.ReadByte(), next3 = r.ReadByte();
        int bonus = r.ReadByte();
        if (idle > 3 || state > 3 || bonus > 20 || (bloc > 6 && bloc != DownloadPacket.NoPiece))
            throw new InvalidDataException("A download packet has a value out of range.");
        var x = new byte[20];
        var color = new byte[20];
        for (int i = 0; i < 20; i++)
        {
            x[i] = r.ReadByte();
            color[i] = r.ReadByte();
        }
        var attacks = r.ReadBytes(MaxPlayers);
        byte lastAttacker = r.ReadByte();
        var blinded = r.ReadBytes(size);
        var blind = r.ReadBytes(20);
        var garbage = new List<DownloadedGarbage>(bonus);
        for (int i = 0; i < bonus; i++)
        {
            ushort hole = r.ReadWord();
            garbage.Add(new DownloadedGarbage(x[i], color[i], blind[i], (ushort)(hole & 0x3FF), (hole & 0x8000) != 0));
        }
        return new DownloadPacket
        {
            Slot = slot, Cells = cells, Seed = seed, Idle = idle, State = state, Bloc = bloc, Next = next, Next2 = next2, Next3 = next3,
            Garbage = garbage, Attacks = attacks, LastAttacker = lastAttacker, Blinded = blinded,
        };
    }

    private static Packet ReadPlayerBase(NetReader r, Func<byte, Packet> build) => build(r.ReadByte());

    private static StampBlockPacket ReadStampBlock(NetReader r)
    {
        byte slot = r.ReadByte();
        return new StampBlockPacket
        {
            Slot = slot,
            X = r.ReadByte(),
            Y = r.ReadByte(),
            Rotate = r.ReadByte(),
            Score = r.ReadByte(),
            Date = r.ReadWord(),
            BlockRotated = r.ReadByte(),
            TimeHeld = r.ReadWord(),
        };
    }

    private static LinesPacket ReadLines(NetReader r)
    {
        byte slot = r.ReadByte();
        byte nb = r.ReadByte();
        byte nc = r.ReadByte();
        byte lx = r.ReadByte();
        byte sender = r.ReadByte();
        var attack = ReadAttack(r);
        var holes = new ushort[Math.Min(nb, (byte)36)];
        for (int i = 0; i < holes.Length; i++)
            holes[i] = r.ReadWord();
        return new LinesPacket { Slot = slot, Nb = nb, Nc = nc, Lx = lx, Sender = sender, Attack = attack, HolePos = holes };
    }

    private static MovesPacket ReadMoves(NetReader r, byte slot)
    {
        int size = r.ReadByte();
        var moves = new byte[size];
        for (int i = 0; i < size; i++)
            moves[i] = r.ReadByte();
        return new MovesPacket(moves) { Slot = slot };
    }
}
