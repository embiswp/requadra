namespace Quadra.Core.Rules;

/// <summary>
/// The generator used for the piece sequence in net versions below 23
/// (<c>Random::crap_rnd</c> in <c>legacy/source/random.cc</c>). It relies on 32-bit wrap-around, so
/// results are identical on every platform.
/// </summary>
public sealed class LegacyRandom
{
    public LegacyRandom(int seed) => Seed = seed;

    public int Seed { get; private set; }

    /// <summary>Next 16-bit value.</summary>
    public ushort Next()
    {
        int tmp = unchecked(Seed * 0x41c64e6d + 0x00003039);
        Seed = tmp >> 10;
        return (ushort)(tmp & 0xFFFF);
    }

    /// <summary>Next piece number, 0 to <see cref="Pieces.Count"/> - 1.</summary>
    public int NextPiece() => Next() % Pieces.Count;
}
