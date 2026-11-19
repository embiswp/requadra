namespace Quadra.Core.Rules;

/// <summary>The current piece and the three that follow it, drawn from the game's random sequence (<c>Canvas::bloc/next*</c>).</summary>
public sealed class PieceQueue(LegacyRandom random)
{
    private const int Length = 4;
    private const int Empty = -1;

    // [0] current piece, [1..3] the three upcoming ones
    private readonly int[] _pieces = [Empty, Empty, Empty, Empty];

    /// <summary>The piece the next stamp will use, or -1 before the first draw.</summary>
    public int Current => _pieces[0];

    /// <summary>The three pieces that will appear after the current one, soonest first.</summary>
    public IReadOnlyList<int> Upcoming => [_pieces[1], _pieces[2], _pieces[3]];

    /// <summary>The piece at <paramref name="index"/> (0 is the current one), or -1.</summary>
    public int At(int index) => _pieces[index];

    public int Seed => random.Seed;

    /// <summary>Starts the sequence over from <paramref name="seed"/>.</summary>
    public void Reseed(int seed) => random.SetSeed(seed);

    /// <summary>Draws all four places afresh; the generator itself is not reset, so the sequence carries on.</summary>
    public void Refill()
    {
        Array.Fill(_pieces, Empty);
        while (_pieces[0] < 0)
            Advance();
    }

    /// <summary>The current piece is used up: everything moves one place forward and a new piece joins at the back.</summary>
    public void Advance()
    {
        for (int i = 0; i < Length - 1; i++)
            _pieces[i] = _pieces[i + 1];
        _pieces[Length - 1] = random.NextPiece();
    }

    /// <summary>Takes over what a late joiner is told by the host.</summary>
    public void Restore(int seed, ReadOnlySpan<int> pieces)
    {
        random.SetSeed(seed);
        for (int i = 0; i < _pieces.Length; i++)
            _pieces[i] = pieces[i];
    }
}
