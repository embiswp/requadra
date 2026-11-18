using Quadra.Core.Demos;
using Quadra.Core.Net;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: DemoInfo <file.rec> [...]");
    return 1;
}

foreach (var path in args)
{
    var demo = DemoFile.Load(path);
    var gs = demo.GameServer;
    Console.WriteLine($"== {path}");
    if (gs is null)
    {
        Console.WriteLine("  legacy single-player demo (no packets)");
        continue;
    }

    Console.WriteLine($"  game '{gs.Name}'  net version {gs.Version}  seed {gs.GameSeed}");
    Console.WriteLine($"  rules: end={gs.GameEnd} {gs.GameEndValue}, level start {gs.LevelStart}, min combo {gs.ComboMin}, level up {!gs.NoLevelUp}");

    var analysis = DemoAnalysis.Analyze(demo);
    Console.WriteLine($"  {demo.Packets.Count} packets, last frame {analysis.LastFrame}, summary duration {demo.Summary.GetValueOrDefault("duration", "?")}");
    foreach (var p in analysis.Players)
    {
        string score = FindSummary(demo, p.Name, "score");
        string lines = FindSummary(demo, p.Name, "lines");
        Console.WriteLine($"  slot {p.Slot} {p.Name,-8} team {p.Team}  deaths {p.Deaths,2}  stamps {p.Stamps,4}   (summary: score {score}, lines {lines})");
    }

    foreach (var g in demo.Packets.GroupBy(d => d.Packet.Type).OrderBy(g => (int)g.Key))
        Console.WriteLine($"    {g.Key,-14} {g.Count()}");
}

return 0;

static string FindSummary(DemoFile demo, string player, string field)
{
    for (int i = 0; i < 8; i++)
        if (demo.Summary.GetValueOrDefault($"players/{i}/name") == player)
            return demo.Summary.GetValueOrDefault($"players/{i}/{field}", "?");
    return "?";
}
