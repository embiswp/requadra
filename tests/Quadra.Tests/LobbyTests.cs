using Quadra.Core.Menus;
using Quadra.Core.Qserv;

namespace Quadra.Tests;

public class LobbyTests
{
    private static ListedGame Game(string host, int port, string name, int players = 0) =>
        new(host, new GameAnnouncement
        {
            Name = name, Port = port,
            Players = [.. Enumerable.Range(0, players).Select(i => new AnnouncedPlayer($"p{i}", i, 0, 0))],
        });

    [Fact]
    public void The_multi_player_item_opens_the_lobby_and_back_returns()
    {
        var flow = MenuFixtures.NewFlow();
        flow.Activate("multi");
        Assert.Equal(Screen.Lobby, flow.Current);
        Assert.Contains(flow.Menu.Items, i => i.Id == "host");
        flow.Back();
        Assert.Equal(Screen.Main, flow.Current);
    }

    [Fact]
    public void Without_a_list_server_nothing_is_fetched_and_the_status_says_so()
    {
        var flow = MenuFixtures.NewFlow();
        flow.Show(Screen.Lobby);
        Assert.False(flow.TakeRefreshRequest());
        Assert.Contains("No game list server", flow.Lobby.Status);
    }

    [Fact]
    public void Typing_the_list_server_saves_it_and_asks_for_the_list()
    {
        var flow = MenuFixtures.NewFlow();
        flow.Show(Screen.Lobby);
        flow.Activate("server");
        foreach (char c in "http://x/q")
            flow.Lobby.TypeCharacter(c);
        Assert.Equal("http://x/q_", flow.Lobby.FieldText("server"));

        flow.Back();   // ends the typing

        Assert.Equal(LobbyEdit.None, flow.Lobby.Edit);
        Assert.Equal("http://x/q", flow.Settings.ListServer);
        Assert.True(flow.TakeRefreshRequest());
        Assert.False(flow.TakeRefreshRequest());
        Assert.Equal(Screen.Lobby, flow.Current);
    }

    [Fact]
    public void Games_from_the_list_become_rows_that_join_when_chosen()
    {
        var flow = MenuFixtures.NewFlow();
        flow.Show(Screen.Lobby);
        flow.SetLobbyGames([Game("10.0.0.5", 3456, "Ann's game", 2), Game("10.0.0.6", 4000, "Bob's game")]);

        Assert.Equal(["game.0", "game.1"], flow.Menu.Items.Where(i => i.Id.StartsWith("game.")).Select(i => i.Id));
        Assert.Equal("Ann's game|2|waiting", LobbyModel.Describe(flow.Lobby.Games[0]));

        flow.Activate("game.1");
        Assert.Equal(new NetworkRequest(false, "10.0.0.6", 4000), flow.TakeNetworkRequest());
        Assert.Null(flow.TakeNetworkRequest());
    }

    [Fact]
    public void Hosting_asks_for_a_game_on_the_default_port()
    {
        var flow = MenuFixtures.NewFlow();
        flow.Show(Screen.Lobby);
        flow.Activate("host");
        var request = flow.TakeNetworkRequest()!;
        Assert.True(request.Host);
        Assert.Equal(3456, request.Port);
    }

    [Theory]
    [InlineData("example.org", "example.org", 3456)]
    [InlineData(" 10.1.2.3:4000 ", "10.1.2.3", 4000)]
    public void Addresses_have_a_host_and_an_optional_port(string text, string host, int port) =>
        Assert.Equal(new NetworkRequest(false, host, port), LobbyModel.ParseAddress(text));

    [Theory]
    [InlineData("")]
    [InlineData("host:")]
    [InlineData("host:0")]
    [InlineData("host:99999")]
    [InlineData("host:abc")]
    [InlineData(":3456")]
    public void Bad_addresses_are_refused(string text) => Assert.Null(LobbyModel.ParseAddress(text));

    [Fact]
    public void Joining_by_a_bad_address_stays_on_the_screen_with_a_message()
    {
        var flow = MenuFixtures.NewFlow();
        flow.Show(Screen.Lobby);
        flow.Activate("join");
        Assert.Null(flow.TakeNetworkRequest());
        Assert.Contains("host:port", flow.Lobby.Status);
    }

    [Fact]
    public void Joining_by_address_uses_what_was_typed()
    {
        var flow = MenuFixtures.NewFlow();
        flow.Show(Screen.Lobby);
        flow.Activate("address");
        foreach (char c in "lan-pc:3999")
            flow.Lobby.TypeCharacter(c);
        flow.Back();
        flow.Activate("join");
        Assert.Equal(new NetworkRequest(false, "lan-pc", 3999), flow.TakeNetworkRequest());
        Assert.Equal("lan-pc:3999", flow.Settings.LastAddress);
    }

    [Fact]
    public void Only_printable_characters_are_typed_and_backspace_removes_them()
    {
        var flow = MenuFixtures.NewFlow();
        flow.Show(Screen.Lobby);
        flow.Lobby.TypeCharacter('x');          // not editing: ignored
        Assert.Equal("", flow.Lobby.Address);

        flow.Activate("address");
        flow.Lobby.TypeCharacter('a');
        flow.Lobby.TypeCharacter('\n');
        flow.Lobby.TypeCharacter('b');
        Assert.Equal("ab", flow.Lobby.Address);
        flow.Lobby.Backspace();
        Assert.Equal("a", flow.Lobby.Address);
    }
}
