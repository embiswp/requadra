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
        for (int i = 0; i < text.Length;)
            width += _advance[Advance(Translate(text, ref i))];
        return width + _shrink;
    }

    public void Draw(PixelBuffer target, string text, int x, int y, Rgb color)
    {
        for (int i = 0; i < text.Length;)
        {
            int slot = Translate(text, ref i);
            if (slot >= 0 && _glyphs[slot] is { } glyph)
                for (int row = 0; row < glyph.Height; row++)
                    for (int col = 0; col < glyph.Width; col++)
                    {
                        int v = glyph.Pixels[row * glyph.Stride + col];
                        if (v != 0)
                            target.Put(x + col, y + row, PixelBuffer.Pack(
                                (byte)(color.R * v / 7), (byte)(color.G * v / 7), (byte)(color.B * v / 7)));
                    }
            x += _advance[Advance(slot)];
        }
    }

    /// <summary>A space or a character the font does not know takes the width of an 'i' and draws nothing.</summary>
    private static int Advance(int slot) => slot < 0 ? 'i' - 33 : slot;

    /// <summary>
    /// Glyph slot of the character at <paramref name="i"/> (advancing it); -1 for a space and -2 for a
    /// character that is not in the font. Accented Latin-1 letters have slots of their own or fall back
    /// to the plain letter; 0xB7 followed by a digit 0-4 is one of the special symbols
    /// (<c>Fontdata::translate</c> in <c>legacy/source/sprite.cc</c>).
    /// </summary>
    private static int Translate(string text, ref int i)
    {
        char c = text[i++];
        if (c == ' ')
            return -1;
        if (c > ' ' && c < 127)
            return c - 33;
        int glyph;
        switch (c)
        {
            case '\u00b7':
                if (i >= text.Length)
                    return -2;
                glyph = text[i++] - '0' + 133;
                if (glyph is < 133 or > 137)
                    return -2;
                break;
            case '\u00c7': glyph = 'C'; break;
            case '\u00f9': glyph = 139; break;
            case '\u00fb': glyph = 140; break;
            case '\u00fc': glyph = 'u'; break;
            case '\u00e9': glyph = 127; break;
            case '\u00e8': glyph = 128; break;
            case '\u00ea': glyph = 129; break;
            case '\u00eb': glyph = 130; break;
            case '\u00e0': glyph = 131; break;
            case '\u00e2': case '\u00e4': glyph = 'a'; break;
            case '\u00e7': glyph = 132; break;
            case '\u00d9': case '\u00db': case '\u00dc': glyph = 'U'; break;
            case '\u00c0': case '\u00c2': case '\u00c4': glyph = 'A'; break;
            case '\u00c9': case '\u00c8': case '\u00ca': case '\u00cb': glyph = 'E'; break;
            case '\u00f4': glyph = 138; break;
            case '\u00f2': case '\u00f6': glyph = 'o'; break;
            case '\u00ff': glyph = 'y'; break;
            case '\u00d2': case '\u00d4': case '\u00d6': glyph = 'O'; break;
            case '\u00ee': glyph = 141; break;
            case '\u00ec': case '\u00ef': glyph = 'i'; break;
            case '\u00cc': case '\u00ce': case '\u00cf': glyph = 'I'; break;
            default: return -2;
        }
        return glyph - 33;
    }

    private static int ReadInt(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length)
            throw new InvalidDataException("Font ends early.");
        int value = data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24);
        pos += 4;
        return value;
    }
}
