namespace Quadra.Core.Net;

/// <summary>
/// A client sends a packet about its own player with a "client" id; the host applies it and passes it on
/// to everyone else under the plain id. This is the pairing of the two.
/// </summary>
public static class ClientPacketRelay
{
    public static PacketType RelayedAs(PacketType clientType) => clientType switch
    {
        PacketType.ClientStampBlock => PacketType.StampBlock,
        PacketType.ClientDead => PacketType.Dead,
        PacketType.ClientRespawn => PacketType.Respawn,
        PacketType.ClientLines => PacketType.Lines,
        PacketType.ClientFirstFrag => PacketType.FirstFrag,
        PacketType.ClientGone => PacketType.Gone,
        PacketType.ClientState => PacketType.State,
        PacketType.ClientRemoveBonus => PacketType.RemoveBonus,
        _ => clientType,
    };

    /// <summary>The same packet with the id it has once relayed.</summary>
    public static Packet AsRelayed(Packet clientPacket) =>
        clientPacket with { Id = (byte)RelayedAs((PacketType)clientPacket.Id) };
}
