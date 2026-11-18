namespace Quadra.Core.Input;

/// <summary>The seven things a player can do to a falling piece, in the game's own order.</summary>
public enum GameAction
{
    Left,
    Right,
    RotateLeft,
    Down,
    RotateRight,
    RotateTwice,
    Drop,
}

/// <summary>
/// Which key triggers which action. Two actions may share a key (the default setup uses Up for both
/// rotations); the first action in the order of <see cref="GameAction"/> that handles the key wins.
/// </summary>
public sealed class KeyBindings
{
    private readonly int[] _keys = new int[7];

    public KeyBindings(int left, int right, int rotateLeft, int down, int rotateRight, int rotateTwice, int drop)
    {
        _keys[(int)GameAction.Left] = left;
        _keys[(int)GameAction.Right] = right;
        _keys[(int)GameAction.RotateLeft] = rotateLeft;
        _keys[(int)GameAction.Down] = down;
        _keys[(int)GameAction.RotateRight] = rotateRight;
        _keys[(int)GameAction.RotateTwice] = rotateTwice;
        _keys[(int)GameAction.Drop] = drop;
    }

    public int this[GameAction action] => _keys[(int)action];
}
