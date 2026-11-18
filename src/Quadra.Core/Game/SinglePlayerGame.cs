using Quadra.Core.Input;
using Quadra.Core.Rules;

namespace Quadra.Core.Game;

public enum GamePhase
{
    /// <summary>A piece is in play and the keys control it.</summary>
    Falling,

    /// <summary>The piece has landed; clearing and the pause before the next piece take a few frames.</summary>
    Settling,

    /// <summary>A new piece appeared on top of blocks. Nothing moves any more.</summary>
    GameOver,
}

/// <summary>
/// A single-player game on the 1.1 rules, advanced one logic frame at a time. It owns the loop the C++
/// client spreads over its executors: play the piece while keys are down, stamp it when it lands,
/// wait while lines clear, then bring in the next piece or end the game.
/// A front end only feeds <see cref="Keys"/> and calls <see cref="Update"/> every 10 ms
/// (<see cref="FramesPerSecond"/>), then draws <see cref="Player"/>.
/// </summary>
public sealed class SinglePlayerGame
{
    /// <summary>The original steps its logic every 10 ms.</summary>
    public const int FramesPerSecond = 100;

    /// <summary>Frames between a landing and the next piece, as in the original (get-next and init frames).</summary>
    public const int PauseFrames = 2;

    /// <summary>
    /// Extra frames while cleared lines flash. The original flashes for about 17 frames and then lets
    /// loose cells fall one row every other frame; here the fall is folded into one fixed delay.
    /// </summary>
    public const int ClearFrames = 20;

    private int _settleFramesLeft;

    public SinglePlayerGame(int seed, KeyStates keys, KeyBindings bindings, int horizontalRepeat = 2, int verticalRepeat = 2)
    {
        Keys = keys;
        Player = new PlayerSim(seed, 1, verticalRepeat, levelUp: true);
        Player.AttachInput(keys, bindings, horizontalRepeat);
    }

    public KeyStates Keys { get; }
    public PlayerSim Player { get; }
    public GamePhase Phase { get; private set; } = GamePhase.Falling;

    /// <summary>Frames the game has been advanced (including the pauses).</summary>
    public int Frame { get; private set; }

    /// <summary>The player's score, lines and level are read from <see cref="Player"/>.</summary>
    public bool IsOver => Phase == GamePhase.GameOver;

    /// <summary>Advances one logic frame.</summary>
    public void Update()
    {
        if (Phase == GamePhase.GameOver)
            return;
        Frame++;

        if (Phase == GamePhase.Settling)
        {
            if (--_settleFramesLeft > 0)
                return;
            Phase = GamePhase.Falling;
            return;
        }

        var result = Player.Frame();
        if (result.Landed is not { } landed)
            return;

        int drop = LegacyScoring.DropPoints(Player.Falling.Frames);
        var stamp = Player.Stamp(landed.X, landed.Y, landed.Rotation, drop);

        if (Player.SpawnBlocked)
        {
            Player.Die();
            Phase = GamePhase.GameOver;
            return;
        }

        _settleFramesLeft = PauseFrames + (stamp.Lines > 0 ? ClearFrames : 0);
        Phase = GamePhase.Settling;
    }
}
