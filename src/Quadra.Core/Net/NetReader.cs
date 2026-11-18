using System.Text;

namespace Quadra.Core.Net;

/// <summary>
/// Reads the Quadra wire format (big-endian). Mirrors Net_buf in source/net_buf.h:
/// reading past the end yields zeros instead of failing, because the C++ buffer is
/// zero-filled. <see cref="Overrun"/> records that it happened.
/// </summary>
public sealed class NetReader
{
    private readonly byte[] _data;

    public NetReader(byte[] data) => _data = data;

    public int Position { get; private set; }
    public int Length => _data.Length;
    public bool Overrun { get; private set; }
    public int Unread => Math.Max(0, _data.Length - Position);

    public byte ReadByte()
    {
        if (Position >= _data.Length)
        {
            Overrun = true;
            Position++;
            return 0;
        }
        return _data[Position++];
    }

    public bool ReadBool() => ReadByte() != 0;

    public ushort ReadWord() => (ushort)((ReadByte() << 8) | ReadByte());

    public uint ReadDword() => ((uint)ReadByte() << 24) | ((uint)ReadByte() << 16) | ((uint)ReadByte() << 8) | ReadByte();

    public int ReadInt() => unchecked((int)ReadDword());

    /// <summary>
    /// Reads a NUL-terminated string of at most <paramref name="maxSize"/> bytes
    /// (including the terminator). Control characters become spaces, as in the C++ code.
    /// </summary>
    public string ReadString(int maxSize, out bool ok)
    {
        var sb = new StringBuilder();
        for (int remaining = maxSize; remaining > 0; remaining--)
        {
            sbyte c = unchecked((sbyte)ReadByte());
            if (c > 0 && c < ' ')
                c = (sbyte)' ';
            if (c == 0)
            {
                ok = true;
                return sb.ToString();
            }
            sb.Append((char)(byte)c);
        }
        ok = false;
        return sb.ToString();
    }
}
