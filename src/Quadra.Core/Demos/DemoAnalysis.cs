using Quadra.Core.Net;

namespace Quadra.Core.Demos;

/// <summary>Per-player figures that can be derived from the packet stream alone.</summary>
public sealed record PlayerTally(byte Slot, string Name, byte Team)
{
    public int Deaths { get; init; }
    public int Stamps { get; init; }
    public int Respawns { get; init; }
}

/// <summary>
/// Packet-level pass over a demo: no board simulation yet, only what the server
/// already decided (roster, deaths, stamped blocks).
/// </summary>
public sealed record DemoAnalysis(IReadOnlyList<PlayerTally> Players, uint LastFrame)
{
    public static DemoAnalysis Analyze(DemoFile demo)
    {
        var tallies = new SortedDictionary<byte, PlayerTally>();
        uint lastFrame = 0;

        foreach (var (frame, packet) in demo.Packets)
        {
            lastFrame = Math.Max(lastFrame, frame);
            switch (packet)
            {
                case PlayerPacket p:
                    tallies[p.Pos] = new PlayerTally(p.Pos, p.Name, p.Team);
                    break;
                case DeadPacket d when tallies.TryGetValue(d.Slot, out var t):
                    tallies[d.Slot] = t with { Deaths = t.Deaths + 1 };
                    break;
                case StampBlockPacket s when tallies.TryGetValue(s.Slot, out var t):
                    tallies[s.Slot] = t with { Stamps = t.Stamps + 1 };
                    break;
                case RespawnPacket r when tallies.TryGetValue(r.Slot, out var t):
                    tallies[r.Slot] = t with { Respawns = t.Respawns + 1 };
                    break;
            }
        }

        return new DemoAnalysis(tallies.Values.ToList(), lastFrame);
    }
}
