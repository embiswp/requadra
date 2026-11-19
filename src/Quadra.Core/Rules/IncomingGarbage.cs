namespace Quadra.Core.Rules;

/// <summary>
/// The garbage rows waiting to be added to a player's board, and who sent them: for each opponent how much
/// they have attacked recently, which decides who is credited with a frag (<c>Canvas::add_packet</c>).
/// </summary>
public sealed class IncomingGarbage
{
    public const int MaxPlayers = 8;
    public const int NoAttacker = 255;
    private const int MaxPendingRows = 20;
    private const int MaxAttackStrength = 255;

    private readonly int[] _attackStrength = new int[MaxPlayers];
    private readonly List<int> _pendingHoles = [];

    /// <summary>Slot of the player who most recently attacked, or <see cref="NoAttacker"/>.</summary>
    public int LastAttacker { get; private set; } = NoAttacker;

    /// <summary>Hole columns of the garbage rows that will be added after the next stamp.</summary>
    public IReadOnlyList<int> PendingHoles => _pendingHoles;

    /// <summary>How much each player has attacked recently; it wears off by one with every piece placed.</summary>
    public IReadOnlyList<int> AttackStrength => _attackStrength;

    /// <summary>
    /// Queues rows sent by <paramref name="sender"/>. <paramref name="count"/> rows, the
    /// <paramref name="complexity"/> (combo value) and <paramref name="column"/> (the sender's last block
    /// column) decide where the holes go.
    /// </summary>
    public void Receive(int sender, int count, int complexity, int column)
    {
        if (sender is < 0 or >= MaxPlayers)
            return;

        _attackStrength[sender] = Math.Min(MaxAttackStrength, _attackStrength[sender] + count * 2);
        if (LastAttacker == NoAttacker || _attackStrength[sender] >= _attackStrength[LastAttacker])
            LastAttacker = sender;

        if (_pendingHoles.Count >= MaxPendingRows)
            return;
        if (count + _pendingHoles.Count > MaxPendingRows)
            count = MaxPendingRows - _pendingHoles.Count;

        // Unsigned byte arithmetic, as in the C++ code.
        int scatterStep = (complexity - 1) & 0xFF;
        int straight = Math.Max(count - scatterStep, 0);
        int scattered = count - straight;
        int hole = column & 0xFF;

        for (int n = 0; n < straight; n++)
            _pendingHoles.Add(hole);
        for (int n = 0; n < scattered; n++)
        {
            hole = (hole + scatterStep) & 0xFF;
            while (hole > 13)
                hole -= 10;
            _pendingHoles.Add(hole);
        }
    }

    /// <summary>A piece was placed: every attacker's strength wears off a little.</summary>
    public void WearOff()
    {
        for (int i = 0; i < MaxPlayers; i++)
            if (_attackStrength[i] > 0)
                _attackStrength[i]--;
    }

    /// <summary>The rows were added to the board.</summary>
    public void ClearPending() => _pendingHoles.Clear();

    /// <summary>Forgets everything, as at a new game or a respawn.</summary>
    public void Reset()
    {
        Array.Clear(_attackStrength);
        LastAttacker = NoAttacker;
        _pendingHoles.Clear();
    }

    /// <summary>Takes over what a late joiner is told by the host.</summary>
    public void Restore(IEnumerable<int> holes, ReadOnlySpan<int> attackStrength, int lastAttacker)
    {
        for (int i = 0; i < _attackStrength.Length; i++)
            _attackStrength[i] = i < attackStrength.Length ? attackStrength[i] : 0;
        LastAttacker = lastAttacker is >= 0 and < MaxPlayers ? lastAttacker : NoAttacker;
        _pendingHoles.Clear();
        _pendingHoles.AddRange(holes);
    }
}
