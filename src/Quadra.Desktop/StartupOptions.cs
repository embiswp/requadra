namespace Quadra.Desktop;

/// <summary>
/// Command line options. Most exist to check a build without a person at the keyboard
/// (a "smoke" run plays by itself for a moment and exits).
/// </summary>
public sealed record StartupOptions
{
    /// <summary>If above zero, run this many window updates by themselves, then exit.</summary>
    public int SmokeUpdates { get; init; }

    /// <summary>In a smoke run, save the last frame to this PNG file.</summary>
    public string? ScreenshotPath { get; init; }

    /// <summary>Level to start a game at (each level has its own background, colours and sounds).</summary>
    public int StartLevel { get; init; } = 1;

    /// <summary>In a smoke run, show the pause sign in the last frame.</summary>
    public bool PauseAtEnd { get; init; }

    /// <summary>In a smoke run, start a level change early on to see the fade.</summary>
    public bool FadeTest { get; init; }

    /// <summary>Open this screen first: main, setup, options, scores or play.</summary>
    public string? Screen { get; init; }

    /// <summary>With <see cref="Screen"/>, put the keyboard focus on this menu item.</summary>
    public string? Focus { get; init; }

    /// <summary>Use this folder for settings and high scores instead of the user's own.</summary>
    public string? DataDirectory { get; init; }

    /// <summary>
    /// A scripted run: steps separated by semicolons, one per window update (see <c>QuadraGame.ReadInput</c>),
    /// for checking the menus end to end without a person.
    /// </summary>
    public string? Script { get; init; }

    /// <summary>Language to start in for a smoke run (0 English, 1 French); not saved.</summary>
    public int? Language { get; init; }

    public static StartupOptions Parse(string[] args)
    {
        string? Value(string name) =>
            Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

        int smoke = 0;
        int smokeAt = Array.IndexOf(args, "--smoke");
        if (smokeAt >= 0)
            smoke = smokeAt + 1 < args.Length && int.TryParse(args[smokeAt + 1], out int n) ? n : 120;

        return new StartupOptions
        {
            SmokeUpdates = smoke,
            ScreenshotPath = Value("--screenshot"),
            StartLevel = int.TryParse(Value("--level"), out int level) && level >= 1 ? level : 1,
            PauseAtEnd = args.Contains("--pause"),
            FadeTest = args.Contains("--fade"),
            Screen = Value("--screen"),
            Focus = Value("--focus"),
            DataDirectory = Value("--data-dir"),
            Script = Value("--script"),
            Language = int.TryParse(Value("--lang"), out int lang) ? lang : null,
        };
    }
}
