namespace Quadra.Core.Rules;

/// <summary>
/// One player's playfield. The layout follows <c>Canvas</c> in the C++ code: a 18 x 36 grid whose
/// playable part is columns 4-13 and rows 0-31; the rest is wall and floor, which count as occupied.
/// </summary>
public sealed class Board
{
    public const int Width = 18;
    public const int Height = 36;
    public const int FirstColumn = 4;
    public const int EndColumn = 14; // exclusive
    public const int Rows = 32;
    /// <summary>Rows above this one never count as cleared lines (the spawn area).</summary>
    public const int FirstClearableRow = 12;

    private readonly bool[,] _occupied = new bool[Height, Width];
    private readonly byte[,] _cell = new byte[Height, Width];
    private readonly bool[,] _supported = new bool[Height, Width];

    public Board() => Reset();

    public bool IsOccupied(int row, int column) => InRange(row, column) ? _occupied[row, column] : true;

    /// <summary>Colour (piece number &lt;&lt; 4) plus the four border bits of the cell.</summary>
    public byte Cell(int row, int column) => _cell[row, column];

    public bool IsEmpty()
    {
        for (int j = FirstClearableRow; j < Rows; j++)
            for (int i = FirstColumn; i < EndColumn; i++)
                if (_occupied[j, i])
                    return false;
        return true;
    }

    public void Reset()
    {
        Array.Clear(_cell);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                bool wall = y >= Rows || x < FirstColumn || x >= EndColumn;
                _occupied[y, x] = wall;
                _supported[y, x] = y == Rows || (y < Rows && x is < FirstColumn or >= EndColumn);
            }
    }

    public bool Collides(int piece, int rotation, int x, int y)
    {
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
                if (Pieces.Shapes[piece, rotation, j, i] != 0 && IsOccupied(y + j, x + i))
                    return true;
        return false;
    }

    /// <summary>A placement the game would accept: free cells, and resting on something.</summary>
    public bool IsValidPlacement(int piece, int rotation, int x, int y) =>
        !Collides(piece, rotation, x, y) && Collides(piece, rotation, x, y + 1);

    public void Stamp(int piece, int rotation, int x, int y)
    {
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
            {
                byte t = Pieces.Shapes[piece, rotation, j, i];
                if (t != 0 && InRange(y + j, x + i))
                {
                    _cell[y + j, x + i] = (byte)(t + (piece << 4));
                    _occupied[y + j, x + i] = true;
                }
            }
    }

    /// <summary>
    /// Removes every complete row (<c>Player_check_line::check_nb_line</c>) and marks the new
    /// borders above and below. Rows are removed in place; nothing falls until
    /// <see cref="DropUnsupported"/>. Returns the number of rows removed.
    /// </summary>
    public int ClearFullRows()
    {
        int cleared = 0;
        for (int j = FirstClearableRow; j < Rows; j++)
        {
            bool full = true;
            for (int i = FirstColumn; i < EndColumn && full; i++)
                full = _occupied[j, i];
            if (!full)
                continue;

            for (int i = FirstColumn; i < EndColumn; i++)
            {
                if (_occupied[j - 1, i] && (_cell[j - 1, i] & 8) == 0)
                    _cell[j - 1, i] |= 8;
                _cell[j, i] = 0;
                _occupied[j, i] = false;
                if (_occupied[j + 1, i] && (_cell[j + 1, i] & 2) == 0)
                    _cell[j + 1, i] |= 2;
            }
            cleared++;
        }
        return cleared;
    }

    /// <summary>
    /// Lets cells that no longer rest on anything fall one row at a time until everything is
    /// supported again (<c>Player_check_link</c>). Cells stay together through their border bits.
    /// </summary>
    public void DropUnsupported()
    {
        ClearSupport();
        while (true)
        {
            bool grew;
            do
            {
                grew = false;
                for (int j = Rows - 1; j >= 0; j--)
                    for (int i = FirstColumn; i < EndColumn; i++)
                        if ((_cell[j, i] & 8) != 0 && !_supported[j, i] && _supported[j + 1, i])
                        {
                            FillSupported(i, j);
                            grew = true;
                        }
            } while (grew);

            bool moved = false;
            for (int j = Rows - 1; j >= 0; j--)
                for (int i = FirstColumn; i < EndColumn; i++)
                    if (_occupied[j, i] && !_supported[j, i])
                    {
                        moved = true;
                        _cell[j + 1, i] = _cell[j, i];
                        _occupied[j + 1, i] = true;
                        _cell[j, i] = 0;
                        _occupied[j, i] = false;
                    }
            if (!moved)
                return;
        }
    }

    private void ClearSupport()
    {
        for (int j = 0; j < Rows; j++)
            for (int i = FirstColumn; i < EndColumn; i++)
                _supported[j, i] = false;
    }

    private void FillSupported(int x, int y)
    {
        var pending = new Stack<(int X, int Y)>();
        _supported[y, x] = true;
        pending.Push((x, y));
        while (pending.Count > 0)
        {
            (x, y) = pending.Pop();
            byte b = _cell[y, x];
            if ((b & 8) == 0 && !_supported[y + 1, x]) { _supported[y + 1, x] = true; pending.Push((x, y + 1)); }
            if ((b & 1) == 0 && !_supported[y, x - 1]) { _supported[y, x - 1] = true; pending.Push((x - 1, y)); }
            if ((b & 4) == 0 && !_supported[y, x + 1]) { _supported[y, x + 1] = true; pending.Push((x + 1, y)); }
            if (y > 0 && (b & 2) == 0 && !_supported[y - 1, x]) { _supported[y - 1, x] = true; pending.Push((x, y - 1)); }
        }
    }

    private static bool InRange(int row, int column) => row >= 0 && row < Height && column >= 0 && column < Width;
}
