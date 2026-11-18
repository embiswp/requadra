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
public sealed class LobbyModel(GameSettings settings)
{
    public const int DefaultPort = QservServer.DefaultPort;
    private const int MaxFieldLength = 120;

    public IReadOnlyList<ListedGame> Games { get; private set; } = [];

    /// <summary>One line about what is going on (fetching, an error, how many games), or empty.</summary>
    public string Status { get; set; } = "";

    public LobbyEdit Edit { get; private set; }

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
