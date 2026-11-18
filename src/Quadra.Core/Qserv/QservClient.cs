using System.Globalization;
using System.Text;

namespace Quadra.Core.Qserv;

/// <summary>A game from the list, with the host that announced it.</summary>
public sealed record ListedGame(string Host, GameAnnouncement Info);

/// <summary>A score from the shared high score table; <see cref="Recording"/> is the demo, base64 encoded.</summary>
public sealed record RemoteHighScore(string Name, int Score, int Lines, int Level, string? Recording);

/// <summary>A reply: the first line is a status, the rest a key/value tree.</summary>
public sealed record QservReply(string Status, QservTree Body)
{
    public static QservReply Parse(string text)
    {
        var lines = text.Split('\n');
        return new QservReply(lines[0].TrimEnd('\r'), QservTree.Parse(lines.Skip(1)));
    }
}

/// <summary>
/// Talks to a game list server over HTTP like <c>Qserv</c> in legacy/source/qserv.cc: a form POST whose
/// <c>data</c> field holds a command line and key/value lines.
/// </summary>
public sealed class QservClient(HttpClient http, Uri endpoint, string version = "1.3.0", int language = 0)
{
    public async Task<QservReply> SendAsync(string command, QservTree? data = null, CancellationToken ct = default)
    {
        var info = new QservTree();
        info.Set("info/language", language.ToString(CultureInfo.InvariantCulture));
        info.Set("info/quadra_version", version);
        info.Set("info/platform/os", OperatingSystemName());

        var text = new StringBuilder(command).Append('\n');
        if (data is not null)
            text.Append(data.Format());
        text.Append(info.Format());

        using var content = new FormUrlEncodedContent([KeyValuePair.Create("data", text.ToString())]);
        using var response = await http.PostAsync(endpoint, content, ct);
        response.EnsureSuccessStatusCode();
        return QservReply.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private static string OperatingSystemName() =>
        OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Other";

    /// <summary>Announces or refreshes a hosted game. Returns false if the server did not accept it.</summary>
    public async Task<bool> PostGameAsync(GameAnnouncement game, CancellationToken ct = default)
    {
        var reply = await SendAsync("postgame", game.ToTree(), ct);
        return reply.Status is "Game added" or "Game updated";
    }

    public async Task<bool> DeleteGameAsync(int port, CancellationToken ct = default)
    {
        var data = new QservTree();
        data.Set("port", port.ToString(CultureInfo.InvariantCulture));
        return (await SendAsync("deletegame", data, ct)).Status == "Game deleted";
    }

    public async Task<IReadOnlyList<ListedGame>> GetGamesAsync(CancellationToken ct = default)
    {
        var reply = await SendAsync("getgames", null, ct);
        if (reply.Status != "Current games")
            return [];

        var games = new List<ListedGame>();
        foreach (var (key, node) in reply.Body.Nodes())
        {
            int colon = key.IndexOf(':');
            games.Add(new ListedGame(colon < 0 ? key : key[..colon], GameAnnouncement.FromTree(node)));
        }
        return games;
    }

    public async Task<IReadOnlyList<RemoteHighScore>> GetHighScoresAsync(int count, CancellationToken ct = default) =>
        ReadScores(await SendAsync("gethighscores", NumberOf(count), ct));

    /// <summary>Posts a score with its recording and returns the table as it now stands.</summary>
    public async Task<IReadOnlyList<RemoteHighScore>> PostDemoAsync(
        string name, int score, int lines, int level, byte[] recording, int count, CancellationToken ct = default)
    {
        var data = NumberOf(count);
        data.Set("name", name);
        data.Set("score", score.ToString(CultureInfo.InvariantCulture));
        data.Set("lines", lines.ToString(CultureInfo.InvariantCulture));
        data.Set("level", level.ToString(CultureInfo.InvariantCulture));
        data.Set("rec", Convert.ToBase64String(recording));
        return ReadScores(await SendAsync("postdemo", data, ct));
    }

    private static QservTree NumberOf(int count)
    {
        var data = new QservTree();
        data.Set("num", count.ToString(CultureInfo.InvariantCulture));
        return data;
    }

    private static IReadOnlyList<RemoteHighScore> ReadScores(QservReply reply)
    {
        if (reply.Status != "Ok")
            return [];

        int Int(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        return [.. reply.Body.Nodes().Select(n => new RemoteHighScore(
            n.Value.Get("name") ?? "", Int(n.Value.Get("score")), Int(n.Value.Get("lines")), Int(n.Value.Get("level")),
            n.Value.Get("rec")))];
    }
}
