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
