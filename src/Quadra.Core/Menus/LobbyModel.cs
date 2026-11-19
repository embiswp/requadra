using System.Globalization;
using Quadra.Core.Qserv;
using Quadra.Core.Settings;

namespace Quadra.Core.Menus;

public enum LobbyEdit
{
    None,
    Address,
    Server,
}

/// <summary>Where a game is: <see cref="Host"/> true means start one here and join it.</summary>
public sealed record NetworkRequest(bool Host, string Address, int Port);

/// <summary>
/// The logic of the multi-player screen: the games the list server reported, the address to join by hand
/// and the list server's own address, both typed in place. Fetching the list is the front end's job; it
/// hands the result to <see cref="SetGames"/>.
/// </summary>
/// <summary>Numbers the lobby shares with the protocol.</summary>
internal static class PacketLimits
{
    public const int Teams = Net.PacketDecoder.MaxTeams;
}

public sealed class LobbyModel(GameSettings settings)
{
    public const int DefaultPort = QservServer.DefaultPort;
    private const int MaxFieldLength = 120;

    public IReadOnlyList<ListedGame> Games { get; private set; } = [];

    /// <summary>One line about what is going on (fetching, an error, how many games), or empty.</summary>
    public string Status { get; set; } = "";

    public LobbyEdit Edit { get; private set; }

    /// <summary>The rules "Host a game" starts with.</summary>
    public HostSettings Rules => settings.Rules;

    public string Address => settings.LastAddress;
    public string ServerUrl => settings.ListServer;

    public void SetGames(IReadOnlyList<ListedGame> games)
    {
        Games = games;
        Status = games.Count == 0 ? "No games listed" : $"{games.Count} game(s) listed";
    }

    /// <summary>The text of a field as shown, with a caret while it is being typed in.</summary>
    public string FieldText(string id) =>
        id switch
        {
            "address" => Address + (Edit == LobbyEdit.Address ? "_" : ""),
            "server" => ServerUrl + (Edit == LobbyEdit.Server ? "_" : ""),
            _ => "",
        };

    /// <summary>What to show for the team and handicap values.</summary>
    public string ValueText(string id) => id switch
    {
        "team" => settings.CurrentPlayer.Team < 0 ? "auto" : (settings.CurrentPlayer.Team + 1).ToString(CultureInfo.InvariantCulture),
        "handicap" => settings.CurrentPlayer.Handicap.ToString(CultureInfo.InvariantCulture),
        "survivor" => settings.Rules.Survivor ? "on" : "off",
        "end" => settings.Rules.End switch
        {
            Net.GameEnd.Frag => "frags",
            Net.GameEnd.Time => "minutes",
            Net.GameEnd.Points => "k points",
            Net.GameEnd.Lines => "lines",
            _ => "never",
        },
        "endvalue" => settings.Rules.End == Net.GameEnd.Never ? "-" : settings.Rules.EndValue.ToString(CultureInfo.InvariantCulture),
        "combo" => settings.Rules.ComboMin.ToString(CultureInfo.InvariantCulture),
        _ => FieldText(id),
    };

    /// <summary>Steps the team (auto, 1 to 8) or the handicap (0 to 4) forwards or backwards, wrapping round.</summary>
    public void Cycle(string id, int direction)
    {
        var player = settings.CurrentPlayer;
        if (id == "team")
            player.Team = Wrap(player.Team + 1 + direction, PacketLimits.Teams + 1) - 1;
        else if (id == "handicap")
            player.Handicap = Wrap(player.Handicap + direction, PlayerSettings.MaxHandicap + 1);
        else if (id == "survivor")
            settings.Rules.Survivor = !settings.Rules.Survivor;
        else if (id == "end")
            settings.Rules.End = (Net.GameEnd)Wrap((int)settings.Rules.End + direction, Enum.GetValues<Net.GameEnd>().Length);
        else if (id == "endvalue" && settings.Rules.End != Net.GameEnd.Never)
        {
            int step = settings.Rules.End is Net.GameEnd.Frag or Net.GameEnd.Time ? 1 : 10;
            settings.Rules.EndValue = Math.Clamp(settings.Rules.EndValue + direction * step, 0, HostSettings.MaxEndValue);
        }
        else if (id == "combo")
            settings.Rules.ComboMin = Wrap(settings.Rules.ComboMin + direction, HostSettings.MaxComboMin + 1);
    }

    private static int Wrap(int value, int count) => (value % count + count) % count;

    public void BeginEdit(string id) => Edit = id switch
    {
        "address" => LobbyEdit.Address,
        "server" => LobbyEdit.Server,
        _ => Edit,
    };

    public void TypeCharacter(char c)
    {
        if (Edit == LobbyEdit.None || c < ' ' || c > '~')
            return;
        if (Edit == LobbyEdit.Address && settings.LastAddress.Length < MaxFieldLength)
            settings.LastAddress += c;
        else if (Edit == LobbyEdit.Server && settings.ListServer.Length < MaxFieldLength)
            settings.ListServer += c;
    }

    public void Backspace()
    {
        if (Edit == LobbyEdit.Address && settings.LastAddress.Length > 0)
            settings.LastAddress = settings.LastAddress[..^1];
        else if (Edit == LobbyEdit.Server && settings.ListServer.Length > 0)
            settings.ListServer = settings.ListServer[..^1];
    }

    /// <summary>Ends typing; returns which field was being edited.</summary>
    public LobbyEdit Commit()
    {
        var was = Edit;
        settings.Normalize();
        Edit = LobbyEdit.None;
        return was;
    }

    /// <summary>One line for the list: name, players, state.</summary>
    public static string Describe(ListedGame game)
    {
        string name = game.Info.Name.Length > 0 ? game.Info.Name : game.Host;
        string state = game.Info.Terminated ? "finished" : game.Info.Started ? "playing" : "waiting";
        return $"{name}|{game.Info.Players.Count}|{state}";
    }

    /// <summary>Where to connect to join a listed game.</summary>
    public static NetworkRequest JoinTarget(ListedGame game) => new(false, game.Host, game.Info.Port);

    /// <summary><c>host</c> or <c>host:port</c>; the port defaults to <see cref="DefaultPort"/>.</summary>
    public static NetworkRequest? ParseAddress(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;
        int colon = text.LastIndexOf(':');
        if (colon < 0)
            return new NetworkRequest(false, text, DefaultPort);
        if (colon == 0 || !int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
            return null;
        return new NetworkRequest(false, text[..colon], port);
    }
}
