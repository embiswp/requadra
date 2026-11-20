using Microsoft.Xna.Framework.Input;
using Quadra.Core.Game;
using Quadra.Core.Input;
using Quadra.Core.Net;
using Quadra.Core.Settings;

namespace Quadra.Client;

/// <summary>The current player's keys: the bindings the game uses, and the key states it reads, fed from the keyboard.</summary>
internal sealed class PlayerControls
{
    private Keys[] _gameKeys = [];
    private PlayerSettings _player = new();

    /// <summary>What the game reads; key presses and releases are reported to it by <see cref="Feed"/>.</summary>
    public KeyStates State { get; } = new();

    /// <summary>Takes the keys of <paramref name="player"/>, forgets all key states, and returns the bindings for a game.</summary>
    public KeyBindings Reset(PlayerSettings player)
    {
        _player = player;
        _gameKeys = Enum.GetValues<GameAction>().Select(KeyFor).Distinct().ToArray();
        State.ClearAll();   // the same object stays: games hold on to it
        return new KeyBindings(
            (int)KeyFor(GameAction.Left), (int)KeyFor(GameAction.Right), (int)KeyFor(GameAction.RotateLeft),
            (int)KeyFor(GameAction.Down), (int)KeyFor(GameAction.RotateRight), (int)KeyFor(GameAction.RotateTwice),
            (int)KeyFor(GameAction.Drop));
    }

    /// <summary>The game the host's place for <paramref name="player"/> is played with: reading this player's keys.</summary>
    public NetworkGame CreateNetworkGame(GameClient client, byte slot, PlayerSettings player)
    {
        var bindings = Reset(player);
        return new NetworkGame(client, slot, State, bindings, player.HorizontalSpeed, player.ContinuousDown);
    }

    public Keys KeyFor(GameAction action)
    {
        var defaults = PlayerSettings.DefaultKeys();
        return Enum.TryParse<Keys>(_player.Keys.GetValueOrDefault(action, defaults[action]), out var key)
            ? key
            : Enum.Parse<Keys>(defaults[action]);
    }

    /// <summary>Reports key-down and key-up of the game's keys as events, the way an input layer should.</summary>
    public void Feed(KeyboardState now, KeyboardState before)
    {
        foreach (var key in _gameKeys)
        {
            bool down = now.IsKeyDown(key), wasDown = before.IsKeyDown(key);
            if (down && !wasDown)
                State.Down((int)key);
            else if (!down && wasDown)
                State.Up((int)key);
        }
    }

    /// <summary>Presses and releases a key at once, as a smoke run does with nobody at the keyboard.</summary>
    public void Tap(GameAction action)
    {
        int key = (int)KeyFor(action);
        State.Down(key);
        State.Up(key);
    }
}
