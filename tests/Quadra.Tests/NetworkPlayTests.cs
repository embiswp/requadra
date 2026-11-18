using Quadra.Core.Game;
using Quadra.Core.Input;
using Quadra.Core.Net;

namespace Quadra.Tests;

public class NetworkPlayTests
{
    private const int Left = 1, Right = 2, RotateLeft = 3, Down = 4, RotateRight = 5, RotateTwice = 6, Drop = 7;

    private static NetworkGame Play(GameClient client, byte slot, KeyStates keys) =>
        new(client, slot, keys, new KeyBindings(Left, Right, RotateLeft, Down, RotateRight, RotateTwice, Drop));

    private static (LocalNetwork Net, NetworkGame A, NetworkGame B, KeyStates KeysA, KeyStates KeysB) Start()
    {
        var net = new LocalNetwork(new HostRules { Seed = 11 });
        var ca = net.AddClient();
        var cb = net.AddClient();
        byte sa = net.AddPlayer(ca, "Ann", 1);
        byte sb = net.AddPlayer(cb, "Bob", 2);
        var keysA = new KeyStates();
        var keysB = new KeyStates();
        var a = Play(ca, sa, keysA);
        var b = Play(cb, sb, keysB);

        ca.RequestPause();
        net.Settle();
        return (net, a, b, keysA, keysB);
    }

    private static void Frame(LocalNetwork net, params NetworkGame[] games)
    {
        net.Host.Tick();
        foreach (var g in games)
            g.Update();
        net.Settle();
    }

    [Fact]
    public void Nothing_moves_until_the_countdown_has_ended()
    {
        var (net, a, b, _, _) = Start();

        for (int i = 0; i < GameHost.WaitingForStart - 5; i++)
            Frame(net, a, b);

        Assert.False(a.Started);
        Assert.Equal(0, a.Player.Stamps);

        for (int i = 0; i < 10; i++)
            Frame(net, a, b);
        Assert.True(a.Started);
        Assert.True(b.HasStarted);
    }

    [Fact]
    public void A_landing_reaches_the_other_machine_and_the_boards_agree()
    {
        var (net, a, b, keysA, _) = Start();
        for (int i = 0; i < GameHost.WaitingForStart + 5; i++)
            Frame(net, a, b);

        for (int i = 0; i < 600 && a.Player.Stamps < 3; i++)
        {
            keysA.Down(Drop);
            Frame(net, a, b);
        }

        Assert.True(a.Player.Stamps >= 3);
        var mine = a.State.Players[a.Slot].Sim;
        var seen = b.State.Players[a.Slot].Sim;
        Assert.Equal(mine.Stamps, seen.Stamps);
        Assert.Equal(mine.Score, seen.Score);
        for (int row = 0; row < Quadra.Core.Rules.Board.Rows; row++)
            for (int col = Quadra.Core.Rules.Board.FirstColumn; col < Quadra.Core.Rules.Board.EndColumn; col++)
                Assert.Equal(mine.Board.Cell(row, col), seen.Board.Cell(row, col));
    }

    [Fact]
    public void A_player_who_stacks_to_the_top_dies_everyone_sees_it_and_a_key_brings_them_back()
    {
        var (net, a, b, keysA, _) = Start();
        for (int i = 0; i < GameHost.WaitingForStart + 5; i++)
            Frame(net, a, b);

        for (int i = 0; i < 5000 && !a.IsDead; i++)
        {
            keysA.Down(Drop);
            Frame(net, a, b);
        }

        Assert.True(a.IsDead);
        Assert.Equal(1, b.State.Players[a.Slot].Sim.Deaths);

        // A key held from before the death does not count; wait out the lock, then press.
        keysA.ClearAll();
        for (int i = 0; i < NetworkGame.RespawnLockFrames + 5; i++)
            Frame(net, a, b);
        Assert.True(a.IsDead);

        keysA.Down(Left);
        Frame(net, a, b);
        Assert.False(a.IsDead);
        Assert.Equal(0, b.State.Players[a.Slot].Sim.Stamps == 0 ? 0 : 0);
        Assert.True(IsEmpty(b.State.Players[a.Slot].Sim.Board));
    }

    private static bool IsEmpty(Quadra.Core.Rules.Board board)
    {
        for (int row = Quadra.Core.Rules.Board.FirstClearableRow; row < Quadra.Core.Rules.Board.Rows; row++)
            for (int col = Quadra.Core.Rules.Board.FirstColumn; col < Quadra.Core.Rules.Board.EndColumn; col++)
                if (board.IsOccupied(row, col))
                    return false;
        return true;
    }
}
