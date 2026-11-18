using System.Text;

namespace Quadra.Core.Net;

/// <summary>Writes the Quadra wire format (big-endian). Mirrors the write side of Net_buf in source/net_buf.h.</summary>
public sealed class NetWriter
{
    private readonly List<byte> _data = [];

    public int Length => _data.Count;

    public void WriteByte(byte v) => _data.Add(v);

    public void WriteBool(bool v) => _data.Add(v ? (byte)1 : (byte)0);

    public void WriteWord(ushort v)
    {
        _data.Add((byte)(v >> 8));
        _data.Add((byte)v);
    }

    public void WriteDword(uint v)
    {
        _data.Add((byte)(v >> 24));
        _data.Add((byte)(v >> 16));
        _data.Add((byte)(v >> 8));
        _data.Add((byte)v);
    }

    public void WriteInt(int v) => WriteDword(unchecked((uint)v));

    public void WriteBytes(ReadOnlySpan<byte> v)
    {
        foreach (var b in v)
            _data.Add(b);
    }

    /// <summary>
    /// Writes a NUL-terminated string cut to <paramref name="maxSize"/> - 1 bytes. Characters outside
    /// Latin-1 become '?', like the one-byte-per-character strings of the original.
    /// </summary>
    public void WriteString(string v, int maxSize)
    {
        var bytes = Encoding.Latin1.GetBytes(v);
        WriteBytes(bytes.AsSpan(0, Math.Min(bytes.Length, maxSize - 1)));
        _data.Add(0);
    }

    public byte[] ToArray() => [.. _data];
}
