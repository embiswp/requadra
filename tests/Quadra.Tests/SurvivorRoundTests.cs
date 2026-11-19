using Quadra.Core.Game;
using Quadra.Core.Input;
using Quadra.Core.Net;

namespace Quadra.Tests;

public class SurvivorRoundTests
{
    private static (LocalNetwork Net, NetworkGame A, NetworkGame B, byte SlotA, byte SlotB) Start(HostRules rules)
    {
        var net = new LocalNetwork(rules with { Survivor = true });
        var a = net.AddClient();
        var b = net.AddClient();
        byte sa = net.AddPlayer(a, "Ann", 1);
        byte sb = net.AddPlayer(b, "Bob", 2);
        a.RequestPause();
        net.Settle();
        var ga = new NetworkGame(a, sa, new KeyStates(), new KeyBindings(1, 2, 3, 4, 5, 6, 7));
        var gb = new NetworkGame(b, sb, new KeyStates(), new KeyBindings(1, 2, 3, 4, 5, 6, 7));
        var game = (net, ga, gb, sa, sb);
        for (int i = 0; i < GameHost.WaitingForStart + 5; i++)
            Frame(net, ga, gb);
        return game;
    }

    private static void Frame(LocalNetwork net, params NetworkGame[] games)
    {
        net.Host.Tick();
        net.Settle();
        foreach (var g in games)
            g.Update();
        net.Settle();
    }

    private static void Run(LocalNetwork net, NetworkGame a, NetworkGame b, int frames)
    {
        for (int i = 0; i < frames; i++)
            Frame(net, a, b);
    }

    [Fact]
    public void A_round_runs_with_both_players_alive_and_no_round_change()
    {
        var (net, a, b, _, _) = Start(new HostRules());
        Run(net, a, b, 50);
        Assert.Equal(RoundState.Last, net.Host.SyncPoint);
        Assert.Equal(0, net.Host.Rounds);
        Assert.False(a.IsDead);
        Assert.False(b.IsDead);
    }

    [Fact]
    public void When_one_team_is_left_everyone_restarts_together_with_a_new_seed()
    {
        var (net, a, b, sa, sb) = Start(new HostRules());
        uint[] seeds = [0xABCDEu];
        net.Host.NewSeed = () => seeds[0];

        Run(net, a, b, 5);          // both alive: the round counts as competitive
        net.Clients[1].Die(sb);
        Run(net, a, b, 2);
        Assert.Equal(RoundState.WaitForWinner, net.Host.SyncPoint);
        Assert.Equal(1, net.Host.Rounds);

        // The winner steps aside, both wait, the host sends the seed and starts the next round.
        for (int i = 0; i < 60 && !(a.Local.Alive && b.Local.Alive && net.Host.SyncPoint == RoundState.Last); i++)
            Frame(net, a, b);

        Assert.Equal(RoundState.Last, net.Host.SyncPoint);
        Assert.True(a.Local.Alive && b.Local.Alive);
        Assert.Equal(RoundState.Playing, a.Local.Round);
        Assert.Equal(RoundState.Playing, b.Local.Round);
        // Both boards draw the same new sequence on every machine.
        Assert.Equal(net.Host.State.Players[sa].Sim.CurrentPiece, a.Local.Sim.CurrentPiece);
        Assert.Equal(net.Host.State.Players[sb].Sim.CurrentPiece, b.Local.Sim.CurrentPiece);
        Assert.Equal(a.Local.Sim.CurrentPiece, b.Local.Sim.CurrentPiece);
    }

    [Fact]
    public void A_one_team_game_just_keeps_playing()
    {
        var net = new LocalNetwork(new HostRules { Survivor = true });
        var c = net.AddClient();
        byte slot = net.AddPlayer(c, "Ann", 1);
        c.RequestPause();
        net.Settle();
        var g = new NetworkGame(c, slot, new KeyStates(), new KeyBindings(1, 2, 3, 4, 5, 6, 7));
        for (int i = 0; i < GameHost.WaitingForStart + 5; i++)
        {
            net.Host.Tick();
            net.Settle();
            g.Update();
        }
        Assert.Equal(0, net.Host.Rounds);
        Assert.Equal(RoundState.Last, net.Host.SyncPoint);
    }

    [Fact]
    public void A_survivor_game_ends_only_when_a_round_does()
    {
        var (net, a, b, sa, sb) = Start(new HostRules { GameEnd = GameEnd.Frag, GameEndValue = 1 });
        Run(net, a, b, 5);
        // Ann frags Bob.
        foreach (var s in new[] { net.Host.State }.Concat(net.Clients.Select(c => c.State!)))
            s.Players[sb].Sim.ReceiveLines(sa, 2, 1, 5);
        net.Clients[1].Die(sb);
        net.Settle();
        net.Host.Tick();
        Assert.False(net.Host.Terminated);       // Ann is still playing: the round is not over yet
        Run(net, a, b, 10);
        Assert.True(net.Host.Terminated);
        Assert.Equal(net.Host.State.Players[sa].Team, net.Host.WinningTeam);
    }
}
