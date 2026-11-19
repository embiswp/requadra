using Quadra.Core.Demos;
using Quadra.Core.Net;

namespace Quadra.Tests;

public class PacketEncoderTests
{
    public static IEnumerable<object[]> DemoNames =>
        Directory.EnumerateFiles(DemoDir(), "*.rec").OrderBy(p => p).Select(p => new object[] { Path.GetFileName(p) });

    private static string DemoDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "legacy", "demos");
            if (Directory.Exists(candidate))
                return candidate;
        }
        throw new DirectoryNotFoundException("Could not find the repository's legacy/demos directory.");
    }

    private static IEnumerable<byte[]> RecordedPackets(string name) =>
        DemoFile.Load(Path.Combine(DemoDir(), name)).Packets.Select(p => p.Raw!);

    // Packets that are complete in the 1.1 recordings must come out byte for byte as they went in;
    // the ones 1.1 stored short are written in full, and then read back the same way.
    [Theory, MemberData(nameof(DemoNames))]
    public void Recorded_packets_encode_to_the_bytes_they_were_read_from(string name)
    {
        int exact = 0;
        foreach (var raw in RecordedPackets(name))
        {
            var packet = PacketDecoder.Decode(raw);
            var encoded = PacketEncoder.Encode(packet);

            if (packet.Truncated)
            {
                Assert.True(encoded.Length > raw.Length);
                Assert.Equal(raw, encoded[..raw.Length]);
            }
            else
            {
                exact++;
                Assert.Equal(raw, encoded);
            }

            // Whatever was written reads back as the same packet and writes the same bytes again.
            Assert.Equal(encoded, PacketEncoder.Encode(PacketDecoder.Decode(encoded)));
        }
        Assert.True(exact > 100);
    }

    [Fact]
    public void A_game_server_packet_for_version_22_carries_the_attacks_and_version_20_does_not()
    {
        var packet = new GameServerPacket
        {
            Id = (byte)PacketType.GameServer,
            Version = 22,
            Name = "Rules",
            NormalAttack = new Attack(AttackType.Blind, 5),
            Players = [new NetPlayer(0, 2, "Ann", 77, 1)],
        };

        var v22 = PacketEncoder.Encode(packet);
        var v20 = PacketEncoder.Encode(packet with { Version = 20 });

        Assert.Equal(4 * 5, v22.Length - v20.Length);
        var back = (GameServerPacket)PacketDecoder.Decode(v22);
        Assert.Equal(new Attack(AttackType.Blind, 5), back.NormalAttack);
        Assert.Equal(new NetPlayer(0, 2, "Ann", 77, 1), Assert.Single(back.Players));
    }

    [Fact]
    public void Strings_are_cut_to_the_field_size_and_zero_terminated()
    {
        var packet = new ChatPacket(1, -1, new string('x', 400)) { Id = (byte)PacketType.Chat };

        var back = (ChatPacket)PacketDecoder.Decode(PacketEncoder.Encode(packet));

        Assert.Equal(255, back.Text.Length);
        Assert.Equal(-1, back.ToTeam);
    }

    [Fact]
    public void Repeat_speeds_travel_in_both_forms_of_the_player_packet()
    {
        var packet = new PlayerPacket
        {
            Id = (byte)PacketType.Player, Name = "Ann", HRepeat = 3, VRepeat = 5, Pos = 2, PlayerId = 9, Uid = 4,
        };

        foreach (var version in new[] { 20, 23 })
        {
            var back = (PlayerPacket)PacketDecoder.Decode(PacketEncoder.Encode(packet, version));
            Assert.Equal((3, 5, 2, 9u), (back.HRepeat, back.VRepeat, back.Pos, back.PlayerId));
        }
    }

    [Fact]
    public void Lines_moves_and_stamps_round_trip()
    {
        Packet[] packets =
        [
            new LinesPacket { Id = (byte)PacketType.Lines, Slot = 1, Nb = 3, Nc = 2, Lx = 7, Sender = 0, Attack = new Attack(AttackType.None, 1), HolePos = [1, 2, 3] },
            new MovesPacket([1, 2, 3, 0, 255]) { Id = (byte)PacketType.Moves, Slot = 4 },
            new StampBlockPacket { Id = (byte)PacketType.StampBlock, Slot = 2, X = 7, Y = 28, Rotate = 3, Score = 12, Date = 513, BlockRotated = 1, TimeHeld = 700 },
            new DeadPacket(true) { Id = (byte)PacketType.Dead, Slot = 5 },
            new GonePacket(false) { Id = (byte)PacketType.Gone, Slot = 6 },
            new PausePacket(-1) { Id = (byte)PacketType.Pause },
            new ServerRandomPacket(0xDEADBEEF) { Id = (byte)PacketType.ServerRandom },
            new RespawnPacket { Id = (byte)PacketType.Respawn, Slot = 3 },
        ];

        foreach (var packet in packets)
        {
            var bytes = PacketEncoder.Encode(packet);
            var back = PacketDecoder.Decode(bytes);
            Assert.False(back.Truncated);
            Assert.Equal(0, back.Unread);
            Assert.Equal(bytes, PacketEncoder.Encode(back));
            Assert.Equal(packet.GetType(), back.GetType());
        }
    }

    [Fact]
    public void An_unknown_packet_is_written_as_it_was_read()
    {
        var raw = new byte[] { (byte)PacketType.GameStat, 9, 8, 7 };

        Assert.Equal(raw, PacketEncoder.Encode(PacketDecoder.Decode(raw)));
    }
}

public class PacketFramingTests
{
    [Fact]
    public void A_frame_is_a_little_endian_size_then_the_packet()
    {
        var framed = PacketFraming.Frame(new byte[300]);

        Assert.Equal(new byte[] { 0x2C, 0x01 }, framed[..2]);
        Assert.Equal(302, framed.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    public void Empty_and_oversize_packets_cannot_be_framed(int size) =>
        Assert.Throws<ArgumentException>(() => PacketFraming.Frame(new byte[size]));

    [Fact]
    public void Packets_are_taken_off_a_stream_one_at_a_time_and_a_partial_one_waits()
    {
        var stream = PacketFraming.Frame([1, 2, 3]).Concat(PacketFraming.Frame([9])).Concat(new byte[] { 5, 0, 1 }).ToArray();

        var first = PacketFraming.TryTake(stream)!.Value;
        Assert.Equal(new byte[] { 1, 2, 3 }, first.Payload);
        var second = PacketFraming.TryTake(stream.AsSpan(first.Used))!.Value;
        Assert.Equal(new byte[] { 9 }, second.Payload);
        Assert.Null(PacketFraming.TryTake(stream.AsSpan(first.Used + second.Used)));
        Assert.Null(PacketFraming.TryTake(new byte[1]));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 1 })]
    [InlineData(new byte[] { 0, 0x10, 1 })]
    public void A_zero_or_oversize_length_is_garbage(byte[] bytes) =>
        Assert.Throws<InvalidDataException>(() => PacketFraming.TryTake(bytes));
}
