namespace Quadra.Core.Rendering;

/// <summary>
/// A software canvas of 32-bit pixels (bytes in the order red, green, blue, alpha, which is what
/// MonoGame's <c>Color</c> uses). The original game draws everything into a bitmap this way; all
/// drawing is clipped to the canvas.
/// </summary>
public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }

    public static uint Pack(byte r, byte g, byte b, byte a = 255) =>
        r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    public static uint Pack(Assets.Rgb color) => Pack(color.R, color.G, color.B);

    public const uint Transparent = 0;

    /// <summary>Wraps decoded image data (bytes in the order red, green, blue, alpha).</summary>
    public static PixelBuffer FromRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != width * height * 4)
            throw new ArgumentException("Pixel data does not match the size.", nameof(rgba));
        var buffer = new PixelBuffer(width, height);
        for (int i = 0; i < buffer.Pixels.Length; i++)
            buffer.Pixels[i] = Pack(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
        return buffer;
    }

    /// <summary>
    /// Makes every pixel of one colour transparent. The original's sprites are indexed images whose
    /// palette entry 0 means "see through"; once decoded to full colour that entry's colour is the key.
    /// </summary>
    public void MakeTransparent(Assets.Rgb key)
    {
        uint keyed = Pack(key);
        for (int i = 0; i < Pixels.Length; i++)
            if (Pixels[i] == keyed)
                Pixels[i] = Transparent;
    }

    public void Clear(uint color = Transparent) => Array.Fill(Pixels, color);

    public uint Get(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? Pixels[y * Width + x] : Transparent;

    public void Put(int x, int y, uint color)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height)
            Pixels[y * Width + x] = color;
    }

    /// <summary>Horizontal line of <paramref name="length"/> pixels starting at (x, y).</summary>
    public void HLine(int y, int x, int length, uint color)
    {
        if ((uint)y >= (uint)Height)
            return;
        int end = Math.Min(x + length, Width);
        for (int i = Math.Max(x, 0); i < end; i++)
            Pixels[y * Width + i] = color;
    }

    /// <summary>Vertical line of <paramref name="length"/> pixels starting at (x, y).</summary>
    public void VLine(int x, int y, int length, uint color)
    {
        if ((uint)x >= (uint)Width)
            return;
        int end = Math.Min(y + length, Height);
        for (int j = Math.Max(y, 0); j < end; j++)
            Pixels[j * Width + x] = color;
    }

    /// <summary>Copies <paramref name="source"/> onto this canvas, skipping fully transparent pixels.</summary>
    public void Blit(PixelBuffer source, int x, int y)
    {
        for (int j = 0; j < source.Height; j++)
            for (int i = 0; i < source.Width; i++)
            {
                uint p = source.Pixels[j * source.Width + i];
                if ((p >> 24) != 0)
                    Put(x + i, y + j, p);
            }
    }
}
