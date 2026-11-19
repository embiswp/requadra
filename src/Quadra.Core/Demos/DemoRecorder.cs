using System.IO.Compression;
using Quadra.Core.Net;

namespace Quadra.Core.Demos;

/// <summary>
/// Writes a demo (.rec) file the way <c>Recording</c> in legacy/source/recording.cc does for a hosted game: the
/// game packet first, then every packet the host sends, each with the frame it was sent in. The file is the
/// uncompressed size (uint32 LE) followed by a zlib stream of hunks; <see cref="DemoFile"/> reads it back.
/// </summary>
public sealed class DemoRecorder
{
    private const byte HunkPacket = 11;

    private readonly List<byte> _hunks = [];

    public DemoRecorder(GameServerPacket gameServer)
    {
        Add(0, gameServer with { Players = [] });
    }

    /// <summary>How many packets are in the recording, the game packet included.</summary>
    public int Count { get; private set; }

    public void Add(uint frame, Packet packet)
    {
        var bytes = PacketEncoder.Encode(packet);
        _hunks.Add(HunkPacket);
        _hunks.AddRange(BitConverter.GetBytes(frame));
        _hunks.AddRange(BitConverter.GetBytes((ushort)bytes.Length));
        _hunks.AddRange(bytes);
        Count++;
    }

    public byte[] ToBytes()
    {
        var data = _hunks.ToArray();
        using var file = new MemoryStream();
        file.Write(BitConverter.GetBytes(data.Length));
        using (var z = new ZLibStream(file, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data);
        return file.ToArray();
    }

    public void Save(string path) => File.WriteAllBytes(path, ToBytes());
}
