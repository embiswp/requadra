using Quadra.Core.Assets;
using Quadra.Core.Settings;

namespace Quadra.Core.Menus;

public enum Screen
{
    Main,
    Setup,
    Options,
    HighScores,
    Lobby,
    Playing,
}

/// <summary>
/// Which screen the program is on and what happens when something on it is chosen: the single place that
/// knows the whole menu structure of the original (<c>Menu_main</c> and what it leads to). It does no
/// drawing and no file or window handling, so the front end only reports input and draws the result.
/// </summary>
public sealed class AppFlow
{
    /// <summary>Notice shown for menu items that need features not built yet.</summary>
    public static readonly string[] NotAvailableYet = ["Not available yet", "Pas encore disponible"];

    private readonly MenuLayouts.ImageSize _imageSize;
    private readonly Func<int, LegacyText> _textFor;
    private readonly Action _saveSettings;
    private readonly Action _saveScores;

    // What the player asked for on the lobby screen; the front end carries it out and takes the request.
    private NetworkRequest? _networkRequest;
    private bool _gameListRequested;
    private bool _lanSearchRequested;

    public AppFlow(GameSettings settings, HighScoreTable scores, MenuLayouts.ImageSize imageSize,
        Func<int, LegacyText> textForLanguage, Action saveSettings, Action saveScores)
    {
        Settings = settings;
        Scores = scores;
        _imageSize = imageSize;
        _textFor = textForLanguage;
        _saveSettings = saveSettings;
        _saveScores = saveScores;
        Text = textForLanguage(settings.Language);
        Setup = new SetupModel(settings, Text);
        Lobby = new LobbyModel(settings);
        Menu = MenuLayouts.Main(settings.Language, imageSize);
    }

    public GameSettings Settings { get; }
    public HighScoreTable Scores { get; }
    public LegacyText Text { get; private set; }
    public Screen Current { get; private set; } = Screen.Main;
    public MenuScreen Menu { get; private set; }
    public SetupModel Setup { get; private set; }
    public LobbyModel Lobby { get; }

    /// <summary>A network game the player chose (host or join); the front end starts it. Cleared by reading.</summary>
    public NetworkRequest? TakeNetworkRequest()
    {
        var request = _networkRequest;
        _networkRequest = null;
        return request;
    }

    /// <summary>The player asked for games on the local network to be looked for. Cleared by reading.</summary>
    public bool TakeLanRequest() => Take(ref _lanSearchRequested);

    /// <summary>The player asked for the game list to be fetched again. Cleared by reading.</summary>
    public bool TakeRefreshRequest() => Take(ref _gameListRequested);

    private static bool Take(ref bool request)
    {
        bool asked = request;
        request = false;
        return asked;
    }

    /// <summary>The list server answered: shows the games, keeping the focus where it was if it can.</summary>
    public void SetLobbyGames(IReadOnlyList<Qserv.ListedGame> games)
    {
        Lobby.SetGames(games);
        RebuildLobbyMenu();
    }

    private void RebuildLobbyMenu()
    {
        string? focused = Menu.FocusedItem?.Id;
        Menu = MenuLayouts.Lobby(Lobby.Games.Count);
        if (focused is not null)
            Menu.FocusId(focused);
    }

    /// <summary>A network game has ended or could not start; back to the lobby.</summary>
    public void ReturnToLobby(string? status = null)
    {
        Show(Screen.Lobby);
        if (status is not null)
            Lobby.Status = status;
    }

    /// <summary>A short message about something that is not available, shown until the next action.</summary>
    public string? Notice { get; private set; }

    /// <summary>Set when the player chose Quit.</summary>
    public bool QuitRequested { get; private set; }

    /// <summary>Place of the game that has just ended on the high score screen, or -1.</summary>
    public int HighlightedRank { get; private set; } = -1;

    public int Language => Settings.Language;

    /// <summary>Something was chosen on the current screen.</summary>
    public void Activate(string id)
    {
        Notice = null;
        switch (Current)
        {
            case Screen.Main: ActivateMain(id); break;
            case Screen.Setup: ActivateSetup(id); break;
            case Screen.Options: ActivateOptions(id); break;
            case Screen.HighScores: ActivateHighScores(id); break;
            case Screen.Lobby: ActivateLobby(id); break;
        }
    }

    /// <summary>Left or right arrow on a value: steps it back or forward without leaving the keyboard.</summary>
    public void Cycle(string id, int direction)
    {
        Notice = null;
        if (Current == Screen.Setup)
            Setup.Cycle(id, direction);
        else if (Current == Screen.Options && id == "language")
            ActivateOptions(id);
        else if (Current == Screen.Lobby && id is "team" or "handicap" or "survivor" or "end" or "endvalue" or "combo")
            Lobby.Cycle(id, direction);
    }

    /// <summary>Escape: backs out of typing or capturing, then out of the screen; on the main menu it quits.</summary>
    public void Back()
    {
        Notice = null;
        switch (Current)
        {
            case Screen.Main:
                QuitRequested = true;
                break;
            case Screen.Setup:
                if (!Setup.Cancel())
                    LeaveSetup();
                break;
            case Screen.Lobby:
                if (Lobby.Edit != LobbyEdit.None)
                    FinishLobbyEdit();
                else
                    Show(Screen.Main);
                break;
            case Screen.Options:
            case Screen.HighScores:
            case Screen.Playing:
                Show(Screen.Main);
                break;
        }
    }

    /// <summary>A single-player game has ended: record the score and show the table.</summary>
    public void GameFinished(int score, int lines, int level)
    {
        HighlightedRank = Scores.Add(new HighScore(Settings.CurrentPlayer.Name, score, lines, level, DateTime.UtcNow));
        if (HighlightedRank >= 0)
            _saveScores();
        Show(Screen.HighScores, playAgain: true);
    }

    /// <summary>Shows a screen directly (used at start-up and by tests).</summary>
    public void Show(Screen screen, bool playAgain = false)
    {
        Current = screen;
        Notice = null;
        if (screen != Screen.HighScores)
            HighlightedRank = -1;
        if (screen == Screen.Lobby)
        {
            Lobby.Commit();
            RequestGameList("No game list server is set; join by address");
        }
        Menu = screen switch
        {
            Screen.Setup => MenuLayouts.Setup(Language, _imageSize),
            Screen.Options => MenuLayouts.Options(),
            Screen.HighScores => MenuLayouts.HighScores(Language, playAgain, _imageSize),
            Screen.Lobby => MenuLayouts.Lobby(Lobby.Games.Count),
            _ => MenuLayouts.Main(Language, _imageSize),
        };
    }

    private void ActivateMain(string id)
    {
        switch (id)
        {
            case "single": Show(Screen.Playing); break;
            case "scores": Show(Screen.HighScores); break;
            case "setup": Show(Screen.Setup); break;
            case "options": Show(Screen.Options); break;
            case "quit": QuitRequested = true; break;
            case "multi": Show(Screen.Lobby); break;
            case "demo":
            case "help":
                Notice = NotAvailableYet[Language];
                break;
        }
    }

    private void ActivateSetup(string id)
    {
        if (id == "back")
            LeaveSetup();
        else
            Setup.Activate(id);
    }

    private void LeaveSetup()
    {
        Setup.Cancel();
        Settings.Normalize();       // an empty name becomes "#1" again, as in the original
        _saveSettings();
        Show(Screen.Main);
    }

    private void ActivateOptions(string id)
    {
        if (id == "language")
        {
            Settings.Language = (Settings.Language + 1) % GameSettings.LanguageCount;
            Text = _textFor(Settings.Language);
            Setup.SetText(Text);
            _saveSettings();
        }
        else if (id == "back")
        {
            Show(Screen.Main);
        }
    }

    private void ActivateHighScores(string id)
    {
        if (id == "again")
            Show(Screen.Playing);
        else if (id == "back")
            Show(Screen.Main);
    }

    private void ActivateLobby(string id)
    {
        if (Lobby.Edit != LobbyEdit.None)
        {
            FinishLobbyEdit();      // a click anywhere else ends the typing, then does its own thing
        }

        if (id.StartsWith("game.", StringComparison.Ordinal) && int.TryParse(id.AsSpan(5), out int index) && index < Lobby.Games.Count)
        {
            _networkRequest = LobbyModel.JoinTarget(Lobby.Games[index]);
            return;
        }

        switch (id)
        {
            case "host": _networkRequest = new NetworkRequest(true, "127.0.0.1", LobbyModel.DefaultPort); break;
            case "refresh":
                _gameListRequested = true;
                Lobby.Status = GameListStatus("No game list server is set");
                break;
            case "lan":
                _lanSearchRequested = true;
                Lobby.Status = "Looking for games on the local network...";
                break;
            case "address" or "server": Lobby.BeginEdit(id); break;
            case "team" or "handicap" or "survivor" or "end" or "endvalue" or "combo":
                Lobby.Cycle(id, 1);
                _saveSettings();
                break;
            case "join":
                _networkRequest = LobbyModel.ParseAddress(Lobby.Address);
                if (_networkRequest is null)
                    Lobby.Status = "Type an address like host or host:port";
                break;
            case "back": Show(Screen.Main); break;
        }
    }

    private void FinishLobbyEdit()
    {
        if (Lobby.Commit() == LobbyEdit.Server)
            RequestGameList("No game list server is set");
        _saveSettings();
    }

    // Asks for the game list if a list server is set, and says what is happening either way.
    private void RequestGameList(string noServerStatus)
    {
        _gameListRequested = Lobby.ServerUrl.Length > 0;
        Lobby.Status = GameListStatus(noServerStatus);
    }

    private string GameListStatus(string noServerStatus) =>
        Lobby.ServerUrl.Length > 0 ? "Fetching the game list..." : noServerStatus;
}
