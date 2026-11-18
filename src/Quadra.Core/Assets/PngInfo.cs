namespace Quadra.Core.Assets;

/// <summary>Reads the size of a PNG from its header, so layouts can use the images' real dimensions.</summary>
public static class PngInfo
{
    public static (int Width, int Height) Size(byte[] png)
    {
        // 8 byte signature, then the IHDR chunk: length, "IHDR", width, height (big-endian).
        if (png.Length < 24 || png[1] != 'P' || png[2] != 'N' || png[3] != 'G')
            throw new InvalidDataException("Not a PNG file.");
        int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (width, height);
    }
}
