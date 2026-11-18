using System.Text.Json;
using System.Text.RegularExpressions;

namespace Quadra.Core.Qserv;

public sealed class QservOptions
{
    /// <summary>A game that has not been re-posted for this long is dropped from the list.</summary>
    public TimeSpan GameTimeout { get; init; } = TimeSpan.FromSeconds(180);

    public int ScoresToKeep { get; init; } = 100;

    /// <summary>How many high scores <c>gethighscores</c> returns when the request does not say.</summary>
    public int DefaultDemos { get; init; } = 5;

    /// <summary>Longest recording (base64 text) accepted with a posted score.</summary>
    public int MaxRecLength { get; init; } = 1_000_000;

    /// <summary>Where the high scores are kept between runs; null keeps them in memory only.</summary>
    public string? ScoresFile { get; init; }
}

/// <summary>A request the server refuses (it answered with an error page in the original).</summary>
public sealed class QservException(string message) : Exception(message);

/// <summary>
/// The game list server: a port of legacy/server/qserv.pl. It works on the request text and returns the
/// reply text, so the HTTP host stays thin. Hosted games are kept in memory and expire; high scores are
/// kept by score value, best first.
/// </summary>
public sealed partial class QservServer
{
    public const int DefaultPort = 3456;
    public const string Greeting = "Hi, I'm the NEW Quadra game server.\nYou should use Quadra to talk to me :).\n";

    private sealed record Hosted(QservTree Info, DateTimeOffset LastUpdate);

    private readonly QservOptions _options;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private readonly Dictionary<string, Hosted> _games = [];
    private readonly SortedDictionary<long, QservTree> _scores = new(Comparer<long>.Create((a, b) => b.CompareTo(a)));

    public QservServer(QservOptions? options = null, TimeProvider? clock = null)
    {
        _options = options ?? new QservOptions();
        _clock = clock ?? TimeProvider.System;
        LoadScores();
    }

    public int GameCount
    {
        get
        {
            lock (_lock)
            {
                Expire();
                return _games.Count;
            }
        }
    }

    /// <summary>Handles the <c>data</c> form field of a request from <paramref name="remoteAddress"/>.</summary>
    public string Handle(string data, string remoteAddress)
    {
        var lines = data.Split('\n');
        var command = lines[0].TrimEnd('\r');
        var request = QservTree.Parse(lines.Skip(1));
        request.Set("info/remoteaddr", remoteAddress);
        request.Set("port", NormalizePort(request.Get("port")));

        lock (_lock)
        {
            Expire();
            return command switch
            {
                "postgame" => PostGame(request),
                "deletegame" => DeleteGame(request),
                "postdemo" => PostDemo(request),
                "gethighscores" => GetHighScores(request),
                "getgames" => GetGames(request),
                _ => Greeting,
            };
        }
    }

    // The port is digits only, and falls back to the default when missing, zero or out of range.
    private static string NormalizePort(string? port)
    {
        var digits = new string((port ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0 || digits.Length > 5 || !int.TryParse(digits, out var n) || n == 0 || n > 65535)
            return DefaultPort.ToString();
        return n.ToString();
    }

    private static string GameKey(QservTree request)
    {
        var key = request.Get("info/remoteaddr") + ":" + request.Get("port");
        if (!SafeName().IsMatch(key))
            throw new QservException($"bad data in {key}");
        return key;
    }

    [GeneratedRegex(@"^[-@\w.:]+$")]
    private static partial Regex SafeName();

    private void Expire()
    {
        var now = _clock.GetUtcNow();
        foreach (var key in _games.Where(g => now - g.Value.LastUpdate >= _options.GameTimeout).Select(g => g.Key).ToList())
            _games.Remove(key);
    }

    private string PostGame(QservTree request)
    {
        var key = GameKey(request);
        var now = _clock.GetUtcNow();
        request.Set("info/lastupdate", now.ToUnixTimeSeconds().ToString());
        bool known = _games.ContainsKey(key);
        _games[key] = new Hosted(request, now);
        return known ? "Game updated\n" : "Game added\n";
    }

    private string DeleteGame(QservTree request) =>
        _games.Remove(GameKey(request)) ? "Game deleted\n" : "Game not found\n";

    private string GetGames(QservTree request)
    {
        var version = request.Get("info/quadra_version");
        bool snoop = request.Get("info/qsnoop_version") is not null;
        var sent = new HashSet<string>();
        var sb = new System.Text.StringBuilder("Current games\n");

        foreach (var (game, hosted) in _games.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var key = game;
            var info = hosted.Info;

            if (version is not null)
            {
                // 1.1.2 clients cannot parse the player list.
                if (version == "1.1.2")
                {
                    info = info.Clone();
                    info.Remove("players");
                }
            }
            else if (!snoop)
            {
                // Old clients list games by host only, one per host.
                key = game[..game.IndexOf(':')];
            }

            if (sent.Add(key))
                sb.Append(info.Format(key));
        }
        return sb.ToString();
    }

    private string PostDemo(QservTree request)
    {
        var score = request.Get("score");
        if (score is not null)
        {
            if (score.Length == 0 || score.Length > 18 || !score.All(char.IsAsciiDigit))
                throw new QservException($"bad data in {score}");
            if ((request.Get("rec")?.Length ?? 0) > _options.MaxRecLength)
                throw new QservException("recording too large");

            _scores[long.Parse(score)] = request;
            while (_scores.Count > _options.ScoresToKeep)
                _scores.Remove(_scores.Keys.Last());
            SaveScores();
        }
        return GetHighScores(request);
    }

    private string GetHighScores(QservTree request)
    {
        int num = _options.DefaultDemos;
        if (int.TryParse(request.Get("num"), out var asked) && asked > 0)
            num = Math.Min(asked, _options.ScoresToKeep);

        var sb = new System.Text.StringBuilder("Ok\n");
        int rank = 0;
        foreach (var scoreInfo in _scores.Values.Take(num))
            sb.Append(scoreInfo.Format($"high{rank++:D3}"));
        return sb.ToString();
    }

    private void LoadScores()
    {
        if (_options.ScoresFile is not { } path || !File.Exists(path))
            return;
        try
        {
            var stored = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [];
            foreach (var text in stored)
            {
                var tree = QservTree.Parse(text.Split('\n'));
                if (long.TryParse(tree.Get("score"), out var score))
                    _scores[score] = tree;
            }
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // An unreadable file starts a fresh table; the next post overwrites it.
        }
    }

    private void SaveScores()
    {
        if (_options.ScoresFile is not { } path)
            return;
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_scores.Values.Select(s => s.Format()).ToList()));
        File.Move(tmp, path, overwrite: true);
    }
}
