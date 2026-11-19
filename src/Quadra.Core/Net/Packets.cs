namespace Quadra.Core.Net;

public enum PacketType : byte
{
    Chat, FindGame, GameServer, WantJoin, PlayerWantJoin, ClientChat, PlayerAccepted, Player,
    ClientPause, Pause, Stat, GameInfo, DropPlayer, ClientDropPlayer, ServerDropPlayer,
    StampBlock, ClientStampBlock, Dead, ClientDead, Respawn, ClientRespawn, DeprecatedWatch,
    StartWatch, ClientStartWatch, Download, ClientLines, Lines, ClientTestPing, TestPing,
    FirstFrag, ClientFirstFrag, Gone, ClientGone, EndGame, Rejoin, Moves, ClientMoves,
    State, ClientState, ServerState, ServerRandom, ServerPotato, DeprecatedServerLines,
    ServerTestPing, Bye, RemoveBonus, ClientRemoveBonus, ServerNameTeam, GameStat, ServerLog,
}

public enum GameEnd : byte { Never, Frag, Time, Points, Lines }

public enum AttackType : byte { Lines, None, Blind, FullBlind }

public readonly record struct Attack(AttackType Type, int Param);

public sealed record NetPlayer(byte Slot, byte Team, string Name, uint PlayerId, int Handicap);

/// <summary>Base of every decoded packet. <see cref="Id"/> is the raw wire id.</summary>
public abstract record Packet
{
    public byte Id { get; init; }

    /// <summary>The decoder read past the end of the payload (zeros were substituted).</summary>
    public bool Truncated { get; init; }

    /// <summary>Payload bytes the decoder did not consume.</summary>
    public int Unread { get; init; }

    public PacketType Type => (PacketType)Id;
}

/// <summary>A packet type this port does not decode yet; the payload is kept verbatim.</summary>
public sealed record RawPacket(byte[] Payload) : Packet;

public sealed record ChatPacket(sbyte Team, sbyte ToTeam, string Text) : Packet;

public sealed record GameServerPacket : Packet
{
    public uint Uid { get; init; }
    public byte Version { get; init; }
    public string Name { get; init; } = "";
    public bool Accepted { get; init; }
    public int GameSeed { get; init; }
    public bool Paused { get; init; }
    public bool NoLevelUp { get; init; }
    public byte LevelStart { get; init; }
    public bool Survivor { get; init; }
    public byte ComboMin { get; init; }
    public ushort DelayStart { get; init; }
    public GameEnd GameEnd { get; init; }
    public int GameEndValue { get; init; }
    public IReadOnlyList<NetPlayer> Players { get; init; } = [];
    public bool WantsMoves { get; init; }
    public byte SyncPoint { get; init; }
    public byte PotatoTeam { get; init; }
    public bool AllowHandicap { get; init; } = true;
    public bool HotPotato { get; init; }
    public Attack NormalAttack { get; init; }
    public Attack CleanAttack { get; init; }
    public Attack PotatoNormalAttack { get; init; }
    public Attack PotatoCleanAttack { get; init; }
    public bool Single { get; init; }
    public bool Terminated { get; init; }
    public bool BoringRules { get; init; }
}

public sealed record PlayerPacket : Packet
{
    public uint Uid { get; init; }
    public byte Team { get; init; }
    public string Name { get; init; } = "";
    public byte Player { get; init; }
    public int HRepeat { get; init; }
    public int VRepeat { get; init; }
    public int Smooth { get; init; }
    public int Shadow { get; init; }
    /// <summary>Slot (net_list position) the player occupies in the game.</summary>
    public byte Pos { get; init; }
    public int Handicap { get; init; }
    public uint PlayerId { get; init; }
}

/// <summary>Packets that start with a player slot byte (Packet_playerbase).</summary>
public abstract record PlayerPacketBase : Packet
{
    public byte Slot { get; init; }
}

public sealed record PausePacket(sbyte Player) : Packet;

public sealed record StampBlockPacket : PlayerPacketBase
{
    public byte X { get; init; }
    public byte Y { get; init; }
    public byte Rotate { get; init; }
    public byte Score { get; init; }
    public ushort Date { get; init; }
    public byte BlockRotated { get; init; }
    public ushort TimeHeld { get; init; }
}

public sealed record DeadPacket(bool ThenGone) : PlayerPacketBase;

public sealed record RespawnPacket : PlayerPacketBase;

public sealed record FirstFragPacket : PlayerPacketBase;

public sealed record GonePacket(bool ChatMsg) : PlayerPacketBase;

public sealed record LinesPacket : PlayerPacketBase
{
    public byte Nb { get; init; }
    public byte Nc { get; init; }
    public byte Lx { get; init; }
    public byte Sender { get; init; }
    public Attack Attack { get; init; }
    public IReadOnlyList<ushort> HolePos { get; init; } = [];
}

/// <summary>Raw key-state bytes for a player; one byte per frame.</summary>
public sealed record MovesPacket(byte[] Moves) : PlayerPacketBase;

public sealed record StatePacket(byte State) : PlayerPacketBase;

/// <summary>A broadcast that asks hosts on the local network to describe their game (UDP, <c>Packet_findgame</c>).</summary>
public sealed record FindGamePacket : Packet;

/// <summary>A player as the UDP game description lists them.</summary>
public readonly record struct GameInfoPlayer(byte Team, string Name, int Handicap);

/// <summary>A host's answer to <see cref="FindGamePacket"/> (UDP, <c>Packet_gameinfo</c>).</summary>
public sealed record GameInfoPacket : Packet
{
    public string Name { get; init; } = "";
    public byte Version { get; init; }

    /// <summary>The TCP port to join on.</summary>
    public int Port { get; init; }

    public IReadOnlyList<GameInfoPlayer> Players { get; init; } = [];
    public int GameEndValue { get; init; }
    public bool NoLevelUp { get; init; }

    /// <summary>The game has not started yet: it is still waiting or counting down.</summary>
    public bool DelayStart { get; init; }

    public bool Terminated { get; init; }
    public byte LevelStart { get; init; } = 1;
    public byte ComboMin { get; init; }
    public bool Survivor { get; init; }
    public GameEnd GameEnd { get; init; }
    public bool AllowHandicap { get; init; } = true;
    public bool HotPotato { get; init; }
    public Attack NormalAttack { get; init; }
    public Attack CleanAttack { get; init; }
    public Attack PotatoNormalAttack { get; init; }
    public Attack PotatoCleanAttack { get; init; }
}

/// <summary>One counter of a <see cref="StatPacket"/> (<c>CS::Stat_type</c>).</summary>
public readonly record struct StatValue(byte Stat, int Value)
{
    public const byte Death = 23, Frag = 24, LinesTotal = 25, Score = 26;
}

/// <summary>A player's counters, as the host sends them to a connection that joins late.</summary>
public sealed record StatPacket(IReadOnlyList<StatValue> Stats) : PlayerPacketBase;

/// <summary>A garbage row waiting to come in, as the late-join packet carries it.</summary>
public readonly record struct DownloadedGarbage(byte X, byte Color, byte BlindTime, ushort HolePos, bool Final);

/// <summary>
/// A player's whole board as it stands, sent to a connection that joins a game that has begun
/// (<c>Packet_download</c>). <see cref="Cells"/> and <see cref="Blinded"/> are 32 rows of 10 columns.
/// </summary>
public sealed record DownloadPacket : PlayerPacketBase
{
    public const int Rows = 32, Columns = 10, NoPiece = 255;

    public byte[] Cells { get; init; } = new byte[Rows * Columns];
    public int Seed { get; init; }

    /// <summary>0 busy, 1 playing, 2 dead and waiting, 3 gone.</summary>
    public byte Idle { get; init; } = 1;

    /// <summary>A <see cref="Game.RoundState"/>.</summary>
    public byte State { get; init; }

    public byte Bloc { get; init; } = NoPiece;
    public byte Next { get; init; } = NoPiece;
    public byte Next2 { get; init; } = NoPiece;
    public byte Next3 { get; init; } = NoPiece;
    public IReadOnlyList<DownloadedGarbage> Garbage { get; init; } = [];
    public byte[] Attacks { get; init; } = new byte[PacketDecoder.MaxPlayers];
    public byte LastAttacker { get; init; } = NoPiece;
    public byte[] Blinded { get; init; } = new byte[Rows * Columns];
}

public sealed record EndGamePacket(bool AutoEnd) : Packet;

public sealed record ServerRandomPacket(uint Seed) : Packet;

/// <summary>A connection asks to take part. A first byte of zero marks a 1.1.0 client, which is ignored.</summary>
public sealed record WantJoinPacket : Packet
{
    public uint Uid { get; init; }
    public bool Current { get; init; } = true;
    public byte NetVersion { get; init; }
    public byte Language { get; init; }
    public byte Os { get; init; }
}

/// <summary>A connection asks to add one of its players to the game.</summary>
public sealed record PlayerWantJoinPacket : Packet
{
    public uint Uid { get; init; }
    public byte Team { get; init; }
    public string Name { get; init; } = "";
    public byte Player { get; init; }
    public int Smooth { get; init; }
    public int Shadow { get; init; }
    public int Handicap { get; init; }
    public byte[] PlayerHash { get; init; } = new byte[16];
    public string TeamName { get; init; } = "";
    public byte[] TeamHash { get; init; } = new byte[16];
    public int HRepeat { get; init; }
    public int VRepeat { get; init; }
}

/// <summary>Why a player could not join (or how they did); <see cref="PlayerAcceptedPacket.Accepted"/>.</summary>
public enum JoinAnswer : byte
{
    Accepted = 0,
    Refused = 1,
    NameTaken = 2,
    GameFinished = 3,
    Replacing = 4,
    Full = 5,
}

public sealed record PlayerAcceptedPacket : Packet
{
    public uint Uid { get; init; }
    public byte Pos { get; init; }
    public JoinAnswer Accepted { get; init; }
}

public enum DropReason : byte { Auto, Manual, InvalidBlock }

/// <summary>Sent in the same layout as <see cref="PacketType.DropPlayer"/>, <see cref="PacketType.ClientDropPlayer"/> and <see cref="PacketType.ServerDropPlayer"/>.</summary>
public sealed record DropPlayerPacket(DropReason Reason) : PlayerPacketBase;

/// <summary>Round-trip measurement: the client sends one, the host sends it back (<see cref="PacketType.ClientTestPing"/>, <see cref="PacketType.TestPing"/>, <see cref="PacketType.ServerTestPing"/>).</summary>
public sealed record TestPingPacket(uint Frame) : Packet;
