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

    /// <summary>Units per frame while the down key is held, from the player's vertical repeat setting (0-3).</summary>
    public static int SoftDrop(int verticalRepeat)
    {
        int delay = verticalRepeat switch { 0 => 11, 1 => 6, 2 => 3, _ => 1 };
        return Math.Min(340 / delay, 180);
    }
}

/// <summary>
/// One piece falling one frame at a time. Each call to <see cref="Step"/> is one frame of
/// <c>Player_process_key::step</c>: the recorded actions are applied in the game's fixed order,
/// then gravity pulls the piece down, and it lands when the next step would collide.
/// Wall kicks are not applied: the 1.1.0 clients that recorded the bundled demos did not have
/// them, and replaying with kicks breaks a recorded stamp.
/// </summary>
public sealed class FallingPiece
{
    private const int SpawnColumn = 7;
    private const int SpawnRow = 10;
    private const int RowUnits = 18 << 4;

    private readonly Board _board;
    private readonly int _gravity;
    private readonly int _softDrop;
    private int _y; // vertical position in sub-row units

    public FallingPiece(Board board, int gravity, int softDrop)
    {
        _board = board;
        _gravity = gravity;
        _softDrop = softDrop;
    }

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

    /// <summary>Plays one frame. Returns the placement if the piece lands during it.</summary>
    public Placement? Step(MoveFlags moves)
    {
        Frames++;
        if ((moves & MoveFlags.RotateLeft) != 0) TryRotate(-1);
        if ((moves & MoveFlags.RotateRight) != 0) TryRotate(1);
        if ((moves & MoveFlags.RotateTwice) != 0) TryRotate(2);
        if ((moves & MoveFlags.Down) != 0) SoftDrop();
        if ((moves & MoveFlags.Drop) != 0) HardDrop();
        if ((moves & MoveFlags.Left) != 0 && !Collides(Column - 1, Row, Rotation)) Column--;
        if ((moves & MoveFlags.Right) != 0 && !Collides(Column + 1, Row, Rotation)) Column++;

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

    private void TryRotate(int delta)
    {
        int rotation = (Rotation + delta) & 3;
        if (!Collides(Column, Row, rotation))
            Rotation = rotation;
    }

    private void SoftDrop()
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

    private void HardDrop()
    {
        while (!Collides(Column, Row + 1, Rotation))
            Row++;
        _y = (Row - 12) * RowUnits;
    }
}
