namespace Quadra.Core.Menus;

/// <summary>The ids of the items on the menu screens: what a click or a key press on an item reports.</summary>
public static class MenuId
{
    // Several screens
    public const string Back = "back";

    // Main menu
    public const string SinglePlayer = "single";
    public const string MultiPlayer = "multi";
    public const string Demo = "demo";
    public const string HighScores = "scores";
    public const string Setup = "setup";
    public const string Options = "options";
    public const string Help = "help";
    public const string Quit = "quit";

    // Options
    public const string Language = "language";

    // High scores
    public const string PlayAgain = "again";

    // Player setup
    public const string Player = "player";
    public const string Name = "name";
    public const string Password = "password";
    public const string Shadow = "shadow";
    public const string Smooth = "smooth";
    public const string HorizontalSpeed = "hspeed";
    public const string VerticalSpeed = "vspeed";
    public const string ContinuousDown = "continuous";
    public const string KeyLeft = "key.left";
    public const string KeyRight = "key.right";
    public const string KeyDown = "key.down";
    public const string KeyDrop = "key.drop";
    public const string KeyRotateClockwise = "key.rotatecw";
    public const string KeyRotateCounterClockwise = "key.rotateccw";
    public const string KeyRotateTwice = "key.twice";
    public const string AllKeys = "allkeys";

    // Lobby
    public const string Host = "host";
    public const string Refresh = "refresh";
    public const string Team = "team";
    public const string Handicap = "handicap";
    public const string Address = "address";
    public const string Join = "join";
    public const string FindOnLan = "lan";
    public const string ListServer = "server";
    public const string Survivor = "survivor";
    public const string GameEnd = "end";
    public const string GameEndValue = "endvalue";
    public const string MinimumCombo = "combo";

    /// <summary>A game of the list is "game." and its place in the list.</summary>
    public const string GamePrefix = "game.";
}
