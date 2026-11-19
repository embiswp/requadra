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
