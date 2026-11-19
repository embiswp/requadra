using System.Net;
using System.Net.Sockets;
using Quadra.Core.Net;

namespace Quadra.Tests;

public class LanDiscoveryTests
{
    private static int FreeUdpPort()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }

    [Fact]
    public void The_question_and_the_answer_survive_the_wire_and_carry_the_magic_number()
    {
        var ask = PacketEncoder.Encode(new FindGamePacket { Id = (byte)PacketType.FindGame });
        Assert.Equal([(byte)PacketType.FindGame, (byte)'R', (byte)'M', (byte)'T', (byte)'3'], ask);
        Assert.IsType<FindGamePacket>(PacketDecoder.Decode(ask));

        var info = new GameInfoPacket
        {
            Id = (byte)PacketType.GameInfo, Name = "Friday", Version = 20, Port = 3456,
            Players = [new GameInfoPlayer(0, "Ann", 0), new GameInfoPlayer(3, "Bob", 2)],
            GameEndValue = 5, NoLevelUp = true, DelayStart = true, LevelStart = 4, ComboMin = 2, Survivor = true,
            GameEnd = GameEnd.Lines, AllowHandicap = false,
        };
        var back = Assert.IsType<GameInfoPacket>(PacketDecoder.Decode(PacketEncoder.Encode(info)));
        Assert.Equal(info with { Players = [] }, back with { Players = [] });
        Assert.Equal(info.Players, back.Players);
    }

    [Fact]
    public void Something_without_the_magic_number_is_not_ours()
    {
        Assert.Throws<InvalidDataException>(() => PacketDecoder.Decode([(byte)PacketType.FindGame, 1, 2, 3, 4]));
    }

    [Fact]
    public async Task A_host_on_the_network_answers_a_search_and_is_listed()
    {
        var host = new GameHost(new HostRules { Name = "LAN game", Survivor = true, GameEnd = GameEnd.Frag, GameEndValue = 3 });
        int hostPort = FreeUdpPort(), askerPort = FreeUdpPort();
        using var responder = new LanResponder(host, tcpPort: 4000, listenPort: hostPort, replyPort: askerPort, bind: IPAddress.Loopback);

        var games = await LanDiscovery.FindAsync(askerPort, TimeSpan.FromMilliseconds(500), new IPEndPoint(IPAddress.Loopback, hostPort));

        var game = Assert.Single(games);
        Assert.Equal("127.0.0.1", game.Host);
        Assert.Equal(("LAN game", 4000, true, GameEnd.Frag, 3), (game.Info.Name, game.Info.Port, game.Info.Survivor, game.Info.EndType, game.Info.EndValue));
        Assert.False(game.Info.Started);
        Assert.Equal(1, responder.Answered);
    }

    [Fact]
    public async Task Nobody_answering_gives_an_empty_list()
    {
        var games = await LanDiscovery.FindAsync(FreeUdpPort(), TimeSpan.FromMilliseconds(150), new IPEndPoint(IPAddress.Loopback, FreeUdpPort()));
        Assert.Empty(games);
    }
}
