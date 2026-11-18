using Quadra.Core.Demos;
using Quadra.Core.Net;

namespace Quadra.Tests;

public class DemoTests
{
    public static IEnumerable<object[]> DemoPaths =>
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

    private static DemoFile Load(string name) => DemoFile.Load(Path.Combine(DemoDir(), name));

    [Fact]
    public void Repository_contains_the_four_bundled_demos() =>
        Assert.Equal(4, Directory.EnumerateFiles(DemoDir(), "*.rec").Count());

    [Theory, MemberData(nameof(DemoPaths))]
    public void Demo_decompresses_and_has_game_packet(string name)
    {
        var demo = Load(name);
        Assert.False(demo.LegacyMode);
        Assert.NotNull(demo.GameServer);
        Assert.Equal(20, demo.GameServer!.Version);
        Assert.Equal("Q1.1 Test", demo.GameServer.Name);
    }

    [Theory, MemberData(nameof(DemoPaths))]
    public void Every_packet_is_decoded_without_leftovers(string name)
    {
        var demo = Load(name);
        Assert.NotEmpty(demo.Packets);
        Assert.DoesNotContain(demo.Packets, d => d.Packet is RawPacket);
        Assert.DoesNotContain(demo.Packets, d => d.Packet.Unread != 0);

        // Net version 20 stored shorter Player, StampBlock, Lines, Gone and EndGame packets than the
        // current layout; the missing trailing fields read as zero (as in the C++ client).
        // Every other packet must match its layout exactly.
        Assert.DoesNotContain(demo.Packets, d => d.Packet.Truncated &&
            d.Packet is not (PlayerPacket or StampBlockPacket or LinesPacket or GonePacket or EndGamePacket));
    }

    [Theory, MemberData(nameof(DemoPaths))]
    public void Old_player_packets_default_the_fields_they_lack(string name)
    {
        foreach (var player in Load(name).Packets.Select(d => d.Packet).OfType<PlayerPacket>())
        {
            Assert.True(player.Truncated);
            Assert.Equal(0, player.Handicap);
            Assert.Equal(0u, player.PlayerId);
            Assert.InRange(player.HRepeat, 0, 3);
        }
    }

    [Theory, MemberData(nameof(DemoPaths))]
    public void Old_stamp_packets_lack_the_later_rotation_and_hold_fields(string name)
    {
        var stamps = Load(name).Packets.Select(d => d.Packet).OfType<StampBlockPacket>().ToList();
        Assert.NotEmpty(stamps);
        foreach (var stamp in stamps)
        {
            Assert.True(stamp.Truncated);
            Assert.Equal(0, stamp.BlockRotated);
            Assert.Equal(0, stamp.TimeHeld);
            Assert.InRange(stamp.X, 0, 18);
            Assert.InRange(stamp.Y, 0, 35);
            Assert.InRange(stamp.Rotate, 0, 3);
        }
    }

    [Theory, MemberData(nameof(DemoPaths))]
    public void Frames_never_go_backwards(string name)
    {
        uint previous = 0;
        foreach (var (frame, _) in Load(name).Packets)
        {
            Assert.True(frame >= previous, $"frame {frame} after {previous}");
            previous = frame;
        }
    }

    [Theory, MemberData(nameof(DemoPaths))]
    public void Rules_match_the_stored_summary(string name)
    {
        var demo = Load(name);
        var gs = demo.GameServer!;
        var s = demo.Summary;
        Assert.Equal(gs.Name, s["name"]);
        Assert.Equal(gs.Version.ToString(), s["version"]);
        Assert.Equal(gs.LevelStart.ToString(), s["rules/levelstart"]);
        Assert.Equal(gs.ComboMin.ToString(), s["rules/mincombo"]);
        Assert.Equal(((int)gs.GameEnd).ToString(), s["end/type"]);
        Assert.Equal(gs.GameEndValue.ToString(), s["end/value"]);
        Assert.Equal(gs.NoLevelUp ? "0" : "1", s["rules/levelup"]);
    }

    [Theory, MemberData(nameof(DemoPaths))]
    public void Roster_and_deaths_match_the_stored_summary(string name)
    {
        var demo = Load(name);
        var analysis = DemoAnalysis.Analyze(demo);
        int summaryPlayers = demo.Summary.Keys.Count(k => k.StartsWith("players/") && k.EndsWith("/name"));
        Assert.Equal(summaryPlayers, analysis.Players.Count);

        foreach (var player in analysis.Players)
        {
            int i = Enumerable.Range(0, 8).First(n => demo.Summary.GetValueOrDefault($"players/{n}/name") == player.Name);
            Assert.Equal(demo.Summary[$"players/{i}/team"], player.Team.ToString());
            Assert.Equal(demo.Summary[$"players/{i}/deaths"], player.Deaths.ToString());
        }
    }

    [Theory, MemberData(nameof(DemoPaths))]
    public void Last_frame_does_not_exceed_recorded_duration(string name)
    {
        var demo = Load(name);
        var analysis = DemoAnalysis.Analyze(demo);
        Assert.True(analysis.LastFrame <= uint.Parse(demo.Summary["duration"]));
    }
}

public class NetReaderTests
{
    [Fact]
    public void Reads_big_endian_values()
    {
        var r = new NetReader([0x12, 0x34, 0x00, 0x00, 0x01, 0x02, 0xFF, 0xFF, 0xFF, 0xFF]);
        Assert.Equal(0x1234, r.ReadWord());
        Assert.Equal(0x0102u, r.ReadDword());
        Assert.Equal(-1, r.ReadInt());
        Assert.False(r.Overrun);
    }

    [Fact]
    public void Reading_past_the_end_yields_zero_and_flags_overrun()
    {
        var r = new NetReader([7]);
        Assert.Equal(7, r.ReadByte());
        Assert.Equal(0, r.ReadByte());
        Assert.True(r.Overrun);
    }

    [Fact]
    public void Strings_replace_control_characters_and_report_missing_terminator()
    {
        var ok = new NetReader([(byte)'a', 7, (byte)'b', 0]);
        Assert.Equal("a b", ok.ReadString(8, out bool terminated));
        Assert.True(terminated);

        var tooLong = new NetReader([(byte)'a', (byte)'b', (byte)'c', (byte)'d']);
        tooLong.ReadString(3, out terminated);
        Assert.False(terminated);
    }
}
