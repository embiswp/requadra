using System.Text.Json;
using System.Text.Json.Serialization;
using Quadra.Core.Input;

namespace Quadra.Core.Settings;

/// <summary>
/// One player profile (<c>Config::player</c> and <c>player2</c> in <c>legacy/source/cfgfile.cc</c>).
/// Keys are stored by the name the front end gives them (for MonoGame, the <c>Keys</c> enum names), so the
/// file stays readable and does not depend on a platform's key codes.
/// </summary>
public sealed class PlayerSettings
{
    public const int SpeedLevels = 4;    // slow, normal, fast, faster
    public const int MaxNameLength = 39;

    public string Name { get; set; } = "";

    /// <summary>For the Internet game list later; unused for now.</summary>
    public string Password { get; set; } = "";

    /// <summary>Show where the piece would land.</summary>
    public bool Shadow { get; set; }

    /// <summary>Let the piece glide between positions instead of jumping from cell to cell.</summary>
    public bool Smooth { get; set; } = true;

    /// <summary>Key repeat speed sideways, 0 (slow) to 3 (faster).</summary>
    public int HorizontalSpeed { get; set; } = 2;

    /// <summary>Soft-drop speed, 0 (slow) to 3 (faster).</summary>
    public int VerticalSpeed { get; set; } = 2;

    /// <summary>Keep dropping a new piece while the down key is still held.</summary>
    public bool ContinuousDown { get; set; } = true;

    /// <summary>Team to join a network game in, 0 to 7, or -1 to take the first one nobody is in.</summary>
    public int Team { get; set; } = -1;

    /// <summary>Handicap in a network game, 0 (none) to 4: more blocks received, fewer sent.</summary>
    public int Handicap { get; set; }

    public const int MaxHandicap = 4;

    public Dictionary<GameAction, string> Keys { get; set; } = DefaultKeys();

    public static Dictionary<GameAction, string> DefaultKeys() => new()
    {
        [GameAction.Left] = "Left",
        [GameAction.Right] = "Right",
        [GameAction.RotateLeft] = "Up",
        [GameAction.Down] = "Down",
        [GameAction.RotateRight] = "Up",
        [GameAction.RotateTwice] = "RightShift",
        [GameAction.Drop] = "Space",
    };

    /// <summary>Repairs anything a hand-edited or old file got wrong.</summary>
    internal void Normalize(int index)
    {
        Name = (Name ?? "").Trim();
        if (Name.Length > MaxNameLength)
            Name = Name[..MaxNameLength];
        if (Name.Length == 0)
            Name = $"#{index + 1}";
        Password ??= "";
        Team = Math.Clamp(Team, -1, 7);
        Handicap = Math.Clamp(Handicap, 0, MaxHandicap);
        HorizontalSpeed = Math.Clamp(HorizontalSpeed, 0, SpeedLevels - 1);
        VerticalSpeed = Math.Clamp(VerticalSpeed, 0, SpeedLevels - 1);
        var keys = DefaultKeys();
        if (Keys is not null)
            foreach (var (action, key) in Keys)
                if (!string.IsNullOrWhiteSpace(key) && keys.ContainsKey(action))
                    keys[action] = key;
        Keys = keys;
    }
}

/// <summary>The rules of a game this player hosts (the game packet's rule fields).</summary>
public sealed class HostSettings
{
    public const int MaxComboMin = 4;
    public const int MaxEndValue = 999;

    public bool Survivor { get; set; }

    public Net.GameEnd End { get; set; }

    /// <summary>Frags, minutes, thousands of points or lines, by <see cref="End"/>.</summary>
    public int EndValue { get; set; }

    /// <summary>The fewest lines cleared at once that send any.</summary>
    public int ComboMin { get; set; }

    internal void Normalize()
    {
        if (!Enum.IsDefined(End))
            End = Net.GameEnd.Never;
        EndValue = Math.Clamp(EndValue, 0, MaxEndValue);
        ComboMin = Math.Clamp(ComboMin, 0, MaxComboMin);
    }
}

/// <summary>Everything the player can set (<c>Config</c>); saved as JSON.</summary>
public sealed class GameSettings
{
    public const int PlayerCount = 3;
    public const int LanguageCount = 2;     // English, French

    /// <summary>The three profiles; the one at <see cref="SetupPlayer"/> is the one that plays.</summary>
    public PlayerSettings[] Players { get; set; } = Enumerable.Range(0, PlayerCount).Select(_ => new PlayerSettings()).ToArray();

    /// <summary>Which profile the setup screen shows and the single-player game uses.</summary>
    public int SetupPlayer { get; set; }

    /// <summary>0 English, 1 French.</summary>
    public int Language { get; set; }

    /// <summary>Address of the game list server (its <c>qserv</c> URL); empty for none.</summary>
    public string ListServer { get; set; } = "";

    /// <summary>The last <c>host[:port]</c> typed on the multi-player screen.</summary>
    public string LastAddress { get; set; } = "";

    /// <summary>What "Host a game" on the multi-player screen starts.</summary>
    public HostSettings Rules { get; set; } = new();

    [JsonIgnore]
    public PlayerSettings CurrentPlayer => Players[SetupPlayer];

    public static GameSettings CreateDefault()
    {
        var settings = new GameSettings();
        settings.Normalize();
        return settings;
    }

    /// <summary>Makes the settings valid: three profiles, sane values, every key set.</summary>
    public void Normalize()
    {
        var players = new PlayerSettings[PlayerCount];
        for (int i = 0; i < PlayerCount; i++)
            players[i] = Players is not null && i < Players.Length && Players[i] is not null ? Players[i] : new PlayerSettings();
        Players = players;
        for (int i = 0; i < PlayerCount; i++)
            Players[i].Normalize(i);
        SetupPlayer = Math.Clamp(SetupPlayer, 0, PlayerCount - 1);
        Language = Math.Clamp(Language, 0, LanguageCount - 1);
        ListServer = (ListServer ?? "").Trim();
        LastAddress = (LastAddress ?? "").Trim();
        Rules ??= new HostSettings();
        Rules.Normalize();
    }
}

/// <summary>Reads and writes <see cref="GameSettings"/>.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads settings; a missing or damaged file gives the defaults instead of an error.</summary>
    public static GameSettings Load(string path)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(path), Options) ?? new GameSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return GameSettings.CreateDefault();
        }
    }

    public static void Save(string path, GameSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, overwrite: true);
    }
}
