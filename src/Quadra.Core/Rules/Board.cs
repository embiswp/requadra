namespace Quadra.Core.Rules;

/// <summary>What a painter needs to know about a playfield.</summary>
public interface IBoardView
{
    bool IsOccupied(int row, int column);

    /// <summary>Colour (piece number &lt;&lt; 4) plus the four border bits of the cell.</summary>
    byte Cell(int row, int column);
}

/// <summary>A frozen copy of a playfield, used to replay how a line clear looked.</summary>
public sealed class BoardSnapshot : IBoardView
{
    private readonly bool[,] _occupied;
    private readonly byte[,] _cell;

    internal BoardSnapshot(bool[,] occupied, byte[,] cell)
    {
        _occupied = (bool[,])occupied.Clone();
        _cell = (byte[,])cell.Clone();
    }

    public bool IsOccupied(int row, int column) =>
        (uint)row < Board.Height && (uint)column < Board.Width && _occupied[row, column];

    public byte Cell(int row, int column) =>
        (uint)row < Board.Height && (uint)column < Board.Width ? _cell[row, column] : (byte)0;
}

/// <summary>
/// One player's playfield. The layout follows <c>Canvas</c> in the C++ code: a 18 x 36 grid whose
/// playable part is columns 4-13 and rows 0-31; the rest is wall and floor, which count as occupied.
/// </summary>
public sealed class Board : IBoardView
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

    public byte Cell(int row, int column) => InRange(row, column) ? _cell[row, column] : (byte)0;

    public BoardSnapshot Snapshot() => new(_occupied, _cell);

    public bool IsEmpty()
    {
        for (int row = FirstClearableRow; row < Rows; row++)
            for (int column = FirstColumn; column < EndColumn; column++)
                if (_occupied[row, column])
                    return false;
        return true;
    }

    public void Reset()
    {
        Array.Clear(_cell);
        for (int row = 0; row < Height; row++)
            for (int column = 0; column < Width; column++)
            {
                bool wall = row >= Rows || column < FirstColumn || column >= EndColumn;
                _occupied[row, column] = wall;
                _supported[row, column] = row == Rows || (row < Rows && column is < FirstColumn or >= EndColumn);
            }
    }

    /// <summary>Sets one playfield cell as it was sent to a late joiner.</summary>
    internal void Load(int row, int column, byte cell, bool occupied)
    {
        _cell[row, column] = cell;
        _occupied[row, column] = occupied;
    }

    /// <summary>Occupies one cell without a piece (used by tests to set up positions).</summary>
    internal void Fill(int row, int column)
    {
        _occupied[row, column] = true;
        _cell[row, column] = 0;
    }

    public bool Collides(int piece, int rotation, int x, int y)
    {
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                if (Pieces.Shapes[piece, rotation, row, column] != 0 && IsOccupied(y + row, x + column))
                    return true;
        return false;
    }

    /// <summary>A placement the game would accept: free cells, and resting on something.</summary>
    public bool IsValidPlacement(int piece, int rotation, int x, int y) =>
        !Collides(piece, rotation, x, y) && Collides(piece, rotation, x, y + 1);

    public void Stamp(int piece, int rotation, int x, int y)
    {
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
            {
                byte shape = Pieces.Shapes[piece, rotation, row, column];
                if (shape != 0 && InRange(y + row, x + column))
                {
                    _cell[y + row, x + column] = (byte)(shape + (piece << CellEdge.ColorShift));
                    _occupied[y + row, x + column] = true;
                }
            }
    }

    /// <summary>
    /// Removes every complete row (<c>Player_check_line::check_nb_line</c>) and marks the new
    /// borders above and below. Rows are removed in place; nothing falls until
    /// <see cref="DropUnsupported"/>. Returns the number of rows removed.
    /// </summary>
    public int ClearFullRows(List<int>? clearedRows = null)
    {
        int cleared = 0;
        for (int row = FirstClearableRow; row < Rows; row++)
        {
            bool full = true;
            for (int column = FirstColumn; column < EndColumn && full; column++)
                full = _occupied[row, column];
            if (!full)
                continue;

            for (int column = FirstColumn; column < EndColumn; column++)
            {
                // What was joined to this row above and below now has an edge there.
                if (_occupied[row - 1, column])
                    _cell[row - 1, column] |= CellEdge.Bottom;
                _cell[row, column] = 0;
                _occupied[row, column] = false;
                if (_occupied[row + 1, column])
                    _cell[row + 1, column] |= CellEdge.Top;
            }
            clearedRows?.Add(row);
            cleared++;
        }
        return cleared;
    }

    /// <summary>
    /// Lets cells that no longer rest on anything fall one row at a time until everything is
    /// supported again (<c>Player_check_link</c>). Cells stay together through their edge bits.
    /// </summary>
    /// <param name="steps">If given, receives a snapshot after every step in which cells moved down.</param>
    /// <returns>The number of steps in which cells moved down.</returns>
    public int DropUnsupported(List<BoardSnapshot>? steps = null)
    {
        int fallSteps = 0;
        ClearSupport();
        while (true)
        {
            SpreadSupportUpwards();

            bool anyFell = false;
            for (int row = Rows - 1; row >= 0; row--)
                for (int column = FirstColumn; column < EndColumn; column++)
                    if (_occupied[row, column] && !_supported[row, column])
                    {
                        anyFell = true;
                        _cell[row + 1, column] = _cell[row, column];
                        _occupied[row + 1, column] = true;
                        _cell[row, column] = 0;
                        _occupied[row, column] = false;
                    }
            if (!anyFell)
                return fallSteps;
            fallSteps++;
            steps?.Add(Snapshot());
        }
    }

    // A cell with an edge below it that rests on something supported is supported itself, and so is everything joined to it.
    private void SpreadSupportUpwards()
    {
        bool spread;
        do
        {
            spread = false;
            for (int row = Rows - 1; row >= 0; row--)
                for (int column = FirstColumn; column < EndColumn; column++)
                    if ((_cell[row, column] & CellEdge.Bottom) != 0 && !_supported[row, column] && _supported[row + 1, column])
                    {
                        MarkSupportedWithNeighbours(column, row);
                        spread = true;
                    }
        } while (spread);
    }

    /// <summary>
    /// Pushes the whole playfield up by one row for each entry and fills the bottom row, leaving a
    /// hole at that column (<c>Player_add_bonus::step</c>, net versions below 23), then lets
    /// anything left unsupported fall.
    /// </summary>
    /// <param name="holeColumns">Hole column (4-13) of each garbage row, oldest first.</param>
    public void AddGarbage(IReadOnlyList<int> holeColumns)
    {
        const int bottomRow = Rows - 1;
        bool isFirstRow = true;
        foreach (int holeColumn in holeColumns)
        {
            for (int row = 0; row < bottomRow; row++)
                for (int column = FirstColumn; column < EndColumn; column++)
                {
                    _cell[row, column] = _cell[row + 1, column];
                    _occupied[row, column] = _occupied[row + 1, column];
                }

            for (int column = FirstColumn; column < EndColumn; column++)
            {
                _cell[bottomRow, column] = CellEdge.Top | CellEdge.Bottom;
                _occupied[bottomRow, column] = true;
            }

            for (int column = FirstColumn; column < EndColumn; column++)
            {
                if (column == holeColumn)
                {
                    _cell[bottomRow, column] = 0;
                    _occupied[bottomRow, column] = false;
                    if (!isFirstRow && _occupied[bottomRow - 1, column])
                        _cell[bottomRow - 1, column] |= CellEdge.Bottom;
                    if (column > FirstColumn)
                    {
                        if (_occupied[bottomRow, column - 1])
                            _cell[bottomRow, column - 1] |= CellEdge.Right;
                        if (_occupied[bottomRow, FirstColumn])
                            _cell[bottomRow, FirstColumn] |= CellEdge.Left;
                    }
                    _cell[bottomRow, column + 1] |= CellEdge.Left;
                    _cell[bottomRow, EndColumn] |= CellEdge.Right;
                }
                else if (!isFirstRow && !_occupied[bottomRow - 1, column])
                {
                    _cell[bottomRow, column] |= CellEdge.Top;
                }
            }
            isFirstRow = false;
        }

        for (int column = FirstColumn; column < EndColumn; column++)
            if (_occupied[0, column])
                _cell[0, column] |= CellEdge.Top;     // clip the top
        DropUnsupported();
    }

    private void ClearSupport()
    {
        for (int row = 0; row < Rows; row++)
            for (int column = FirstColumn; column < EndColumn; column++)
                _supported[row, column] = false;
    }

    // Marks a cell supported, and every cell joined to it.
    private void MarkSupportedWithNeighbours(int x, int y)
    {
        var pending = new Stack<(int X, int Y)>();
        _supported[y, x] = true;
        pending.Push((x, y));
        while (pending.Count > 0)
        {
            (x, y) = pending.Pop();
            byte edges = _cell[y, x];
            if ((edges & CellEdge.Bottom) == 0 && !_supported[y + 1, x]) { _supported[y + 1, x] = true; pending.Push((x, y + 1)); }
            if ((edges & CellEdge.Left) == 0 && !_supported[y, x - 1]) { _supported[y, x - 1] = true; pending.Push((x - 1, y)); }
            if ((edges & CellEdge.Right) == 0 && !_supported[y, x + 1]) { _supported[y, x + 1] = true; pending.Push((x + 1, y)); }
            if (y > 0 && (edges & CellEdge.Top) == 0 && !_supported[y - 1, x]) { _supported[y - 1, x] = true; pending.Push((x, y - 1)); }
        }
    }

    private static bool InRange(int row, int column) => row >= 0 && row < Height && column >= 0 && column < Width;
}
