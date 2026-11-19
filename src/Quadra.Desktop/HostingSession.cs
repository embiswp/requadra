using System.Net;
using System.Net.Sockets;
using Quadra.Core.Net;
using Quadra.Core.Qserv;

namespace Quadra.Desktop;

/// <summary>Everything that makes a game this window hosts reachable: the TCP server, the answers to LAN searches and the list server announcement.</summary>
internal sealed class HostingSession
{
    private readonly TcpGameServer _server;
    private readonly LanResponder? _lanResponder;
    private readonly HostAnnouncer? _announcer;

    /// <summary>Starts listening on <paramref name="port"/>; throws <see cref="SocketException"/> if it cannot.</summary>
    public HostingSession(GameHost game, int port, Uri? listServer, HttpClient http)
    {
        _server = new TcpGameServer(game, IPAddress.Any, port);
        _server.Start();
        Port = _server.Port;

        if (listServer is not null)
            _announcer = new HostAnnouncer(game, new QservClient(http, listServer), Port);
        try
        {
            _lanResponder = new LanResponder(game, Port);
        }
        catch (SocketException)
        {
            // The UDP port is taken (a second game on this machine): only the list server will show this game.
        }
    }

    public int Port { get; }

    public void Stop()
    {
        _lanResponder?.Dispose();
        _announcer?.DisposeAsync().AsTask().Wait(3000);
        _server.DisposeAsync().AsTask().Wait(1000);
    }
}
