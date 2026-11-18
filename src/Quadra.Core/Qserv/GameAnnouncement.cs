using System.Globalization;
using Quadra.Core.Net;

namespace Quadra.Core.Qserv;

/// <summary>A player as listed in a game announcement.</summary>
public sealed record AnnouncedPlayer(string Name, int Team, int Status, int Handicap)
{
    /// <summary>Only sent once the game has started.</summary>
    public int Frags { get; init; }
    public int Deaths { get; init; }
    public int Lines { get; init; }
    public int Score { get; init; }
    public int PlayingTime { get; init; }
}

/// <summary>
/// What a hosting game tells the game list server and what the list shows: the <c>postgame</c> body of
/// <c>Game::addgameinfo</c> (legacy/source/game.cc) and its reading in <c>Menu_multi_internet::parsegames</c>.
/// </summary>
public sealed class GameAnnouncement
{
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public List<string> Addresses { get; set; } = [];
    public int Port { get; set; } = QservServer.DefaultPort;

    public bool Started { get; set; }
    public bool Terminated { get; set; }
    public bool AutoRestart { get; set; }
    public int RunningTime { get; set; }
    public int PlayingTime { get; set; }
    public int RoundNumber { get; set; }

    /// <summary>0 free for all, 1 survivor, 2 peace, 3 blind, 4 hot potato, 5 single player.</summary>
    public int RulesType { get; set; }
    public bool Survivor { get; set; }
    public bool HotPotato { get; set; }
    public Attack NormalAttack { get; set; } = new(AttackType.Lines, 0);
    public Attack CleanAttack { get; set; } = new(AttackType.Lines, 0);
    public Attack PotatoNormalAttack { get; set; } = new(AttackType.Lines, 0);
    public Attack PotatoCleanAttack { get; set; } = new(AttackType.Lines, 0);
    public int LevelStart { get; set; } = 1;
    public bool LevelUp { get; set; } = true;
    public int MinCombo { get; set; }
    public bool AllowHandicap { get; set; } = true;
    public GameEnd EndType { get; set; }
    public int EndValue { get; set; }

    public List<AnnouncedPlayer> Players { get; set; } = [];

    private static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Flag(bool v) => v ? "1" : "0";
    private static string Pair(Attack a) => $"{(int)a.Type} {a.Param}";

    /// <summary>The key/value lines of a <c>postgame</c> request (without the command line).</summary>
    public QservTree ToTree()
    {
        var t = new QservTree();
        t.Set("name", Name);
        t.Set("version", Int(Version));
        t.Set("address", string.Join(' ', Addresses));
        t.Set("port", Int(Port));
        t.Set("status/started", Flag(Started));
        t.Set("status/terminated", Flag(Terminated));
        t.Set("status/autorestart", Flag(AutoRestart));
        t.Set("status/running_time", Int(RunningTime));
        t.Set("stats/playing_time", Int(PlayingTime));
        t.Set("stats/round_number", Int(RoundNumber));
        t.Set("rules/type", Int(RulesType));
        t.Set("rules/survivor", Flag(Survivor));
        t.Set("rules/hot_potato", Flag(HotPotato));
        t.Set("rules/attacks/normal", Pair(NormalAttack));
        t.Set("rules/attacks/clean", Pair(CleanAttack));
        if (HotPotato)
        {
            t.Set("rules/attacks/potato_normal", Pair(PotatoNormalAttack));
            t.Set("rules/attacks/potato_clean", Pair(PotatoCleanAttack));
        }
        t.Set("rules/levelstart", Int(LevelStart));
        t.Set("rules/levelup", Flag(LevelUp));
        t.Set("rules/mincombo", Int(MinCombo));
        t.Set("rules/allowhandicap", Flag(AllowHandicap));
        t.Set("end/type", Int((int)EndType));
        t.Set("end/value", Int(EndValue));
        for (int i = 0; i < Players.Count; i++)
        {
            var p = Players[i];
            t.Set($"players/{i}/name", p.Name);
            t.Set($"players/{i}/team", Int(p.Team));
            t.Set($"players/{i}/status", Int(p.Status));
            t.Set($"players/{i}/handicap", Int(p.Handicap));
            t.Set($"players/{i}/playing_time", Int(p.PlayingTime));
            if (Started)
            {
                t.Set($"players/{i}/frags", Int(p.Frags));
                t.Set($"players/{i}/deaths", Int(p.Deaths));
                t.Set($"players/{i}/lines", Int(p.Lines));
                t.Set($"players/{i}/score", Int(p.Score));
            }
        }
        return t;
    }

    /// <summary>
    /// Reads one game of a <c>getgames</c> reply. Missing or out-of-range values fall back to the
    /// defaults, as in the original list.
    /// </summary>
    public static GameAnnouncement FromTree(QservTree t)
    {
        var g = new GameAnnouncement
        {
            Name = t.Get("name") ?? "",
            Version = ToInt(t.Get("version")),
            Port = ToInt(t.Get("port"), QservServer.DefaultPort),
            Started = ToInt(t.Get("status/started")) != 0,
            Terminated = ToInt(t.Get("status/terminated")) != 0,
            AutoRestart = ToInt(t.Get("status/autorestart")) != 0,
            RunningTime = ToInt(t.Get("status/running_time")),
            PlayingTime = ToInt(t.Get("stats/playing_time")),
            RoundNumber = ToInt(t.Get("stats/round_number")),
            RulesType = ToInt(t.Get("rules/type")),
            Survivor = ToInt(t.Get("rules/survivor")) != 0,
            HotPotato = ToInt(t.Get("rules/hot_potato")) != 0,
            NormalAttack = ToAttack(t.Get("rules/attacks/normal")),
            CleanAttack = ToAttack(t.Get("rules/attacks/clean")),
            PotatoNormalAttack = ToAttack(t.Get("rules/attacks/potato_normal")),
            PotatoCleanAttack = ToAttack(t.Get("rules/attacks/potato_clean")),
            LevelStart = ToInt(t.Get("rules/levelstart"), 1),
            LevelUp = t.Get("rules/levelup") is not { } up || ToInt(up) != 0,
            MinCombo = ToInt(t.Get("rules/mincombo")),
            AllowHandicap = t.Get("rules/allowhandicap") is not { } ah || ToInt(ah) != 0,
            EndType = (GameEnd)Math.Clamp(ToInt(t.Get("end/type")), 0, (int)GameEnd.Lines),
            EndValue = ToInt(t.Get("end/value")),
        };

        if (t.Get("address") is { Length: > 0 } addresses)
            g.Addresses = [.. addresses.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

        if (t.Sub("players") is { } players)
        {
            foreach (var (_, p) in players.Nodes())
            {
                int team = ToInt(p.Get("team"));
                int status = ToInt(p.Get("status"), -1);
                int handicap = ToInt(p.Get("handicap"));
                g.Players.Add(new AnnouncedPlayer(
                    p.Get("name") ?? "",
                    team is >= 0 and < PacketDecoder.MaxTeams ? team : 0,
                    status is >= 0 and <= 3 ? status : -1,
                    handicap is >= 0 and <= 4 ? handicap : 0)
                {
                    Frags = ToInt(p.Get("frags")),
                    Deaths = ToInt(p.Get("deaths")),
                    Lines = ToInt(p.Get("lines")),
                    Score = ToInt(p.Get("score")),
                    PlayingTime = ToInt(p.Get("playing_time")),
                });
            }
        }
        return g;
    }

    private static int ToInt(string? s, int fallback = 0)
    {
        if (s is null)
            return fallback;
        s = s.TrimStart();
        int end = 0;
        while (end < s.Length && (char.IsAsciiDigit(s[end]) || (end == 0 && s[end] == '-')))
            end++;
        return int.TryParse(s[..end], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : fallback;
    }

    // "type param", both numbers; a missing part is zero.
    private static Attack ToAttack(string? s)
    {
        if (s is null)
            return new Attack(AttackType.Lines, 0);
        int space = s.IndexOf(' ');
        int type = ToInt(space < 0 ? s : s[..space]);
        int param = space < 0 ? 0 : ToInt(s[(space + 1)..]);
        return new Attack(type is >= 0 and <= (int)AttackType.FullBlind ? (AttackType)type : AttackType.Lines, param);
    }
}
