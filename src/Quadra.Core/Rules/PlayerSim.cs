namespace Quadra.Core.Rules;

/// <summary>Outcome of placing one piece, including everything it triggered.</summary>
public readonly record struct StampResult(bool Valid, int Piece, int Lines, int Points);

/// <summary>
/// The rules-only part of one player: board, piece queue, pending garbage, score, lines and the
/// attacker bookkeeping that decides frags. It consumes the final placement from a stamp packet
/// and works out what the C++ client would have done. Net versions below 23, survivor games
/// without level-up (the only rule set the bundled demos exercise).
/// </summary>
public sealed class PlayerSim
{
    public const int MaxPlayers = 8;
    public const int NoAttacker = 255;
    private const int MaxPendingGarbage = 20;

    private readonly LegacyRandom _random;
    private readonly int _level;
    // [0] current piece, [1..3] the three upcoming ones, -1 = empty (as in Canvas::bloc/next*)
    private readonly int[] _queue = [-1, -1, -1, -1];
    private readonly int[] _attacks = new int[MaxPlayers];
    private readonly List<int> _pendingGarbage = [];

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
    public int Deaths { get; private set; }

    /// <summary>Slot of the player who most recently attacked this one, or <see cref="NoAttacker"/>.</summary>
    public int LastAttacker { get; private set; } = NoAttacker;

    /// <summary>Hole columns of the garbage rows that will be added after the next stamp.</summary>
    public IReadOnlyList<int> PendingGarbage => _pendingGarbage;

    /// <summary>The piece the next stamp will use.</summary>
    public int CurrentPiece => _queue[0];

    /// <summary>
    /// Empties the board, the attack bookkeeping and the pending garbage, and draws a fresh queue.
    /// Called when the player starts and on respawn; the generator itself is not reset, so the
    /// sequence carries on. Garbage that arrived while dead is dropped here: the C++ client
    /// applies it to the dead board, which this wipes.
    /// </summary>
    public void Restart()
    {
        Board.Reset();
        Array.Fill(_queue, -1);
        Array.Clear(_attacks);
        LastAttacker = NoAttacker;
        _pendingGarbage.Clear();
        while (_queue[0] < 0)
            Shift();
    }

    /// <summary>
    /// Records a death and returns the slot credited with the frag (<see cref="NoAttacker"/> if none).
    /// </summary>
    public int Die()
    {
        Deaths++;
        _pendingGarbage.Clear();
        return LastAttacker;
    }

    /// <summary>
    /// Queues garbage rows sent by <paramref name="sender"/> (<c>Canvas::add_packet</c>).
    /// <paramref name="count"/> rows, <paramref name="complexity"/> combo value and
    /// <paramref name="column"/> (the sender's last block column) decide where the holes go.
    /// </summary>
    public void ReceiveLines(int sender, int count, int complexity, int column)
    {
        if (sender is < 0 or >= MaxPlayers)
            return;

        _attacks[sender] = Math.Min(255, _attacks[sender] + count * 2);
        if (LastAttacker == NoAttacker || _attacks[sender] >= _attacks[LastAttacker])
            LastAttacker = sender;

        if (_pendingGarbage.Count >= MaxPendingGarbage)
            return;
        if (count + _pendingGarbage.Count > MaxPendingGarbage)
            count = MaxPendingGarbage - _pendingGarbage.Count;

        // Unsigned byte arithmetic, as in the C++ code.
        int nc = (complexity - 1) & 0xFF;
        int normal = Math.Max(count - nc, 0);
        int scattered = count - normal;
        int lx = column & 0xFF;

        for (int n = 0; n < normal; n++)
            _pendingGarbage.Add(lx);
        for (int n = 0; n < scattered; n++)
        {
            lx = (lx + nc) & 0xFF;
            while (lx > 13)
                lx -= 10;
            _pendingGarbage.Add(lx);
        }
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

        for (int i = 0; i < MaxPlayers; i++)
            if (_attacks[i] > 0)
                _attacks[i]--;

        // Player_stamp::init stacks three modules, so they run: line check, garbage, line check.
        int before = Score;
        int lines = ClearLines();
        if (_pendingGarbage.Count > 0)
        {
            Board.AddGarbage(_pendingGarbage);
            _pendingGarbage.Clear();
            lines += ClearLines();
        }
        return new StampResult(valid, piece, lines, Score - before);
    }

    /// <summary>
    /// Clears complete rows until none is left (each pass is one step of the combo), then scores
    /// the move (<c>Player_check_line</c> followed by <c>Canvas::give_line</c>).
    /// </summary>
    private int ClearLines()
    {
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

        if (lines > 0)
        {
            Score += LegacyScoring.Clear(lines, combo, _level);
            Lines += lines;
        }
        return lines;
    }

    private void Shift()
    {
        _queue[0] = _queue[1];
        _queue[1] = _queue[2];
        _queue[2] = _queue[3];
        _queue[3] = _random.NextPiece();
    }
}
