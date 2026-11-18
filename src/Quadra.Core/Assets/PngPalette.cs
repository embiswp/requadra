namespace Quadra.Core.Assets;

/// <summary>A palette entry.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// Reads the palette (the <c>PLTE</c> chunk) of an indexed PNG. The original game paints its blocks
/// with colour ramps that live in the palette of the level's background image, so the palette is
/// needed even though a normal image loader only returns the final colours.
/// </summary>
public static class PngPalette
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static Rgb[] Read(byte[] png)
    {
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG file.");

        int pos = 8;
        while (pos + 8 <= png.Length)
        {
            int length = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            if (type == "PLTE")
            {
                if (length % 3 != 0 || pos + 8 + length > png.Length)
                    throw new InvalidDataException("Bad PLTE chunk.");
                var palette = new Rgb[length / 3];
                for (int i = 0; i < palette.Length; i++)
                    palette[i] = new Rgb(png[pos + 8 + i * 3], png[pos + 9 + i * 3], png[pos + 10 + i * 3]);
                return palette;
            }
            if (type == "IDAT")
                break;   // the palette must come before the image data
            pos += 12 + length;
        }
        throw new InvalidDataException("PNG has no palette.");
    }
}
