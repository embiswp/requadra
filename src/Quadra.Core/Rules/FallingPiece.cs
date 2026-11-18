namespace Quadra.Core.Rules;

/// <summary>
/// The action bits of one frame, as stored in a <c>Moves</c> packet byte
/// (<c>Player_base::rotate_left</c> and friends in <c>legacy/source/player.cc</c>).
/// </summary>
[Flags]
public enum MoveFlags : byte
{
    None = 0,
    Down = 1,
    Left = 2,
    Right = 4,
    RotateLeft = 8,
    RotateRight = 16,
    RotateTwice = 32,
    Drop = 64,
}

/// <summary>Where a piece came to rest: its box position and rotation.</summary>
public readonly record struct Placement(int X, int Y, int Rotation);

/// <summary>Speeds for net versions below 23 (<c>Canvas::calc_speed</c> and <c>Canvas::reinit</c>).</summary>
public static class LegacySpeeds
{
    /// <summary>Sub-row units a piece falls per frame at a level; one row is 288 units.</summary>
    public static int Gravity(int level) => level <= 10 ? 4 + (level - 1) * 5 : 50 + (level - 10) * 3;

    /// <summary>Frames between repeats of a held key, from a repeat setting (0-3).</summary>
    public static int RepeatDelay(int repeat) => repeat switch { 0 => 11, 1 => 6, 2 => 3, _ => 1 };

    /// <summary>Units per frame while the down key is held, from the player's vertical repeat setting (0-3).</summary>
    public static int SoftDrop(int verticalRepeat) => Math.Min(340 / RepeatDelay(verticalRepeat), 180);
}

/// <summary>Everything that changes while a piece falls, so a frame can be tried and undone.</summary>
public readonly record struct PieceState(int Piece, int Column, int Row, int Rotation, int Y, int Frames);

/// <summary>
/// One piece falling one frame at a time. Each call to <see cref="Step"/> is one frame of
/// <c>Player_process_key::step</c>: the recorded actions are applied in the game's fixed order,
/// then gravity pulls the piece down, and it lands when the next step would collide.
/// Wall kicks are not applied: the 1.1.0 clients that recorded the bundled demos did not have
/// them, and replaying with kicks breaks a recorded stamp.
/// </summary>
public sealed class FallingPiece
{
    /// <summary>Column and row where every piece appears.</summary>
    public const int SpawnColumn = 7;
    public const int SpawnRow = 10;
    private const int RowUnits = 18 << 4;

    private readonly Board _board;
    private int _gravity;
    private readonly int _softDrop;
    private int _y; // vertical position in sub-row units

    public FallingPiece(Board board, int gravity, int softDrop)
    {
        _board = board;
        _gravity = gravity;
        _softDrop = softDrop;
    }

    /// <summary>
    /// Vertical position in sub-row units (288 per row, 0 at row 12), for smooth drawing between rows.
    /// </summary>
    public int Y => _y;

    /// <summary>Changes how fast the piece falls, for a new level (<see cref="LegacySpeeds.Gravity"/>).</summary>
    public void SetGravity(int gravity) => _gravity = gravity;

    public int Piece { get; private set; }
    public int Column { get; private set; }
    public int Row { get; private set; }
    public int Rotation { get; private set; }
    public int Frames { get; private set; }

    public void Spawn(int piece)
    {
        Piece = piece;
        Column = SpawnColumn;
        Row = SpawnRow;
        Rotation = 0;
        // The C++ constructor starts one cell-and-a-bit above the row it reports.
        _y = (SpawnRow - 12) * RowUnits - (17 << 4);
        Frames = 0;
    }

    public PieceState Save() => new(Piece, Column, Row, Rotation, _y, Frames);

    public void Restore(PieceState state)
    {
        Piece = state.Piece;
        Column = state.Column;
        Row = state.Row;
        Rotation = state.Rotation;
        _y = state.Y;
        Frames = state.Frames;
    }

    /// <summary>
    /// Plays one frame from recorded action bits: the actions in the game's fixed order, then
    /// gravity. Returns the placement if the piece lands during it.
    /// </summary>
    public Placement? Step(MoveFlags moves)
    {
        if ((moves & MoveFlags.RotateLeft) != 0) RotateLeft();
        if ((moves & MoveFlags.RotateRight) != 0) RotateRight();
        if ((moves & MoveFlags.RotateTwice) != 0) RotateTwice();
        if ((moves & MoveFlags.Down) != 0) MoveDown();
        if ((moves & MoveFlags.Drop) != 0) HardDrop();
        if ((moves & MoveFlags.Left) != 0) MoveLeft();
        if ((moves & MoveFlags.Right) != 0) MoveRight();
        return Gravity();
    }

    // The individual actions are public so that an input layer can apply them in the same
    // order and see which of them succeeded (key repeat depends on that).

    public bool RotateLeft() => TryRotate(-1);

    public bool RotateRight() => TryRotate(1);

    public bool RotateTwice() => TryRotate(2);

    /// <summary>One soft-drop step; a blocked step settles the piece so gravity lands it next frame.</summary>
    public void MoveDown()
    {
        int row = RowAt(_y + _softDrop);
        if (!Collides(Column, row, Rotation))
        {
            _y += _softDrop;
            Row = RowAt(_y);
        }
        else
        {
            // Settle just short of the next row so that gravity lands the piece on the next frame.
            _y = (Row - 12) * RowUnits - 1;
        }
    }

    public void HardDrop()
    {
        while (!Collides(Column, Row + 1, Rotation))
            Row++;
        _y = (Row - 12) * RowUnits;
    }

    /// <summary>Moves one column left; false if something is in the way.</summary>
    public bool MoveLeft()
    {
        if (Collides(Column - 1, Row, Rotation))
            return false;
        Column--;
        return true;
    }

    /// <summary>Moves one column right; false if something is in the way.</summary>
    public bool MoveRight()
    {
        if (Collides(Column + 1, Row, Rotation))
            return false;
        Column++;
        return true;
    }

    /// <summary>
    /// Ends a frame: gravity pulls the piece down, or the piece lands if the next step would
    /// collide. Returns the placement when it lands.
    /// </summary>
    public Placement? Gravity()
    {
        Frames++;
        if (Collides(Column, RowAt(_y + _gravity), Rotation))
            return new Placement(Column, Row, Rotation);

        _y += _gravity;
        Row = RowAt(_y);
        return null;
    }

    /// <summary>Board row that a vertical position falls into (<c>Player_base::calc_by</c>).</summary>
    private static int RowAt(int y) => ((((y + 15 + (12 * 18 << 4)) >> 4) + 17) / 18) & 0xFF;

    // The C++ code passes uint8_t coordinates, so negative values wrap.
    private bool Collides(int x, int y, int rotation) => _board.Collides(Piece, rotation, x & 0xFF, y & 0xFF);

    private bool TryRotate(int delta)
    {
        int rotation = (Rotation + delta) & 3;
        if (Collides(Column, Row, rotation))
            return false;
        Rotation = rotation;
        return true;
    }
}
