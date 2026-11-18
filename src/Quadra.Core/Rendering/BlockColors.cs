using Quadra.Core.Assets;

namespace Quadra.Core.Rendering;

/// <summary>
/// The colour ramps the blocks are painted with. Each of the 9 colours has 8 shades (1 dark to 7
/// light, 4 the main colour) at palette entries 184 and up (<c>Color</c> in <c>legacy/source/color.cc</c>).
/// Colours 0 to 6 belong to the seven pieces; colour 8 is the landing shadow.
/// </summary>
public sealed class BlockColors
{
    public const int ShadowColor = 8;
    private const int FirstEntry = 184;

    private readonly uint[] _shades = new uint[9 * 8];

    public BlockColors(Rgb[] palette)
    {
        if (palette.Length < FirstEntry + 9 * 8)
            throw new ArgumentException("Palette too small to hold the block colours.", nameof(palette));
        for (int color = 0; color < 9; color++)
        {
            // The original swaps colours 7 and 8 (so the shadow uses the entries after the pieces).
            int q = color == 8 ? 7 : color == 7 ? 8 : color;
            for (int shade = 0; shade < 8; shade++)
                _shades[color * 8 + shade] = PixelBuffer.Pack(palette[q * 8 + FirstEntry + shade]);
        }
    }

    public uint Shade(int color, int shade) => _shades[color * 8 + shade];
}
