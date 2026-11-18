using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Time.Testing;
using Quadra.Core.Net;
using Quadra.Core.Qserv;
using Quadra.Server;

namespace Quadra.Tests;

public class QservTreeTests
{
    [Fact]
    public void Lines_become_nested_nodes_and_format_back()
    {
        var tree = QservTree.Parse(["name Fun game", "players/0/name Ann", "players/0/team 3", "players/1/name Bob"]);

        Assert.Equal("Fun game", tree.Get("name"));
        Assert.Equal("3", tree.Get("players/0/team"));
        Assert.Equal(["0", "1"], tree.Sub("players")!.Nodes().Select(n => n.Key));
        Assert.Equal("a/name Fun game\na/players/0/name Ann\na/players/0/team 3\na/players/1/name Bob\n", tree.Format("a"));
    }

    [Fact]
    public void The_value_starts_after_the_first_whitespace_and_may_hold_more()
    {
        var tree = QservTree.Parse(["name  two  spaces", "flag", "tab\tvalue"]);

        Assert.Equal(" two  spaces", tree.Get("name"));
        Assert.Equal("", tree.Get("flag"));
        Assert.Equal("value", tree.Get("tab"));
    }

    [Fact]
    public void A_missing_path_reads_as_null_and_a_node_is_not_a_leaf()
    {
        var tree = QservTree.Parse(["a/b 1"]);

        Assert.Null(tree.Get("a/c"));
        Assert.Null(tree.Get("x/y"));
        Assert.Null(tree.Get("a"));
        Assert.Null(tree.Sub("a/b"));
    }
}

public class QservServerTests
{
    private static readonly string Posted = "postgame\nname Test game\nversion 20\nport 3456\n";

    private static (QservServer, FakeTimeProvider) Make(QservOptions? options = null)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return (new QservServer(options, clock), clock);
    }

    [Fact]
    public void An_unknown_command_gets_the_greeting()
    {
        var (server, _) = Make();

        Assert.Equal(QservServer.Greeting, server.Handle("nothing", "1.2.3.4"));
        Assert.Equal(QservServer.Greeting, server.Handle("", "1.2.3.4"));
    }

    [Fact]
    public void A_posted_game_is_added_then_updated_and_listed_under_the_callers_address()
    {
        var (server, _) = Make();

        Assert.Equal("Game added\n", server.Handle(Posted, "1.2.3.4"));
        Assert.Equal("Game updated\n", server.Handle(Posted, "1.2.3.4"));

        var reply = QservReply.Parse(server.Handle("getgames\ninfo/quadra_version 1.3.0\n", "9.9.9.9"));
        Assert.Equal("Current games", reply.Status);
        var game = reply.Body.Sub("1.2.3.4:3456")!;
        Assert.Equal("Test game", game.Get("name"));
        Assert.Equal("1.2.3.4", game.Get("info/remoteaddr"));
        Assert.Equal("1767225600", game.Get("info/lastupdate"));
    }

    [Fact]
    public void The_server_decides_the_address_whatever_the_request_says()
    {
        var (server, _) = Make();

        server.Handle(Posted + "info/remoteaddr 6.6.6.6\n", "1.2.3.4");

        var body = server.Handle("getgames\ninfo/quadra_version 1.3.0\n", "9.9.9.9");
        Assert.DoesNotContain("6.6.6.6", body);
        Assert.Contains("1.2.3.4:3456/name Test game", body);
    }

    [Theory]
    [InlineData("port 99999")]
    [InlineData("port 0")]
    [InlineData("port abc")]
    [InlineData("name x")]
    public void A_missing_or_bad_port_falls_back_to_the_default(string line)
    {
        var (server, _) = Make();

        server.Handle("postgame\n" + line + "\n", "1.2.3.4");

        Assert.Contains("1.2.3.4:3456/", server.Handle("getgames\ninfo/quadra_version 1.3.0\n", "x"));
    }

    [Fact]
    public void Non_digits_are_stripped_from_the_port()
    {
        var (server, _) = Make();

        server.Handle("postgame\nport 34a56\n", "1.2.3.4");

        Assert.Contains("1.2.3.4:3456/", server.Handle("getgames\ninfo/quadra_version 1.3.0\n", "x"));
    }

    [Fact]
    public void A_game_that_stops_posting_expires_and_posting_again_keeps_it_alive()
    {
        var (server, clock) = Make();
        server.Handle(Posted, "1.2.3.4");

        clock.Advance(TimeSpan.FromSeconds(179));
        server.Handle(Posted, "1.2.3.4");
        clock.Advance(TimeSpan.FromSeconds(179));
        Assert.Equal(1, server.GameCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, server.GameCount);
        Assert.Equal("Current games\n", server.Handle("getgames\ninfo/quadra_version 1.3.0\n", "x"));
    }

    [Fact]
    public void A_game_can_be_deleted_once()
    {
        var (server, _) = Make();
        server.Handle(Posted, "1.2.3.4");

        Assert.Equal("Game deleted\n", server.Handle("deletegame\nport 3456\n", "1.2.3.4"));
        Assert.Equal("Game not found\n", server.Handle("deletegame\nport 3456\n", "1.2.3.4"));
    }

    [Fact]
    public void Only_the_poster_can_delete_a_game()
    {
        var (server, _) = Make();
        server.Handle(Posted, "1.2.3.4");

        Assert.Equal("Game not found\n", server.Handle("deletegame\nport 3456\n", "5.6.7.8"));
        Assert.Equal(1, server.GameCount);
    }

    [Fact]
    public void Games_are_listed_in_address_order()
    {
        var (server, _) = Make();
        server.Handle("postgame\nname B\n", "2.2.2.2");
        server.Handle("postgame\nname A\n", "1.1.1.1");

        var keys = server.Handle("getgames\ninfo/quadra_version 1.3.0\n", "x")
            .Split('\n').Skip(1).Where(l => l.Length > 0).Select(l => l.Split('/')[0]);

        Assert.Equal(["1.1.1.1:3456", "2.2.2.2:3456"], keys.Distinct());
    }

    [Fact]
    public void Old_clients_see_one_game_per_host_under_the_host_alone()
    {
        var (server, _) = Make();
        server.Handle("postgame\nname First\nport 3456\n", "1.1.1.1");
        server.Handle("postgame\nname Second\nport 4000\n", "1.1.1.1");

        var old = server.Handle("getgames\n", "x");
        var snoop = server.Handle("getgames\ninfo/qsnoop_version 1\n", "x");

        Assert.Contains("1.1.1.1/name First", old);
        Assert.DoesNotContain("Second", old);
        Assert.Contains("1.1.1.1:3456/name First", snoop);
        Assert.Contains("1.1.1.1:4000/name Second", snoop);
    }

    [Fact]
    public void Version_1_1_2_clients_get_no_player_list_but_the_stored_game_keeps_it()
    {
        var (server, _) = Make();
        server.Handle(Posted + "players/0/name Ann\n", "1.1.1.1");

        Assert.DoesNotContain("players", server.Handle("getgames\ninfo/quadra_version 1.1.2\n", "x"));
        Assert.Contains("players/0/name Ann", server.Handle("getgames\ninfo/quadra_version 1.3.0\n", "x"));
    }

    [Fact]
    public void Scores_come_back_best_first_and_a_post_answers_with_the_table()
    {
        var (server, _) = Make();
        server.Handle("postdemo\nscore 500\nname Low\nlines 5\nlevel 1\nrec AAAA\n", "1.1.1.1");
        var reply = QservReply.Parse(server.Handle("postdemo\nscore 9000\nname High\nlines 50\nlevel 4\nrec BBBB\nnum 5\n", "1.1.1.1"));

        Assert.Equal("Ok", reply.Status);
        Assert.Equal(["high000", "high001"], reply.Body.Nodes().Select(n => n.Key));
        Assert.Equal("High", reply.Body.Get("high000/name"));
        Assert.Equal("BBBB", reply.Body.Get("high000/rec"));
        Assert.Equal("Low", reply.Body.Get("high001/name"));
    }

    [Fact]
    public void The_number_of_scores_is_the_one_asked_for_or_five()
    {
        var (server, _) = Make();
        for (int i = 1; i <= 8; i++)
            server.Handle($"postdemo\nscore {i * 100}\nname P{i}\n", "1.1.1.1");

        Assert.Equal(5, QservReply.Parse(server.Handle("gethighscores\n", "x")).Body.Nodes().Count());
        Assert.Equal(2, QservReply.Parse(server.Handle("gethighscores\nnum 2\n", "x")).Body.Nodes().Count());
        Assert.Equal(8, QservReply.Parse(server.Handle("gethighscores\nnum 50\n", "x")).Body.Nodes().Count());
    }

    [Fact]
    public void Only_the_best_scores_are_kept()
    {
        var (server, _) = Make(new QservOptions { ScoresToKeep = 3 });
        foreach (var score in new[] { 10, 50, 30, 20, 40 })
            server.Handle($"postdemo\nscore {score}\nname P{score}\n", "1.1.1.1");

        var names = QservReply.Parse(server.Handle("gethighscores\nnum 10\n", "x")).Body.Nodes().Select(n => n.Value.Get("name"));
        Assert.Equal(["P50", "P40", "P30"], names);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("12x")]
    [InlineData("../etc")]
    [InlineData("")]
    public void A_score_that_is_not_a_number_is_refused(string score)
    {
        var (server, _) = Make();

        Assert.Throws<QservException>(() => server.Handle($"postdemo\nscore {score}\n", "1.1.1.1"));
    }

    [Fact]
    public void An_oversize_recording_is_refused()
    {
        var (server, _) = Make(new QservOptions { MaxRecLength = 10 });

        Assert.Throws<QservException>(() => server.Handle("postdemo\nscore 5\nrec " + new string('A', 11) + "\n", "1.1.1.1"));
    }

    [Fact]
    public void A_postdemo_without_a_score_only_reads_the_table()
    {
        var (server, _) = Make();

        Assert.Equal("Ok\n", server.Handle("postdemo\nname Nobody\n", "1.1.1.1"));
    }

    [Fact]
    public void Scores_survive_a_restart()
    {
        var file = Path.Combine(Path.GetTempPath(), $"qserv-{Guid.NewGuid():N}.json");
        try
        {
            var options = new QservOptions { ScoresFile = file };
            new QservServer(options).Handle("postdemo\nscore 777\nname Kept\nrec QUJD\n", "1.1.1.1");

            var reply = QservReply.Parse(new QservServer(options).Handle("gethighscores\n", "x"));

            Assert.Equal("Kept", reply.Body.Get("high000/name"));
            Assert.Equal("QUJD", reply.Body.Get("high000/rec"));
        }
        finally
        {
            File.Delete(file);
        }
    }
}

public class GameAnnouncementTests
{
    private static GameAnnouncement Sample() => new()
    {
        Name = "Friday night",
        Version = 20,
        Addresses = ["10.0.0.5", "192.168.1.9"],
        Port = 3456,
        Started = true,
        RunningTime = 1234,
        RoundNumber = 2,
        Survivor = true,
        RulesType = 1,
        NormalAttack = new Attack(AttackType.Blind, 7),
        LevelStart = 3,
        LevelUp = false,
        MinCombo = 2,
        AllowHandicap = false,
        EndType = GameEnd.Points,
        EndValue = 5000,
        Players =
        [
            new AnnouncedPlayer("Ann", 2, 1, 0) { Frags = 3, Deaths = 1, Lines = 40, Score = 9000, PlayingTime = 500 },
            new AnnouncedPlayer("Bob", 5, 0, 2) { Frags = 1, Deaths = 3, Lines = 22, Score = 4000 },
        ],
    };

    [Fact]
    public void An_announcement_survives_a_trip_through_the_wire_format()
    {
        var read = GameAnnouncement.FromTree(QservTree.Parse(Sample().ToTree().Format().Split('\n')));

        Assert.Equal("Friday night", read.Name);
        Assert.Equal(20, read.Version);
        Assert.Equal(["10.0.0.5", "192.168.1.9"], read.Addresses);
        Assert.True(read.Started);
        Assert.True(read.Survivor);
        Assert.False(read.LevelUp);
        Assert.False(read.AllowHandicap);
        Assert.Equal(3, read.LevelStart);
        Assert.Equal(new Attack(AttackType.Blind, 7), read.NormalAttack);
        Assert.Equal(GameEnd.Points, read.EndType);
        Assert.Equal(5000, read.EndValue);
        Assert.Equal(2, read.Players.Count);
        Assert.Equal(new AnnouncedPlayer("Bob", 5, 0, 2) { Frags = 1, Deaths = 3, Lines = 22, Score = 4000 }, read.Players[1]);
    }

    [Fact]
    public void Player_figures_are_only_sent_once_the_game_has_started()
    {
        var game = Sample();
        game.Started = false;

        var text = game.ToTree().Format();

        Assert.DoesNotContain("players/0/score", text);
        Assert.Contains("players/0/name Ann", text);
    }

    [Fact]
    public void Potato_attacks_are_only_sent_in_a_hot_potato_game()
    {
        var game = Sample();
        Assert.DoesNotContain("potato_", game.ToTree().Format());

        game.HotPotato = true;
        Assert.Contains("rules/attacks/potato_normal 0 0", game.ToTree().Format());
    }

    [Fact]
    public void Out_of_range_values_fall_back_like_the_original_list()
    {
        var tree = QservTree.Parse(["players/0/name Ann", "players/0/team 9", "players/0/status 7", "players/0/handicap 12", "end/type 99", "rules/attacks/normal 9 3"]);

        var read = GameAnnouncement.FromTree(tree);

        Assert.Equal(new AnnouncedPlayer("Ann", 0, -1, 0), read.Players[0] with { Frags = 0 });
        Assert.Equal(GameEnd.Lines, read.EndType);
        Assert.Equal(new Attack(AttackType.Lines, 3), read.NormalAttack);
    }

    [Fact]
    public void A_bare_game_reads_with_the_default_rules()
    {
        var read = GameAnnouncement.FromTree(QservTree.Parse(["name Bare"]));

        Assert.Equal(3456, read.Port);
        Assert.True(read.LevelUp);
        Assert.True(read.AllowHandicap);
        Assert.Equal(1, read.LevelStart);
        Assert.Empty(read.Players);
    }
}

public sealed class QservEndToEndTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private QservClient _client = null!;

    public async Task InitializeAsync()
    {
        _app = QservHost.Create(["--urls", "http://127.0.0.1:0", "--logging:loglevel:default", "Warning"]);
        await _app.StartAsync();
        var address = _app.Urls.First();
        _http = new HttpClient();
        _client = new QservClient(_http, new Uri(address + "/cgi-bin/qserv.pl"));
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task A_game_is_announced_listed_and_withdrawn_over_http()
    {
        var game = new GameAnnouncement
        {
            Name = "Over the wire",
            Version = 20,
            Port = 4567,
            Players = [new AnnouncedPlayer("Ann", 1, 0, 0)],
        };

        Assert.True(await _client.PostGameAsync(game));

        var listed = Assert.Single(await _client.GetGamesAsync());
        Assert.Equal("127.0.0.1", listed.Host);
        Assert.Equal("Over the wire", listed.Info.Name);
        Assert.Equal(4567, listed.Info.Port);
        Assert.Equal("Ann", Assert.Single(listed.Info.Players).Name);

        Assert.True(await _client.DeleteGameAsync(4567));
        Assert.Empty(await _client.GetGamesAsync());
    }

    [Fact]
    public async Task Scores_with_recordings_round_trip_over_http()
    {
        var recording = Enumerable.Range(0, 3000).Select(i => (byte)(i * 31)).ToArray();

        var table = await _client.PostDemoAsync("Ann", 4200, 31, 3, recording, 5);

        var best = Assert.Single(table);
        Assert.Equal(("Ann", 4200, 31, 3), (best.Name, best.Score, best.Lines, best.Level));
        Assert.Equal(recording, Convert.FromBase64String(best.Recording!));
        Assert.Equal(4200, Assert.Single(await _client.GetHighScoresAsync(5)).Score);
    }

    [Fact]
    public async Task A_plain_get_gets_the_greeting_and_a_bad_score_gets_a_400()
    {
        var url = _app.Urls.First() + "/cgi-bin/qserv.pl";

        Assert.Equal(QservServer.Greeting, await _http.GetStringAsync(url));

        var response = await _http.PostAsync(url, new FormUrlEncodedContent([KeyValuePair.Create("data", "postdemo\nscore nope\n")]));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}

public sealed class HostAnnouncerTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private QservClient _client = null!;

    public async Task InitializeAsync()
    {
        _app = QservHost.Create(["--urls", "http://127.0.0.1:0", "--logging:loglevel:default", "Warning"]);
        await _app.StartAsync();
        _http = new HttpClient();
        _client = new QservClient(_http, new Uri(_app.Urls.First() + "/cgi-bin/qserv.pl"));
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task A_hosted_game_is_listed_with_its_players_kept_up_to_date_and_withdrawn_at_the_end()
    {
        var host = new GameHost(new HostRules { Name = "Announced", Survivor = true, ComboMin = 2 });
        var sink = new TestSink();
        var peer = host.Connect(sink);
        host.Receive(peer, new WantJoinPacket { Id = (byte)PacketType.WantJoin, NetVersion = 20 });
        host.Receive(peer, new PlayerWantJoinPacket { Id = (byte)PacketType.PlayerWantJoin, Name = "Ann", Team = 3 });

        await using (var announcer = new HostAnnouncer(host, _client, 3456, TimeSpan.FromMilliseconds(30)))
        {
            ListedGame? listed = null;
            for (int i = 0; i < 200 && listed is null; i++)
            {
                listed = (await _client.GetGamesAsync()).FirstOrDefault();
                await Task.Delay(10);
            }

            Assert.NotNull(listed);
            Assert.Equal("Announced", listed.Info.Name);
            Assert.True(listed.Info.Survivor);
            Assert.Equal(2, listed.Info.MinCombo);
            Assert.False(listed.Info.Started);
            Assert.Equal("Ann", Assert.Single(listed.Info.Players).Name);

            var peer2 = host.Connect(new TestSink());
            host.Receive(peer2, new WantJoinPacket { Id = (byte)PacketType.WantJoin, NetVersion = 20 });
            host.Receive(peer2, new PlayerWantJoinPacket { Id = (byte)PacketType.PlayerWantJoin, Name = "Bob", Team = 1, PlayerHash = [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] });

            for (int i = 0; i < 200 && (await _client.GetGamesAsync()).FirstOrDefault()?.Info.Players.Count != 2; i++)
                await Task.Delay(10);
            Assert.Equal(["Ann", "Bob"], (await _client.GetGamesAsync()).Single().Info.Players.Select(p => p.Name));
            Assert.Null(announcer.Failed);
            Assert.True(announcer.Posted >= 2);
        }

        Assert.Empty(await _client.GetGamesAsync());
    }

    [Fact]
    public async Task An_unreachable_list_server_is_reported_and_does_not_stop_the_host()
    {
        var host = new GameHost(new HostRules());
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var client = new QservClient(http, new Uri("http://127.0.0.1:1/cgi-bin/qserv.pl"));

        await using var announcer = new HostAnnouncer(host, client, 3456, TimeSpan.FromMilliseconds(30));
        for (int i = 0; i < 200 && announcer.Failed is null; i++)
            await Task.Delay(10);

        Assert.NotNull(announcer.Failed);
        Assert.Equal(0, announcer.Posted);
    }
}
