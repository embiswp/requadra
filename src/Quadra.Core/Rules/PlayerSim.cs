namespace Quadra.Core.Rules;

/// <summary>Outcome of placing one piece.</summary>
public readonly record struct StampResult(bool Valid, int Piece, int Lines, int Combo, int Points);

/// <summary>
/// The rules-only part of one player: board, piece queue, score and lines. It consumes the
/// final placement from a stamp packet and works out what the C++ client would have done.
/// Net versions below 23 only; garbage rows from opponents are not applied yet.
/// </summary>
public sealed class PlayerSim
{
    private readonly LegacyRandom _random;
    private readonly int _level;
    // [0] current piece, [1..3] the three upcoming ones, -1 = empty (as in Canvas::bloc/next*)
    private readonly int[] _queue = [-1, -1, -1, -1];

    public PlayerSim(int gameSeed, int level)
    {
        _random = new LegacyRandom(gameSeed);
        _level = level;
        Restart();
    }

    public Board Board { get; } = new();
    public int Score { get; private set; }
    public int Lines { get; private set; }
    public int Stamps { get; private set; }
    public int InvalidStamps { get; private set; }

    /// <summary>The piece the next stamp will use.</summary>
    public int CurrentPiece => _queue[0];

    /// <summary>
    /// Empties the board and draws a fresh queue. Called when the player starts and on respawn;
    /// the generator itself is not reset, so the sequence carries on.
    /// </summary>
    public void Restart()
    {
        Board.Reset();
        Array.Fill(_queue, -1);
        while (_queue[0] < 0)
            Shift();
    }

    public StampResult Stamp(int x, int y, int rotation, int dropScore)
    {
        int piece = _queue[0];
        bool valid = Board.IsValidPlacement(piece, rotation, x, y);
        if (!valid)
            InvalidStamps++;
        Stamps++;
        Score += dropScore;
        Board.Stamp(piece, rotation, x, y);
        Shift();

        int lines = 0, combo = 0;
        while (true)
        {
            int cleared = Board.ClearFullRows();
            if (cleared == 0)
                break;
            lines += cleared;
            combo++;
            if (Board.IsEmpty())
                Score += LegacyScoring.CleanBoardBonus;
            Board.DropUnsupported();
        }

        int points = 0;
        if (lines > 0)
        {
            points = LegacyScoring.Clear(lines, combo, _level);
            Score += points;
            Lines += lines;
        }
        return new StampResult(valid, piece, lines, combo, points);
    }

    private void Shift()
    {
        _queue[0] = _queue[1];
        _queue[1] = _queue[2];
        _queue[2] = _queue[3];
        _queue[3] = _random.NextPiece();
    }
}
