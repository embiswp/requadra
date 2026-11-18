using Quadra.Core.Rendering;

namespace Quadra.Core.Assets;

/// <summary>
/// A bitmap font in the original <c>.fnt</c> format (<c>Fontdata</c> in <c>legacy/source/sprite.cc</c>):
/// 109 glyph slots for the characters from '!' upwards; each present glyph is a width, height and row
/// stride followed by 8-bit pixels. A pixel value of 0 is transparent and 1 to 7 is the brightness of the
/// glyph (1 the dark outline, 7 the bright fill); drawing a glyph in a colour scales the colour by it.
/// </summary>
public sealed class LegacyFont
{
    private const int GlyphSlots = 141 - 32;

    private sealed record Glyph(int Width, int Height, int Stride, byte[] Pixels);

    private readonly Glyph?[] _glyphs = new Glyph?[GlyphSlots];
    private readonly int[] _advance = new int[GlyphSlots];
    private readonly int _shrink;

    /// <param name="shrink">How much neighbouring glyphs overlap (2 for the normal font, 1 for the number font).</param>
    public LegacyFont(byte[] data, int shrink)
    {
        _shrink = shrink;
        int pos = 0;
        for (int i = 0; i < GlyphSlots; i++)
        {
            int width = ReadInt(data, ref pos);
            if (width == 0)
                continue;
            int height = ReadInt(data, ref pos);
            int stride = ReadInt(data, ref pos);
            if (width < 0 || height < 0 || stride < width || pos + stride * height > data.Length)
                throw new InvalidDataException($"Bad glyph {i} in font.");
            _glyphs[i] = new Glyph(width, height, stride, data[pos..(pos + stride * height)]);
            pos += stride * height;
            _advance[i] = Math.Max(width - shrink, 3);
        }
        if (pos != data.Length)
            throw new InvalidDataException("Font has trailing data.");
        Height = _glyphs.First(g => g is not null)!.Height;
    }

    /// <summary>Height of the tallest glyph row (all glyphs of the originals share one height).</summary>
    public int Height { get; }

    public int GlyphCount => _glyphs.Count(g => g is not null);

    public int Measure(string text)
    {
        int width = 0;
        foreach (char c in text)
            width += _advance[Slot(c)];
        return width + _shrink;
    }

    public void Draw(PixelBuffer target, string text, int x, int y, Rgb color)
    {
        foreach (char c in text)
        {
            int slot = Slot(c);
            if (c != ' ' && _glyphs[slot] is { } glyph)
                for (int row = 0; row < glyph.Height; row++)
                    for (int col = 0; col < glyph.Width; col++)
                    {
                        int v = glyph.Pixels[row * glyph.Stride + col];
                        if (v != 0)
                            target.Put(x + col, y + row, PixelBuffer.Pack(
                                (byte)(color.R * v / 7), (byte)(color.G * v / 7), (byte)(color.B * v / 7)));
                    }
            x += _advance[slot];
        }
    }

    /// <summary>Glyph slot of a character; anything that is not printable ASCII acts like a space.</summary>
    private static int Slot(char c) => c is > ' ' and < (char)127 ? c - 33 : 'i' - 33;

    private static int ReadInt(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length)
            throw new InvalidDataException("Font ends early.");
        int value = data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24);
        pos += 4;
        return value;
    }
}
