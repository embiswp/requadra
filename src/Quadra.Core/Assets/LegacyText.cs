using System.Text;

namespace Quadra.Core.Assets;

/// <summary>
/// The game's text table (<c>textes/anglais.txt</c> and <c>francais.txt</c>): one string per line in
/// Latin-1. The original refers to a string by a 1-based number (<c>ST_BASE(n)</c> in
/// <c>legacy/source/texte.h</c>); the numbers used here are the constants below.
/// </summary>
public sealed class LegacyText
{
    // Numbers of the strings the single-player screen uses (texte.h).
    public const int ScoreLabel = 103;
    public const int LinesLabel = 104;
    public const int LevelLabel = 105;
    public const int FirstClearName = 111;   // "Single", "Double", ... up to "14-lines"
    public const int MoreClears = 125;
    public const int Total = 126;

    // Strings for the menus.
    public const int HeroesLabel = 4;
    public const int ScoreColumn = 5;
    public const int LinesColumn = 6;
    public const int LevelColumn = 7;
    public const int Back = 16;
    public const int Slow = 20;
    public const int Normal = 21;
    public const int Fast = 22;
    public const int PressAKey = 23;
    public const int PressLeft = 24;
    public const int PressRight = 25;
    public const int PressRotateCounterClockwise = 26;
    public const int PressDown = 27;
    public const int PressRotateClockwise = 28;
    public const int Yes = 171;
    public const int No = 172;
    public const int GameOptions = 182;
    public const int Faster = 370;
    public const int SelectLanguage = 457;
    public const int English = 458;
    public const int French = 459;
    public const int PressRotateTwice = 583;
    public const int PressDrop = 584;

    private readonly string[] _lines;

    public LegacyText(byte[] file)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _lines = Encoding.Latin1.GetString(file).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
    }

    /// <summary>The string with a 1-based number, as in <c>ST_BASE</c>.</summary>
    public string Get(int number)
    {
        if (number < 1 || number > _lines.Length)
            throw new ArgumentOutOfRangeException(nameof(number));
        return _lines[number - 1];
    }
}
