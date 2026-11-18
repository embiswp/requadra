using System.IO.Compression;
using System.Text;
using Quadra.Core.Net;

namespace Quadra.Core.Demos;

/// <summary>A packet recorded at a given overmind frame.</summary>
/// <summary>A recorded packet at its frame, with the bytes it was decoded from when they are known.</summary>
public readonly record struct DemoPacket(uint Frame, Packet Packet, byte[]? Raw = null)
{
    public void Deconstruct(out uint frame, out Packet packet) => (frame, packet) = (Frame, Packet);
}

/// <summary>
/// A Quadra demo (.rec) file. Format (see source/recording.cc):
/// <c>uint32 LE uncompressed size</c> followed by a zlib stream of hunks
/// (<c>0</c> seed, <c>1</c> single player, <c>2</c> key data, <c>3</c> final info,
/// <c>11</c> packet, <c>13</c> summary).
/// </summary>
public sealed class DemoFile
{
    private const byte HunkSeed = 0, HunkSingle = 1, HunkData = 2, HunkInfo = 3, HunkPacket = 11, HunkSummary = 13;

    public IReadOnlyList<DemoPacket> Packets { get; private set; } = [];

    /// <summary>First <see cref="GameServerPacket"/>: rules, seed, version.</summary>
    public GameServerPacket? GameServer { get; private set; }

    /// <summary>Key/value lines of the summary hunk (qserv style). Empty if the demo has none.</summary>
    public IReadOnlyDictionary<string, string> Summary { get; private set; } = new Dictionary<string, string>();

    /// <summary>True for 1.0.x single-player demos, which store key presses instead of packets.</summary>
    public bool LegacyMode => Packets.Count == 0;

    public int UncompressedSize { get; private set; }

    /// <summary>Builds a demo from already decoded parts (used by tests and tools).</summary>
    public static DemoFile FromParts(GameServerPacket gameServer, IReadOnlyList<DemoPacket> packets,
        IReadOnlyDictionary<string, string> summary) =>
        new() { GameServer = gameServer, Packets = packets, Summary = summary };

    public static DemoFile Load(string path) => Parse(File.ReadAllBytes(path));

    public static DemoFile Parse(byte[] file)
    {
        if (file.Length < 4)
            throw new InvalidDataException("Demo too short.");

        int expected = BitConverter.ToInt32(file, 0);
        byte[] data = Inflate(file, expected);

        var demo = new DemoFile { UncompressedSize = expected };
        var packets = new List<DemoPacket>();
        var summary = new Dictionary<string, string>();

        int i = 0;
        while (i < data.Length)
        {
            byte hunk = data[i++];
            switch (hunk)
            {
                case HunkSeed:
                    i += 16; // seed + 3 repeat speeds
                    break;
                case HunkSingle:
                    i += 1;
                    break;
                case HunkData:
                    while (i < data.Length && data[i++] != 255) { }
                    break;
                case HunkInfo:
                    i += 40 + 12; // name + score, lines, level
                    break;
                case HunkPacket:
                {
                    uint frame = BitConverter.ToUInt32(data, i);
                    ushort size = BitConverter.ToUInt16(data, i + 4);
                    i += 6;
                    var raw = data[i..(i + size)];
                    var packet = PacketDecoder.Decode(raw);
                    i += size;
                    demo.GameServer ??= packet as GameServerPacket;
                    // Like Playback::read_packet: ignore packets that precede the game packet.
                    if (packet is not GameServerPacket && demo.GameServer is not null)
                        packets.Add(new DemoPacket(frame, packet, raw));
                    break;
                }
                case HunkSummary:
                {
                    int size = (int)BitConverter.ToUInt32(data, i);
                    i += 4;
                    foreach (var line in Encoding.Latin1.GetString(data, i, size).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        int sp = line.IndexOf(' ');
                        if (sp > 0)
                            summary[line[..sp]] = line[(sp + 1)..];
                    }
                    i += size;
                    break;
                }
                default:
                    throw new InvalidDataException($"Bad hunk {hunk} at offset {i - 1}.");
            }
        }

        demo.Packets = packets;
        demo.Summary = summary;
        return demo;
    }

    private static byte[] Inflate(byte[] file, int expected)
    {
        using var input = new MemoryStream(file, 4, file.Length - 4);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(expected);
        z.CopyTo(output);
        if (output.Length != expected)
            throw new InvalidDataException($"Demo size mismatch: header says {expected}, got {output.Length}.");
        return output.ToArray();
    }
}
