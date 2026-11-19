using Quadra.Core.Input;

namespace Quadra.Core.Rules;

/// <summary>Everything about the current piece that a frame can change, so a frame can be tried and undone.</summary>
public readonly record struct FrameSnapshot(PieceState Piece, ControllerState Input, Placement? Landed, int MovesAfterLanding);

/// <summary>
/// One step of a line clear as it looked on screen: the rows that were removed (they flash), the board
/// right after the removal, and the board after each step in which loose cells fell one row.
/// </summary>
public sealed record ClearPass(IReadOnlyList<int> Rows, int Combo, BoardSnapshot AfterRemoval, IReadOnlyList<BoardSnapshot> FallSteps);

/// <summary>Outcome of placing one piece, including everything it triggered.</summary>
/// <param name="Combo">Most successive clears caused by this piece (1 for a plain clear, 0 for none).</param>
/// <param name="Falls">Number of steps in which loose cells fell.</param>
/// <param name="Clears">
/// The moves that scored, in order. A piece makes one; garbage that then completes rows makes a second,
/// and each of them sends its own attack.
/// </param>
/// <param name="LastColumn">Board column of the last block of the piece, the anchor of garbage holes it sends.</param>
public readonly record struct StampResult(bool Valid, int Piece, int Lines, int Points, int Combo = 0, int Falls = 0)
{
    public IReadOnlyList<ClearEvent> Clears { get; init; } = [];
    public int LastColumn { get; init; }
}

/// <summary>One scoring move: <paramref name="Lines"/> rows over <paramref name="Combo"/> successive passes.</summary>
/// <param name="Clean">The board was left empty, which sends extra lines.</param>
public readonly record struct ClearEvent(int Lines, int Combo, bool Clean);

/// <summary>
/// The rules-only part of one player: board, piece queue, pending garbage, score, lines and the
/// attacker bookkeeping that decides frags. It consumes the final placement from a stamp packet
/// and works out what the C++ client would have done. Net versions below 23, survivor games
/// without level-up (the only rule set the bundled demos exercise).
/// </summary>
public sealed class PlayerSim
{
    public const int MaxPlayers = IncomingGarbage.MaxPlayers;
    public const int NoAttacker = IncomingGarbage.NoAttacker;

    private readonly PieceQueue _queue;
    private readonly IncomingGarbage _garbage = new();
    private readonly int _startLevel;
    private readonly bool _levelUp;
    private int _level;
    private readonly List<ClearPass> _lastClear = [];
    private readonly int[] _pieceCounts = new int[Pieces.Count];
    private readonly int[] _clearCounts = new int[15];
    private int _falls, _bestCombo;

    private readonly FallingPiece _falling;
    private PieceController? _input;

    /// <param name="gameSeed">Seed from the game packet.</param>
    /// <param name="level">Level the game starts at.</param>
    /// <param name="verticalRepeat">The player's vertical repeat setting (0-3), which sets the soft-drop speed.</param>
    /// <param name="levelUp">
    /// Single-player games raise the level every 15 lines (the rule is a port of <c>Canvas::give_line</c>
    /// and is not covered by the bundled demos, which are all without level-up).
    /// </param>
    public PlayerSim(int gameSeed, int level, int verticalRepeat = 2, bool levelUp = false)
    {
        _queue = new PieceQueue(new LegacyRandom(gameSeed));
        _startLevel = level;
        _level = level;
        _levelUp = levelUp;
        _falling = new FallingPiece(Board, LegacySpeeds.Gravity(level), LegacySpeeds.SoftDrop(verticalRepeat));
        Restart();
    }

    public Board Board { get; } = new();
    public int Score { get; private set; }
    public int Lines { get; private set; }

    /// <summary>Current level; it only rises when level-up is on.</summary>
    public int Level => _level;

    /// <summary>Lines cleared since the last (re)start; the level rises when this reaches 15 times the level.</summary>
    public int LinesThisRun { get; private set; }

    /// <summary>The three pieces that will appear after the current one, soonest first.</summary>
    public IReadOnlyList<int> Upcoming => _queue.Upcoming;
    public int Stamps { get; private set; }
    public int InvalidStamps { get; private set; }
    public int Deaths { get; private set; }

    /// <summary>How many pieces of each of the seven kinds have been placed (<c>CS::COMPTE0</c> and up).</summary>
    public IReadOnlyList<int> PieceCounts => _pieceCounts;

    /// <summary>
    /// How many moves cleared 1, 2, ... 14 lines at once; the last entry counts 15 or more
    /// (<c>CS::CLEAR00</c> and up). The original also counts a 15-line move twice; that is not copied.
    /// </summary>
    public IReadOnlyList<int> ClearCounts => _clearCounts;

    /// <summary>When true, <see cref="Stamp"/> keeps what the clear looked like in <see cref="LastClear"/>.</summary>
    public bool RecordClearAnimation { get; set; }

    /// <summary>The steps of the clear caused by the last <see cref="Stamp"/> (only if recorded).</summary>
    public IReadOnlyList<ClearPass> LastClear => _lastClear;

    /// <summary>The piece in flight; moves are applied to it with <see cref="Move"/>.</summary>
    public FallingPiece Falling => _falling;

    /// <summary>Where the current piece landed, once <see cref="Move"/> has brought it to rest.</summary>
    public Placement? Landed { get; private set; }

    /// <summary>Moves received after the piece had already landed (should stay zero in a valid game).</summary>
    public int MovesAfterLanding { get; private set; }

    /// <summary>Slot of the player who most recently attacked this one, or <see cref="NoAttacker"/>.</summary>
    public int LastAttacker => _garbage.LastAttacker;

    /// <summary>Hole columns of the garbage rows that will be added after the next stamp.</summary>
    public IReadOnlyList<int> PendingGarbage => _garbage.PendingHoles;

    /// <summary>The piece the next stamp will use.</summary>
    public int CurrentPiece => _queue.Current;

    /// <summary>
    /// True when the piece that just appeared overlaps blocks, which is how a player dies
    /// (<c>Player_normal::step</c>). Checked after every stamp; it matched all 33 recorded deaths
    /// in the bundled demos. The caller then calls <see cref="Die"/>.
    /// </summary>
    public bool SpawnBlocked =>
        Board.Collides(_queue.Current, 0, FallingPiece.SpawnColumn, FallingPiece.SpawnRow);

    /// <summary>
    /// Empties the board, the attack bookkeeping and the pending garbage, and draws a fresh queue.
    /// Called when the player starts and on respawn; the generator itself is not reset, so the
    /// sequence carries on. Garbage that arrived while dead is dropped here: the C++ client
    /// applies it to the dead board, which this wipes.
    /// </summary>
    public void Restart()
    {
        Board.Reset();
        _level = _startLevel;
        _falling.SetGravity(LegacySpeeds.Gravity(_level));
        LinesThisRun = 0;
        _garbage.Reset();
        _queue.Refill();
        _falling.Spawn(_queue.Current);
        Landed = null;
        _input?.BeginPiece();
    }

    /// <summary>Starts the piece sequence over from a seed the host sent for a new round; the next <see cref="Restart"/> draws from it.</summary>
    public void Reseed(int seed) => _queue.Reseed(seed);

    // --- a late joiner takes a board over from the host (Packet_download)

    internal int RandomSeed => _queue.Seed;

    /// <summary>The piece at <paramref name="index"/> of the queue (0 is the current one), or -1.</summary>
    internal int QueuedPiece(int index) => _queue.At(index);

    internal IReadOnlyList<int> Attacks => _garbage.AttackStrength;

    /// <summary>Puts the sim in the state a download describes; the board cells are loaded separately.</summary>
    internal void Restore(int seed, ReadOnlySpan<int> queue, IEnumerable<int> holes, ReadOnlySpan<int> attacks, int lastAttacker)
    {
        _queue.Restore(seed, queue);
        _garbage.Restore(holes, attacks, lastAttacker);
        Landed = null;
        if (_queue.Current >= 0)
            _falling.Spawn(_queue.Current);
    }

    internal void RestoreCounters(int score, int lines, int deaths)
    {
        Score = score;
        Lines = lines;
        Deaths = deaths;
    }

    /// <summary>The key-to-action layer, if <see cref="AttachInput"/> was called.</summary>
    public PieceController? Input => _input;

    /// <summary>
    /// Lets <see cref="Frame"/> drive the piece from key states (for a player at the keyboard).
    /// </summary>
    /// <param name="horizontalRepeat">The player's horizontal repeat setting (0-3).</param>
    public void AttachInput(KeyStates keys, KeyBindings bindings, int horizontalRepeat, bool continuousDown = true, bool retryBlockedMoves = false)
    {
        _falling.SetSideSpeed(LegacySpeeds.SideSpeed(horizontalRepeat));
        _input = new PieceController(_falling, keys, bindings, LegacySpeeds.RepeatDelay(horizontalRepeat), continuousDown, retryBlockedMoves);
        _input.BeginPiece();
    }

    /// <summary>
    /// Plays one frame of the current piece from the key states. Returns what was attempted (the byte
    /// to record or send) and where the piece landed, if it did.
    /// </summary>
    public FrameResult Frame()
    {
        if (_input is null)
            throw new InvalidOperationException("No input attached; call AttachInput first.");
        if (SpawnBlocked && _falling.Frames == 0)
            throw new InvalidOperationException("The piece appeared on top of blocks; the player is dead (see Die).");
        if (Landed is not null)
        {
            MovesAfterLanding++;
            return new FrameResult(MoveFlags.None, Landed);
        }
        var result = _input.Frame();
        Landed = result.Landed;
        return result;
    }

    public FrameSnapshot SaveFrame() =>
        new(_falling.Save(), _input?.Save() ?? default, Landed, MovesAfterLanding);

    public void RestoreFrame(FrameSnapshot snapshot)
    {
        _falling.Restore(snapshot.Piece);
        _input?.Restore(snapshot.Input);
        Landed = snapshot.Landed;
        MovesAfterLanding = snapshot.MovesAfterLanding;
    }

    /// <summary>
    /// Plays one frame of the current piece from recorded action bits (one byte of a <c>Moves</c> packet). Returns the
    /// placement when the piece lands; it is then stamped with <see cref="Stamp"/>.
    /// </summary>
    public Placement? Move(MoveFlags moves)
    {
        if (Landed is not null)
        {
            MovesAfterLanding++;
            return Landed;
        }
        var landed = _falling.Step(moves);
        Landed = landed;
        return landed;
    }

    /// <summary>
    /// Records a death and returns the slot credited with the frag (<see cref="NoAttacker"/> if none).
    /// </summary>
    public int Die()
    {
        Deaths++;
        _garbage.ClearPending();
        Landed = null;
        return LastAttacker;
    }

    /// <summary>
    /// Queues garbage rows sent by <paramref name="sender"/> (<c>Canvas::add_packet</c>); see <see cref="IncomingGarbage.Receive"/>.
    /// </summary>
    public void ReceiveLines(int sender, int count, int complexity, int column) =>
        _garbage.Receive(sender, count, complexity, column);

    public StampResult Stamp(int x, int y, int rotation, int dropScore)
    {
        int piece = _queue.Current;
        bool valid = Board.IsValidPlacement(piece, rotation, x, y);
        if (!valid)
            InvalidStamps++;
        Stamps++;
        _pieceCounts[piece]++;
        Score += dropScore;
        Board.Stamp(piece, rotation, x, y);
        int lastColumn = x + LastCellColumn(piece, rotation);
        _queue.Advance();
        _garbage.WearOff();

        // Player_stamp::init stacks three modules, so they run: line check, garbage, line check.
        int before = Score;
        _lastClear.Clear();
        _falls = _bestCombo = 0;
        var clears = new List<ClearEvent>();
        int lines = ClearLines(clears);
        if (_garbage.PendingHoles.Count > 0)
        {
            Board.AddGarbage(_garbage.PendingHoles);
            _garbage.ClearPending();
            lines += ClearLines(clears);
        }
        _falling.Spawn(_queue.Current);
        Landed = null;
        _input?.BeginPiece();
        return new StampResult(valid, piece, lines, Score - before, _bestCombo, _falls)
        {
            Clears = clears,
            LastColumn = lastColumn,
        };
    }

    /// <summary>
    /// Clears complete rows until none is left (each pass is one step of the combo), then scores
    /// the move (<c>Player_check_line</c> followed by <c>Canvas::give_line</c>).
    /// </summary>
    private int ClearLines(List<ClearEvent> clears)
    {
        int lines = 0, combo = 0;
        bool clean = false;
        while (true)
        {
            var rows = RecordClearAnimation ? new List<int>() : null;
            int cleared = Board.ClearFullRows(rows);
            if (cleared == 0)
                break;
            lines += cleared;
            combo++;
            if (Board.IsEmpty())
            {
                Score += LegacyScoring.CleanBoardBonus;
                clean = true;
            }
            var afterRemoval = RecordClearAnimation ? Board.Snapshot() : null;
            var steps = RecordClearAnimation ? new List<BoardSnapshot>() : null;
            _falls += Board.DropUnsupported(steps);
            if (afterRemoval is not null)
                _lastClear.Add(new ClearPass(rows!, combo, afterRemoval, steps!));
        }
        _bestCombo = Math.Max(_bestCombo, combo);

        if (lines > 0)
        {
            Score += LegacyScoring.Clear(lines, combo, _level);
            _clearCounts[Math.Min(lines, _clearCounts.Length) - 1]++;
            clears.Add(new ClearEvent(lines, combo, clean));
            Lines += lines;
            LinesThisRun += lines;
            if (_levelUp && LinesThisRun >= _level * 15)
            {
                _level++;
                _falling.SetGravity(LegacySpeeds.Gravity(_level));
            }
        }
        return lines;
    }

    // The column (within the piece's 4x4 box) of the last block in reading order, which is how the original
    // picks the anchor of the holes in the garbage a clear sends.
    private static int LastCellColumn(int piece, int rotation)
    {
        int column = 0;
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
                if (Pieces.Shapes[piece, rotation, j, i] != 0)
                    column = i;
        return column;
    }
}
