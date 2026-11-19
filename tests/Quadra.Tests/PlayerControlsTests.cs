using Microsoft.Xna.Framework.Input;
using Quadra.Core.Game;
using Quadra.Core.Net;
using Quadra.Core.Settings;
using Quadra.Desktop;

namespace Quadra.Tests;

/// <summary>The desktop keyboard wiring: what the window feeds from the keyboard must reach the game being played.</summary>
public class PlayerControlsTests
{
    [Fact]
    public void Resetting_the_controls_keeps_the_key_states_that_games_hold()
    {
        var controls = new PlayerControls();
        var held = controls.State;
        controls.Reset(new PlayerSettings());

        controls.Feed(new KeyboardState(Keys.Left), new KeyboardState());

        Assert.Same(held, controls.State);
        Assert.Equal(Quadra.Core.Input.KeyFlags.Pressed, held.Get((int)Keys.Left));
    }

    [Fact]
    public void A_key_pressed_on_the_keyboard_moves_the_piece_in_a_network_game()
    {
        var controls = new PlayerControls();
        var net = new LocalNetwork(new HostRules { Seed = 11 });
        var client = net.AddClient();
        byte slot = net.AddPlayer(client, "Ann", 1);
        var game = controls.CreateNetworkGame(client, slot, new PlayerSettings());
        client.RequestPause();
        net.Settle();

        void Frame()
        {
            net.Host.Tick();
            game.Update();
            net.Settle();
        }

        for (int i = 0; i < GameHost.WaitingForStart + 5; i++)
            Frame();
        Assert.True(game.Started);

        int before = game.Player.Falling.X;
        controls.Feed(new KeyboardState(Keys.Left), new KeyboardState());
        for (int i = 0; i < 3; i++)
            Frame();

        Assert.True(game.Player.Falling.X < before, $"the piece stayed at {before}");
    }
}
