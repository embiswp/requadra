using Quadra.Core.Demos;
using Quadra.Core.Net;

namespace Quadra.Tests;

public class RecordingTests
{
    [Theory]
    [InlineData("demo00.rec")]
    [InlineData("demo02.rec")]
    public void A_hosted_game_is_recorded_so_that_it_replays_to_the_same_result(string name)
    {
        var original = DemoFile.Load(Path.Combine(NetworkGameTests.DemoDir(), name));
        var wanted = DemoReplay.Run(original).ToDictionary(r => r.Slot);

        DemoRecorder? recorder = null;
        NetworkGameTests.PlayDemo(name, host => host.Recorder = recorder = new DemoRecorder(host.State.Rules));

        var recorded = DemoFile.Parse(recorder!.ToBytes());
        Assert.Equal(original.GameServer!.GameSeed, recorded.GameServer!.GameSeed);
        Assert.Equal(original.GameServer.Survivor, recorded.GameServer.Survivor);

        var got = DemoReplay.Run(recorded).ToDictionary(r => r.Slot);
        Assert.Equal(wanted.Keys, got.Keys);
        foreach (var (slot, want) in wanted)
            Assert.Equal((want.Score, want.Lines, want.Frags, want.Deaths), (got[slot].Score, got[slot].Lines, got[slot].Frags, got[slot].Deaths));
    }

    [Fact]
    public void The_frame_of_each_packet_is_kept()
    {
        var recorder = new DemoRecorder(new GameServerPacket { Id = (byte)PacketType.GameServer, Name = "x" });
        recorder.Add(250, new ChatPacket(-1, -1, "hello") { Id = (byte)PacketType.Chat });

        var demo = DemoFile.Parse(recorder.ToBytes());

        var chat = Assert.Single(demo.Packets);
        Assert.Equal(250u, chat.Frame);
        Assert.Equal("hello", Assert.IsType<ChatPacket>(chat.Packet).Text);
    }
}
