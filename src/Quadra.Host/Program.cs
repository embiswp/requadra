using System.Net;
using System.Runtime.InteropServices;
using Quadra.Core.Demos;
using Quadra.Core.Net;
using Quadra.Core.Qserv;

// A game host without a player of its own: it takes connections, hands out slots and relays moves. The
// original has no such thing (the host is a player), but this is what the in-game server does without the screen.

var options = HostOptions.Parse(args, out var error);
if (options is null)
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine("""
        Usage: Quadra.Host [--port 3456] [--name "Game name"] [--seed N] [--survivor]
                           [--max-players 8] [--combo-min N]
                           [--end frags|time|points|lines N] [--list http://host/cgi-bin/qserv.pl]
                           [--record game.rec]
        """);
    return 1;
}

var host = new GameHost(new HostRules
{
    Name = options.Name,
    Seed = options.Seed,
    Survivor = options.Survivor,
    MaxPlayers = options.MaxPlayers,
    ComboMin = (byte)options.ComboMin,
    GameEnd = options.End,
    GameEndValue = options.EndValue,
});

if (options.Record is not null)
    host.Recorder = new DemoRecorder(host.State.Rules);

await using var server = new TcpGameServer(host, IPAddress.Any, options.Port);
server.Start();
Console.WriteLine($"Hosting \"{options.Name}\" on port {server.Port} (seed {options.Seed}). Press Ctrl+C to stop.");

LanResponder? lan = null;
try
{
    lan = new LanResponder(host, server.Port);
}
catch (System.Net.Sockets.SocketException e)
{
    Console.WriteLine($"Not answering searches on the local network: {e.Message}");
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
HostAnnouncer? announcer = options.List is null
    ? null
    : new HostAnnouncer(host, new QservClient(http, options.List), server.Port);

using var quit = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    quit.Cancel();
};
// A service manager or container stops a host with SIGTERM; leave the game list cleanly then too.
using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    quit.Cancel();
});

string last = "";
try
{
    while (!quit.IsCancellationRequested)
    {
        await Task.Delay(500, quit.Token);
        var now = string.Join(", ", host.State.Players.Values.Select(p => $"{p.Name} (team {p.Team})"));
        var status = (host.Started ? "started" : "waiting") + ": " + (now.Length == 0 ? "no players" : now);
        if (announcer?.Failed is { } failed)
            status += $" [game list: {failed}]";
        if (status != last)
            Console.WriteLine(last = status);
    }
}
catch (OperationCanceledException)
{
}

lan?.Dispose();
if (options.Record is not null && host.Recorder is { } recorder)
{
    recorder.Save(options.Record);
    Console.WriteLine($"Recorded {recorder.Count} packets to {options.Record}.");
}
if (announcer is not null)
    await announcer.DisposeAsync();
return 0;

internal sealed record HostOptions(int Port, string Name, int Seed, bool Survivor, int MaxPlayers, int ComboMin, Uri? List, GameEnd End = GameEnd.Never, int EndValue = 0, string? Record = null)
{
    public static HostOptions? Parse(string[] args, out string? error)
    {
        int port = TcpGameServer.DefaultPort, maxPlayers = 8, comboMin = 0, seed = Random.Shared.Next();
        string name = "Quadra game";
        bool survivor = false;
        Uri? list = null;
        var end = GameEnd.Never;
        int endValue = 0;
        string? record = null;
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            try
            {
                switch (args[i])
                {
                    case "--port": port = int.Parse(Value()); break;
                    case "--name": name = Value(); break;
                    case "--seed": seed = int.Parse(Value()); break;
                    case "--survivor": survivor = true; break;
                    case "--max-players": maxPlayers = int.Parse(Value()); break;
                    case "--combo-min": comboMin = int.Parse(Value()); break;
                    case "--end":
                        end = Value() switch
                        {
                            "frags" => GameEnd.Frag, "time" => GameEnd.Time, "points" => GameEnd.Points, "lines" => GameEnd.Lines,
                            var other => throw new ArgumentException($"Unknown end condition {other}."),
                        };
                        endValue = int.Parse(Value());
                        break;
                    case "--list": list = new Uri(Value()); break;
                    case "--record": record = Value(); break;
                    default: error = $"Unknown option {args[i]}."; return null;
                }
            }
            catch (Exception e) when (e is ArgumentException or FormatException or UriFormatException or OverflowException)
            {
                error = e.Message;
                return null;
            }
        }

        if (port is < 0 or > 65535 || maxPlayers is < 1 or > 8 || comboMin is < 0 or > 255 || endValue < 0)
        {
            error = "A value is out of range.";
            return null;
        }
        return new HostOptions(port, name, seed, survivor, maxPlayers, comboMin, list, end, endValue, record);
    }
}
