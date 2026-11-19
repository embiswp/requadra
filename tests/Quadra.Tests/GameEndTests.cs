using Quadra.Core.Game;
using Quadra.Core.Input;
using Quadra.Core.Net;

namespace Quadra.Tests;

public class GameEndTests
{
    private static (LocalNetwork Net, GameClient A, GameClient B, byte SlotA, byte SlotB) Start(HostRules rules)
    {
        var net = new LocalNetwork(rules);
        var a = net.AddClient();
        var b = net.AddClient();
        byte sa = net.AddPlayer(a, "Ann", 1);
        byte sb = net.AddPlayer(b, "Bob", 2);
        a.RequestPause();
        net.Settle();
        for (int i = 0; i < GameHost.WaitingForStart; i++)
        {
            net.Host.Tick();
            foreach (var c in net.Clients)
                c.Tick();
        }
        net.Settle();
        return (net, a, b, sa, sb);
    }

    /// <summary>Ann frags Bob: Bob's board has her garbage on it when he dies.</summary>
    private static void Frag(LocalNetwork net, GameClient victim, byte victimSlot, byte attackerSlot)
    {
        // The garbage that made the kill, as every machine has taken it in.
        net.Host.State.Players[victimSlot].Sim.ReceiveLines(attackerSlot, 2, 1, 5);
        foreach (var c in net.Clients)
            c.State!.Players[victimSlot].Sim.ReceiveLines(attackerSlot, 2, 1, 5);
        victim.Die(victimSlot);
        net.Settle();
    }

    [Fact]
    public void A_frag_game_ends_when_a_team_has_the_goal_and_everyone_is_told()
    {
        var (net, a, b, sa, sb) = Start(new HostRules { GameEnd = GameEnd.Frag, GameEndValue = 2 });

        Frag(net, b, sb, sa);
        net.Host.Tick();
        Assert.False(net.Host.Terminated);

        Frag(net, b, sb, sa);
        net.Host.Tick();
        net.Settle();

        Assert.True(net.Host.Terminated);
        Assert.Equal(1, net.Host.WinningTeam);
        Assert.True(a.Terminated);
        Assert.True(b.Terminated);
        Assert.True(net.Host.Announcement(3456).Terminated);
    }

    [Fact]
    public void Gameplay_after_the_end_no_longer_counts()
    {
        var (net, _, b, sa, sb) = Start(new HostRules { GameEnd = GameEnd.Frag, GameEndValue = 1 });
        Frag(net, b, sb, sa);
        net.Host.Tick();
        Assert.True(net.Host.Terminated);

        int deaths = net.Host.State.Players[sb].Sim.Deaths;
        b.Respawn(sb);
        b.Die(sb);
        net.Settle();
        Assert.Equal(deaths, net.Host.State.Players[sb].Sim.Deaths);
    }

    [Fact]
    public void A_game_level_at_the_goal_keeps_going_until_someone_is_ahead()
    {
        var (net, a, b, sa, sb) = Start(new HostRules { GameEnd = GameEnd.Frag, GameEndValue = 1 });

        Frag(net, b, sb, sa);
        Frag(net, a, sa, sb);          // one frag each, before the host looked
        net.Host.Tick();
        Assert.False(net.Host.Terminated);

        Frag(net, b, sb, sa);
        net.Host.Tick();
        Assert.True(net.Host.Terminated);
        Assert.Equal(1, net.Host.WinningTeam);
    }

    [Fact]
    public void A_timed_game_ends_when_the_minutes_are_up_if_someone_is_ahead()
    {
        var (net, _, b, sa, sb) = Start(new HostRules { GameEnd = GameEnd.Time, GameEndValue = 1 });
        Frag(net, b, sb, sa);

        for (int i = 0; i < GameGoals.FramesPerMinute - 10; i++)
            net.Host.Tick();
        Assert.False(net.Host.Terminated);
        for (int i = 0; i < 15; i++)
            net.Host.Tick();

        Assert.True(net.Host.Terminated);
        Assert.Equal(1, net.Host.WinningTeam);
    }

    [Fact]
    public void A_timed_game_with_nobody_ahead_does_not_end()
    {
        var (net, _, _, _, _) = Start(new HostRules { GameEnd = GameEnd.Time, GameEndValue = 1 });
        for (int i = 0; i < GameGoals.FramesPerMinute + 10; i++)
            net.Host.Tick();
        Assert.False(net.Host.Terminated);
    }

    [Fact]
    public void A_game_without_an_end_condition_never_ends()
    {
        var (net, _, b, sa, sb) = Start(new HostRules());
        Frag(net, b, sb, sa);
        for (int i = 0; i < 100; i++)
            net.Host.Tick();
        Assert.False(net.Host.Terminated);
    }

    [Fact]
    public void Points_are_counted_in_thousands_and_lines_as_they_are()
    {
        Assert.Equal(5000, GameGoals.Goal(GameEnd.Points, 5));
        Assert.Equal(5, GameGoals.Goal(GameEnd.Lines, 5));
        Assert.Equal(5, GameGoals.Goal(GameEnd.Frag, 5));

        var totals = new Dictionary<int, int> { [0] = 3, [1] = 3, [2] = 1 };
        Assert.Equal(0, GameGoals.Leader(totals, out bool draw));
        Assert.True(draw);
        Assert.Equal(-1, GameGoals.Leader(new Dictionary<int, int> { [0] = 0 }, out draw));
        Assert.False(draw);
    }

    [Fact]
    public void A_player_sees_that_the_game_is_over_stops_playing_and_gets_the_result()
    {
        var (net, a, b, sa, sb) = Start(new HostRules { GameEnd = GameEnd.Frag, GameEndValue = 1 });
        var keys = new KeyStates();
        var game = new NetworkGame(b, sb, keys, new KeyBindings(1, 2, 3, 4, 5, 6, 7));

        Frag(net, b, sb, sa);
        net.Host.Tick();
        net.Settle();
        game.Update();

        Assert.True(game.Ended);
        Assert.Equal("Team 2 (Ann) wins", game.Result());
        int stamps = game.Player.Stamps;
        keys.Down(7);
        for (int i = 0; i < 300; i++)
            game.Update();
        Assert.Equal(stamps, game.Player.Stamps);
    }
}
