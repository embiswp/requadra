namespace Quadra.Core.Net;

/// <summary>
/// The TCP framing of game packets: a little-endian 16-bit size, then that many bytes. A size of zero
/// or of <see cref="MaxPacketSize"/> and more is garbage and ends the connection. The size "//" (two
/// slashes, which is how a browser's text request starts) switches the original to a plain-text mode
/// that this port does not offer.
/// </summary>
public static class PacketFraming
{
    public const int MaxPacketSize = 4096;

    public static byte[] Frame(byte[] payload)
    {
        if (payload.Length is 0 or >= MaxPacketSize)
            throw new ArgumentException($"A packet is 1 to {MaxPacketSize - 1} bytes.", nameof(payload));

        var framed = new byte[payload.Length + 2];
        framed[0] = (byte)payload.Length;
        framed[1] = (byte)(payload.Length >> 8);
        payload.CopyTo(framed, 2);
        return framed;
    }

    /// <summary>
    /// Takes the next whole packet off the front of <paramref name="buffer"/> (<paramref name="count"/>
    /// bytes are valid). Returns the packet and how many bytes it used, null if more must arrive first,
    /// and throws <see cref="InvalidDataException"/> on garbage.
    /// </summary>
    public static (byte[] Payload, int Used)? TryTake(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 2)
            return null;

        int size = buffer[0] | (buffer[1] << 8);
        if (size == 0 || size >= MaxPacketSize)
            throw new InvalidDataException($"Garbage on the connection (packet size {size:X4}).");
        if (buffer.Length < size + 2)
            return null;

        return (buffer.Slice(2, size).ToArray(), size + 2);
    }
}
