using System.Net;
using Quadra.Core.Game;
using Quadra.Core.Input;
using Quadra.Core.Net;
using Quadra.Core.Settings;

namespace Quadra.Tests;

public class ClientSessionTests
{
    private static NetworkGame NewGame(GameClient client, byte slot) =>
        new(client, slot, new KeyStates(), new KeyBindings(1, 2, 3, 4, 5, 6, 7));

    private static void RunUntil(ClientSession session, Func<bool> done)
    {
        for (int i = 0; i < 500 && !done(); i++)
        {
            session.Update();
            Thread.Sleep(10);
        }
    }

    [Fact]
    public async Task A_session_connects_joins_and_gets_a_place()
    {
        var host = new GameHost(new HostRules());
        await using var server = new TcpGameServer(host, IPAddress.Loopback, 0);
        server.Start();

        var session = new ClientSession("127.0.0.1", server.Port, new PlayerSettings { Name = "Ann" }, null, NewGame);
        Assert.Equal(SessionStage.Connecting, session.Stage);
        RunUntil(session, () => session.Game is not null);

        Assert.NotNull(session.Game);
        Assert.Equal(SessionStage.Waiting, session.Stage);
        Assert.Equal("Ann", host.State.Players[session.Game.Slot].Name);
        session.Stop();
    }

    [Fact]
    public async Task A_name_given_on_the_command_line_replaces_the_players_own()
    {
        var host = new GameHost(new HostRules());
        await using var server = new TcpGameServer(host, IPAddress.Loopback, 0);
        server.Start();

        var session = new ClientSession("127.0.0.1", server.Port, new PlayerSettings { Name = "Ann" }, "Zed", NewGame);
        RunUntil(session, () => session.Game is not null);

        Assert.Equal("Zed", host.State.Players[session.Game!.Slot].Name);
        session.Stop();
    }

    [Fact]
    public void A_session_fails_with_a_message_when_nobody_is_listening()
    {
        int unused;
        using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            unused = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        var session = new ClientSession("127.0.0.1", unused, new PlayerSettings(), null, NewGame);
        RunUntil(session, () => session.Stage == SessionStage.Failed);

        Assert.Equal(SessionStage.Failed, session.Stage);
        Assert.StartsWith("Could not connect", session.Message);
        session.Stop();
    }
}
