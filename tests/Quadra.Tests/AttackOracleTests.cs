using Quadra.Core.Demos;
using Quadra.Core.Game;
using Quadra.Core.Net;
using Quadra.Core.Rules;

namespace Quadra.Tests;

/// <summary>
/// The recorded games contain the garbage every client worked out for its own players. The rules in
/// <see cref="AttackRules"/> must produce the same lines from the stamps alone.
/// </summary>
public class AttackOracleTests
{
    public static IEnumerable<object[]> DemoNames => PacketEncoderTests.DemoNames;

    private static string DemoDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "legacy", "demos");
            if (Directory.Exists(candidate))
                return candidate;
        }
        throw new DirectoryNotFoundException();
    }

    private static (Dictionary<(int, int), List<(int Nb, int Nc, int Lx)>> Expected, Dictionary<(int, int), List<(int Nb, int Nc, int Lx)>> Recorded)
        Run(string name)
    {
        var demo = DemoFile.Load(Path.Combine(DemoDir(), name));
        var gs = demo.GameServer!;
        var state = new MultiplayerState(gs);
        var expected = new Dictionary<(int, int), List<(int, int, int)>>();
        var recorded = new Dictionary<(int, int), List<(int, int, int)>>();

        state.Stamped += (player, result) =>
        {
            foreach (var clear in result.Clears)
                foreach (var attack in AttackRules.For(clear, gs.ComboMin))
                    foreach (var victim in state.Players.Values.Where(v => v.Team != player.Team))
                    {
                        var (count, combo) = AttackRules.ForReceiver(attack, player.Handicap, victim.Handicap);
                        if (count > 0)
                            expected.GetOrAdd((player.Slot, victim.Slot)).Add((count, combo, result.LastColumn));
                    }
        };

        foreach (var (_, packet) in demo.Packets)
        {
            if (packet is LinesPacket l && l.Sender != PlayerSim.NoAttacker)
                recorded.GetOrAdd((l.Sender, l.Slot)).Add((l.Nb, l.Nc, l.Lx));
            state.Apply(packet);
        }
        return (expected, recorded);
    }

    internal static int RecordedCount(string name) => Run(name).Recorded.Values.Sum(v => v.Count);

    [Theory, MemberData(nameof(DemoNames))]
    public void Every_recorded_attack_is_what_the_rules_send(string name)
    {
        var (expected, recorded) = Run(name);

        foreach (var key in expected.Keys.Union(recorded.Keys).OrderBy(k => k))
        {
            var want = expected.GetValueOrDefault(key) ?? [];
            var got = recorded.GetValueOrDefault(key) ?? [];
            Assert.True(want.SequenceEqual(got),
                $"{name} attacker {key.Item1} -> victim {key.Item2}:\n expected {string.Join(" ", want)}\n recorded {string.Join(" ", got)}");
        }
    }
}

internal static class DictionaryExtensions
{
    public static List<T> GetOrAdd<TKey, T>(this Dictionary<TKey, List<T>> d, TKey key) where TKey : notnull
    {
        if (!d.TryGetValue(key, out var list))
            d[key] = list = [];
        return list;
    }
}

public class AttackOracleCoverageTests
{
    [Fact]
    public void The_recordings_contain_enough_attacks_to_mean_something()
    {
        int attacks = PacketEncoderTests.DemoNames
            .Select(n => (string)n[0])
            .Sum(name => AttackOracleTests.RecordedCount(name));

        Assert.True(attacks > 50, $"only {attacks} recorded attacks");
    }
}
