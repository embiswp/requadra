using System.Net;
using System.Net.Sockets;
using Quadra.Core.Qserv;

namespace Quadra.Core.Net;

/// <summary>
/// Finding games on the local network without a game list server, as the original does with UDP: a client
/// broadcasts a <see cref="FindGamePacket"/> to the UDP port next to the game's TCP port, and each host answers
/// the sender with a <see cref="GameInfoPacket"/> sent to that same UDP port.
/// </summary>
public static class LanDiscovery
{
    /// <summary>The UDP port that belongs to a TCP port (<c>Net_param::udpport</c>).</summary>
    public static int UdpPort(int tcpPort) => tcpPort + 1;

    public static GameInfoPacket Describe(GameAnnouncement game) => new()
    {
        Id = (byte)PacketType.GameInfo, Name = game.Name, Version = (byte)game.Version, Port = game.Port,
        Players = [.. game.Players.Select(p => new GameInfoPlayer((byte)p.Team, p.Name, p.Handicap))],
        GameEndValue = game.EndValue, NoLevelUp = !game.LevelUp, DelayStart = !game.Started, Terminated = game.Terminated,
        LevelStart = (byte)game.LevelStart, ComboMin = (byte)game.MinCombo, Survivor = game.Survivor, GameEnd = game.EndType,
        AllowHandicap = game.AllowHandicap, HotPotato = game.HotPotato,
        NormalAttack = game.NormalAttack, CleanAttack = game.CleanAttack,
        PotatoNormalAttack = game.PotatoNormalAttack, PotatoCleanAttack = game.PotatoCleanAttack,
    };

    public static GameAnnouncement ToAnnouncement(GameInfoPacket info) => new()
    {
        Name = info.Name, Version = info.Version, Port = info.Port,
        Players = [.. info.Players.Select(p => new AnnouncedPlayer(p.Name, p.Team, 0, p.Handicap))],
        EndValue = info.GameEndValue, LevelUp = !info.NoLevelUp, Started = !info.DelayStart, Terminated = info.Terminated,
        LevelStart = info.LevelStart, MinCombo = info.ComboMin, Survivor = info.Survivor, RulesType = info.Survivor ? 1 : 0,
        EndType = info.GameEnd, AllowHandicap = info.AllowHandicap, HotPotato = info.HotPotato,
        NormalAttack = info.NormalAttack, CleanAttack = info.CleanAttack,
        PotatoNormalAttack = info.PotatoNormalAttack, PotatoCleanAttack = info.PotatoCleanAttack,
    };

    /// <summary>
    /// Asks the local network (or <paramref name="target"/>) which games are being hosted and collects the answers for
    /// <paramref name="wait"/>. Answers arrive on <paramref name="listenPort"/>, which is where hosts send them.
    /// </summary>
    public static async Task<IReadOnlyList<ListedGame>> FindAsync(
        int listenPort, TimeSpan wait, IPEndPoint? target = null, CancellationToken ct = default)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort)) { EnableBroadcast = true };
        target ??= new IPEndPoint(IPAddress.Broadcast, listenPort);
        var request = PacketEncoder.Encode(new FindGamePacket { Id = (byte)PacketType.FindGame });
        await udp.SendAsync(request, target, ct);

        var games = new List<ListedGame>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(wait);
        try
        {
            while (true)
            {
                var received = await udp.ReceiveAsync(timeout.Token);
                try
                {
                    if (PacketDecoder.Decode(received.Buffer) is not GameInfoPacket info)
                        continue;
                    string host = received.RemoteEndPoint.Address.ToString();
                    if (!games.Any(g => g.Host == host && g.Info.Port == info.Port))
                        games.Add(new ListedGame(host, ToAnnouncement(info)));
                }
                catch (InvalidDataException)
                {
                    // Not one of ours: whatever else shares the port is ignored.
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The time for answers is up.
        }
        return games;
    }
}

/// <summary>Answers the questions of <see cref="LanDiscovery.FindAsync"/> for one hosted game.</summary>
public sealed class LanResponder : IDisposable
{
    private readonly GameHost _host;
    private readonly int _tcpPort;
    private readonly int _replyPort;
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    /// <param name="listenPort">Where questions arrive; the game's UDP port unless a test says otherwise.</param>
    /// <param name="replyPort">Where answers are sent on the asker's machine; the same UDP port, as in the original.</param>
    public LanResponder(GameHost host, int tcpPort, int? listenPort = null, int? replyPort = null, IPAddress? bind = null)
    {
        _host = host;
        _tcpPort = tcpPort;
        _replyPort = replyPort ?? LanDiscovery.UdpPort(tcpPort);
        _udp = new UdpClient(new IPEndPoint(bind ?? IPAddress.Any, listenPort ?? LanDiscovery.UdpPort(tcpPort))) { EnableBroadcast = true };
        _loop = RunAsync();
    }

    public int Answered { get; private set; }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var received = await _udp.ReceiveAsync(_stop.Token);
                try
                {
                    if (PacketDecoder.Decode(received.Buffer) is not FindGamePacket)
                        continue;
                }
                catch (InvalidDataException)
                {
                    continue;
                }
                var info = LanDiscovery.Describe(_host.Announcement(_tcpPort));
                await _udp.SendAsync(PacketEncoder.Encode(info), new IPEndPoint(received.RemoteEndPoint.Address, _replyPort), _stop.Token);
                Answered++;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
            // The socket went away with the host.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _udp.Dispose();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
        _stop.Dispose();
    }
}
