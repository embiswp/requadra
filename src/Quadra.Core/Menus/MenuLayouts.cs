namespace Quadra.Core.Menus;

/// <summary>
/// Where the original puts everything on its screens (<c>Menu_main</c>, <c>Menu_setup</c>,
/// <c>Menu_option</c> and <c>Menu_highscore</c> in <c>legacy/source/menu.cc</c>), for English (0) and
/// French (1). Sizes of pictures come from the picture files themselves.
/// </summary>
public static class MenuLayouts
{
    public delegate (int Width, int Height) ImageSize(string file);

    public const int BackButtonX = 560, BackButtonY = 450, BackButtonWidth = 80, BackButtonHeight = 22;

    // ---- main menu -------------------------------------------------------------------------------

    public static string MainBackground => "debuto.png";

    /// <summary>The French main menu draws its own item text over the background at this position.</summary>
    public static (string File, int X, int Y)? MainOverlay(int language) => language == 1 ? ("debutof.png", 155, 93) : null;

    private static readonly (string Id, int X, int Y)[] MainEnglish =
    [
        ("single", 160, 99), ("multi", 166, 139), ("demo", 214, 183), ("scores", 235, 225),
        ("setup", 221, 267), ("options", 264, 310), ("help", 261, 351), ("quit", 295, 392),
    ];

    private static readonly (string Id, int X, int Y)[] MainFrench =
    [
        ("single", 223, 94), ("multi", 145, 136), ("demo", 174, 175), ("scores", 240, 219),
        ("setup", 160, 261), ("options", 257, 303), ("help", 223, 344), ("quit", 261, 386),
    ];

    public static MenuScreen Main(int language, ImageSize size)
    {
        var table = language == 1 ? MainFrench : MainEnglish;
        var items = new List<MenuItem>();
        for (int i = 0; i < table.Length; i++)
        {
            string file = $"debut{i}{(language == 1 ? "f" : "")}.png";
            var (w, h) = size(file);
            items.Add(new MenuItem(table[i].Id, table[i].X, table[i].Y, w, h, Hover: file));
        }
        return new MenuScreen(items);
    }

    // ---- player setup ------------------------------------------------------------------------------

    public static string SetupBackground(int language) => language == 1 ? "setupf.png" : "setup.png";

    // Label picture positions: index 0..15 of setup<N>.png / setupf<N>.png.
    private static readonly (int X, int Y)[] SetupEnglish =
    [
        (195, 11), (255, 101), (222, 132), (237, 161), (240, 192), (157, 220), (184, 252), (153, 281),
        (45, 347), (32, 378), (26, 408), (19, 439), (344, 350), (275, 380), (382, 411), (390, 439),
    ];

    private static readonly (int X, int Y)[] SetupFrench =
    [
        (137, 11), (266, 101), (193, 132), (244, 162), (265, 192), (140, 221), (166, 252), (127, 281),
        (60, 349), (71, 380), (95, 409), (64, 438), (354, 350), (316, 381), (358, 410), (359, 440),
    ];

    // Picture number -> what it is.
    private static readonly string[] SetupIds =
    [
        "player", "name", "password", "shadow", "smooth", "hspeed", "vspeed", "continuous",
        "key.left", "key.right", "key.down", "key.drop", "key.rotatecw", "key.rotateccw", "key.twice", "allkeys",
    ];

    /// <summary>Where the current value of a setting is shown (the same in both languages).</summary>
    public static readonly IReadOnlyDictionary<string, (int X, int Y, int Width)> SetupValues = new Dictionary<string, (int, int, int)>
    {
        ["player"] = (333, 11, 40),
        ["name"] = (333, 106, 110),
        ["password"] = (333, 132, 200),
        ["shadow"] = (333, 161, 80),
        ["smooth"] = (333, 192, 80),
        ["hspeed"] = (333, 220, 120),
        ["vspeed"] = (333, 252, 120),
        ["continuous"] = (333, 281, 80),
        ["key.left"] = (148, 347, 120),
        ["key.right"] = (148, 378, 120),
        ["key.down"] = (148, 408, 120),
        ["key.drop"] = (148, 439, 120),
        ["key.rotatecw"] = (513, 350, 125),
        ["key.rotateccw"] = (513, 380, 125),
        ["key.twice"] = (513, 411, 125),
    };

    public const int SetupPromptX = 180, SetupPromptY = 315, SetupPromptWidth = 280;

    public static MenuScreen Setup(int language, ImageSize size)
    {
        var positions = language == 1 ? SetupFrench : SetupEnglish;
        var items = new List<MenuItem>();
        for (int i = 0; i < SetupIds.Length; i++)
        {
            string file = $"setup{(language == 1 ? "f" : "")}{i}.png";
            var (w, h) = size(file);
            items.Add(new MenuItem(SetupIds[i], positions[i].X, positions[i].Y, w, h, Hover: file));
        }
        // The value next to a label can be clicked too.
        foreach (var (id, (x, y, width)) in SetupValues)
            items.Add(new MenuItem(id, x, y, width, 22) { KeyboardFocusable = false });
        items.Add(new MenuItem("back", BackButtonX, BackButtonY, BackButtonWidth, BackButtonHeight));
        return new MenuScreen(items);
    }

    // ---- options -------------------------------------------------------------------------------------

    public static string TextScreenBackground => "multi.png";

    public const int OptionsLanguageX = 380, OptionsLanguageY = 100, OptionsLanguageWidth = 160;

    public static MenuScreen Options() => new(
    [
        new MenuItem("language", OptionsLanguageX, OptionsLanguageY, OptionsLanguageWidth, 22),
        new MenuItem("back", BackButtonX, BackButtonY, BackButtonWidth, BackButtonHeight),
    ]);

    // ---- high scores ---------------------------------------------------------------------------------

    public static string HighScoreBackground => "hscore.png";

    /// <summary>The French screen draws its own titles over the background at this position.</summary>
    public static (string File, int X, int Y)? HighScoreOverlay(int language) => language == 1 ? ("hscoretf.png", 138, 0) : null;

    public static MenuScreen HighScores(int language, bool playAgain, ImageSize size)
    {
        string f = language == 1 ? "f" : "";
        string againRest = $"hscore1{f}.png", againHover = language == 1 ? "hscor1fs.png" : "hscore1s.png";
        string backRest = $"hscore2{f}.png", backHover = language == 1 ? "hscor2fs.png" : "hscore2s.png";
        var items = new List<MenuItem>();
        if (playAgain)
        {
            var (w, h) = size(againRest);
            items.Add(new MenuItem("again", 38, 410, w, h, againRest, againHover));
        }
        var (bw, bh) = size(backRest);
        items.Add(new MenuItem("back", 485, 410, bw, bh, backRest, backHover));
        return new MenuScreen(items);
    }

    /// <summary>Where the table of local scores starts: headings at y = 256, the rows 20 below, 21 apart.</summary>
    public const int ScoreTableX = 100, ScoreHeadingY = 256, ScoreRowY = 276, ScoreRowStep = 21;

    // ---- multi-player lobby ----------------------------------------------------------------------------
    // The original's lobby is made of pictures; this one is text on the same plain screen as the options.

    public const int LobbyRows = 10;
    public const int LobbyListX = 40, LobbyListY = 84, LobbyListWidth = 560, LobbyRowStep = 22;
    public const int LobbyButtonY = 330, LobbyAddressY = 366, LobbyServerY = 402, LobbyStatusY = 434;
    public const int LobbyHostX = 40, LobbyHostWidth = 150, LobbyRefreshX = 220, LobbyRefreshWidth = 100;
    public const int LobbyFieldX = 150, LobbyAddressWidth = 250, LobbyServerWidth = 450, LobbyJoinX = 420, LobbyJoinWidth = 60;

    public const int LobbyTeamX = 400, LobbyTeamWidth = 60, LobbyHandicapX = 560, LobbyHandicapWidth = 50;

    public static MenuScreen Lobby(int gameCount)
    {
        var items = new List<MenuItem>();
        for (int i = 0; i < Math.Min(gameCount, LobbyRows); i++)
            items.Add(new MenuItem($"game.{i}", LobbyListX, LobbyListY + LobbyRowStep * i, LobbyListWidth, 20));
        items.Add(new MenuItem("host", LobbyHostX, LobbyButtonY, LobbyHostWidth, 22));
        items.Add(new MenuItem("refresh", LobbyRefreshX, LobbyButtonY, LobbyRefreshWidth, 22));
        items.Add(new MenuItem("team", LobbyTeamX, LobbyButtonY, LobbyTeamWidth, 22));
        items.Add(new MenuItem("handicap", LobbyHandicapX, LobbyButtonY, LobbyHandicapWidth, 22));
        items.Add(new MenuItem("address", LobbyFieldX, LobbyAddressY, LobbyAddressWidth, 22));
        items.Add(new MenuItem("join", LobbyJoinX, LobbyAddressY, LobbyJoinWidth, 22));
        items.Add(new MenuItem("server", LobbyFieldX, LobbyServerY, LobbyServerWidth, 22));
        items.Add(new MenuItem("back", BackButtonX, BackButtonY, BackButtonWidth, BackButtonHeight));
        return new MenuScreen(items);
    }
}
