using Quadra.Core.Rules;

namespace Quadra.Core.Input;

/// <summary>What one frame of input did.</summary>
/// <param name="Moves">The actions attempted this frame, as stored in a <c>Moves</c> packet byte.</param>
/// <param name="Landed">Set when the piece came to rest this frame.</param>
public readonly record struct FrameResult(MoveFlags Moves, Placement? Landed);

/// <summary>The controller's repeat counters, so a frame can be tried and undone.</summary>
public readonly record struct ControllerState(int HoldLeft, int HoldRight);

/// <summary>
/// Turns key states into piece actions, one frame at a time. A port of
/// <c>Player_process_key::keyboard_control</c> and the gravity step that follows it
/// (<c>legacy/source/player.cc</c>), without the wall kick (see <see cref="FallingPiece"/>) and without
/// the throttle that skips input when the game runs faster than the display.
///
/// How keys behave:
/// <list type="bullet">
/// <item>Rotations happen when the key is <em>released</em>.</item>
/// <item>Down acts every frame while held, or once for a tap.</item>
/// <item>Drop acts once on key-down.</item>
/// <item>Left and Right act at once, then repeat after <c>delay + 10</c> frames and every
/// <c>delay</c> frames after that. Pressing both cancels both. The repeat starts over for every
/// new piece. The current C++ client retries a blocked move on the next frame
/// (<c>retryBlockedMoves</c>); the 1.1.0 clients that recorded the bundled demos did not, and neither
/// does this by default.</item>
/// </list>
/// Frames that do not belong to the piece (the pause before it appears, the line-clear animation) are
/// the game loop's business: call <see cref="Frame"/> only for frames where the piece is in play.
/// </summary>
public sealed class PieceController
{
    private readonly FallingPiece _piece;
    private readonly KeyStates _keys;
    private readonly KeyBindings _bindings;
    private readonly int _repeatDelay;
    private readonly bool _continuousDown;
    private readonly bool _retryBlockedMoves;
    private int _holdLeft;
    private int _holdRight;

    /// <param name="repeatDelay">Frames between repeats, from <see cref="LegacySpeeds.RepeatDelay"/>.</param>
    /// <param name="continuousDown">When false, a down key still held from the previous piece is ignored for the new one.</param>
    /// <param name="retryBlockedMoves">When true, a blocked left or right move is tried again on the next frame (current C++ client).</param>
    public PieceController(FallingPiece piece, KeyStates keys, KeyBindings bindings, int repeatDelay,
        bool continuousDown = true, bool retryBlockedMoves = false)
    {
        _piece = piece;
        _keys = keys;
        _bindings = bindings;
        _repeatDelay = repeatDelay;
        _continuousDown = continuousDown;
        _retryBlockedMoves = retryBlockedMoves;
    }

    /// <summary>Frames until a held Left key repeats (0 when it is not being held).</summary>
    public int HoldLeft => _holdLeft;

    /// <summary>Frames until a held Right key repeats (0 when it is not being held).</summary>
    public int HoldRight => _holdRight;

    public ControllerState Save() => new(_holdLeft, _holdRight);

    public void Restore(ControllerState state)
    {
        _holdLeft = state.HoldLeft;
        _holdRight = state.HoldRight;
    }

    /// <summary>Call when a new piece appears: drops a stale drop key and restarts the key repeat.</summary>
    public void BeginPiece()
    {
        _keys.Clear(_bindings[GameAction.Drop]);
        if (!_continuousDown)
            _keys.Clear(_bindings[GameAction.Down]);
        _holdLeft = 0;
        _holdRight = 0;
    }

    /// <summary>Plays one frame: reads the keys, moves the piece, then lets gravity act.</summary>
    public FrameResult Frame()
    {
        var moves = MoveFlags.None;

        if (Has(GameAction.RotateLeft, KeyFlags.Released))
        {
            _piece.RotateLeft();
            moves |= MoveFlags.RotateLeft;
            _keys.Clear(_bindings[GameAction.RotateLeft]);
        }
        if (Has(GameAction.RotateRight, KeyFlags.Released))
        {
            _piece.RotateRight();
            moves |= MoveFlags.RotateRight;
            _keys.Clear(_bindings[GameAction.RotateRight]);
        }
        if (Has(GameAction.RotateTwice, KeyFlags.Released))
        {
            _piece.RotateTwice();
            moves |= MoveFlags.RotateTwice;
            _keys.Clear(_bindings[GameAction.RotateTwice]);
        }
        if (Has(GameAction.Down, KeyFlags.Pressed | KeyFlags.Released))
        {
            _piece.MoveDown();
            moves |= MoveFlags.Down;
            _keys.Unrelease(_bindings[GameAction.Down]);
        }
        if (Has(GameAction.Drop, KeyFlags.Pressed))
        {
            _piece.HardDrop();
            moves |= MoveFlags.Drop;
            _keys.Clear(_bindings[GameAction.Drop]);
        }

        bool left = false, right = false;
        if (Has(GameAction.Left, KeyFlags.Pressed | KeyFlags.Released))
        {
            left = true;
            _keys.Unrelease(_bindings[GameAction.Left]);
        }
        if (Has(GameAction.Right, KeyFlags.Pressed | KeyFlags.Released))
        {
            right = true;
            _keys.Unrelease(_bindings[GameAction.Right]);
        }

        if (left && !right)
        {
            if (Repeat(ref _holdLeft, _piece.MoveLeft))
                moves |= MoveFlags.Left;
        }
        else
        {
            _holdLeft = 0;
        }

        if (right && !left)
        {
            if (Repeat(ref _holdRight, _piece.MoveRight))
                moves |= MoveFlags.Right;
        }
        else
        {
            _holdRight = 0;
        }

        return new FrameResult(moves, _piece.Gravity());
    }

    private bool Has(GameAction action, KeyFlags any) => (_keys.Get(_bindings[action]) & any) != 0;

    /// <summary>
    /// One frame of a held direction key. Returns true if a move was attempted this frame.
    /// </summary>
    private bool Repeat(ref int hold, Func<bool> move)
    {
        if (hold >= 2)
        {
            hold--;
            return false;
        }

        hold = hold == 0 ? _repeatDelay + 10 : _repeatDelay; // longer wait before the first repeat
        if (!move() && _retryBlockedMoves)
            hold = 1; // blocked: try again next frame
        return true;
    }
}
