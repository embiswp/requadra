using Quadra.Core.Assets;
using Quadra.Core.Input;
using Quadra.Core.Settings;

namespace Quadra.Core.Menus;

public enum SetupMode
{
    Browsing,
    EditingName,
    EditingPassword,

    /// <summary>The next key pressed becomes the binding of <see cref="SetupModel.CaptureAction"/>.</summary>
    CapturingKey,
}

/// <summary>
/// The logic of the player setup screen (<c>Menu_setup</c>): what each item does when clicked, how values
/// are shown, typing in the name and password fields, and capturing keys one at a time or all at once.
/// The front end feeds it clicks and key presses and draws what it reports.
/// </summary>
public sealed class SetupModel
{
    private const int MaxPasswordLength = 63;

    // "Set all keys" asks for the keys in this order (<c>Menu_setup_all_key</c>), with these prompts.
    private static readonly (GameAction Action, int Prompt)[] AllKeysOrder =
    [
        (GameAction.Left, LegacyText.PressLeft),
        (GameAction.Right, LegacyText.PressRight),
        (GameAction.Down, LegacyText.PressDown),
        (GameAction.RotateRight, LegacyText.PressRotateClockwise),
        (GameAction.RotateLeft, LegacyText.PressRotateCounterClockwise),
        (GameAction.RotateTwice, LegacyText.PressRotateTwice),
        (GameAction.Drop, LegacyText.PressDrop),
    ];

    private static readonly Dictionary<string, GameAction> KeyItems = new()
    {
        [MenuId.KeyLeft] = GameAction.Left,
        [MenuId.KeyRight] = GameAction.Right,
        [MenuId.KeyDown] = GameAction.Down,
        [MenuId.KeyDrop] = GameAction.Drop,
        [MenuId.KeyRotateClockwise] = GameAction.RotateRight,
        [MenuId.KeyRotateCounterClockwise] = GameAction.RotateLeft,
        [MenuId.KeyRotateTwice] = GameAction.RotateTwice,
    };

    private readonly GameSettings _settings;
    private LegacyText _text;
    private int _allKeysStep = -1;      // -1: not running "set all keys"

    public SetupModel(GameSettings settings, LegacyText text)
    {
        _settings = settings;
        _text = text;
    }

    public SetupMode Mode { get; private set; } = SetupMode.Browsing;

    /// <summary>The action a captured key will be bound to (only while capturing).</summary>
    public GameAction? CaptureAction { get; private set; }

    /// <summary>The prompt to show while a key is being captured, or null.</summary>
    public string? Prompt { get; private set; }

    public PlayerSettings Player => _settings.CurrentPlayer;

    /// <summary>Changes the language of the strings (after the language setting changed).</summary>
    public void SetText(LegacyText text) => _text = text;

    /// <summary>What to show for a setting: a number, a name, Yes or No, a speed, a key.</summary>
    public string ValueText(string id)
    {
        switch (id)
        {
            case MenuId.Player: return (_settings.SetupPlayer + 1).ToString();
            case MenuId.Name: return Player.Name + (Mode == SetupMode.EditingName ? "_" : "");
            case MenuId.Password: return new string('*', Player.Password.Length) + (Mode == SetupMode.EditingPassword ? "_" : "");
            case MenuId.Shadow: return YesNo(Player.Shadow);
            case MenuId.Smooth: return YesNo(Player.Smooth);
            case MenuId.ContinuousDown: return YesNo(Player.ContinuousDown);
            case MenuId.HorizontalSpeed: return Speed(Player.HorizontalSpeed);
            case MenuId.VerticalSpeed: return Speed(Player.VerticalSpeed);
        }
        if (KeyItems.TryGetValue(id, out var action))
            return Mode == SetupMode.CapturingKey && CaptureAction == action
                ? "?"
                : KeyNames.Friendly(Player.Keys[action]);
        return "";
    }

    /// <summary>A click on an item. Returns true if the model handled it ("back" is left to the caller).</summary>
    public bool Activate(string id)
    {
        if (Mode != SetupMode.Browsing)
            Cancel();           // clicking elsewhere ends typing or capturing, as clicking away does in the original

        switch (id)
        {
            case MenuId.Name:
                Mode = SetupMode.EditingName;
                return true;
            case MenuId.Password:
                Mode = SetupMode.EditingPassword;
                return true;
            case MenuId.AllKeys:
                _allKeysStep = 0;
                BeginCapture(AllKeysOrder[0].Action, AllKeysOrder[0].Prompt);
                return true;
        }
        if (KeyItems.TryGetValue(id, out var action))
        {
            BeginCapture(action, LegacyText.PressAKey);
            return true;
        }
        return Cycle(id, 1);
    }

    /// <summary>Steps a value forward (+1) or back (-1): the profile, Yes/No, or a speed.</summary>
    public bool Cycle(string id, int direction)
    {
        switch (id)
        {
            case MenuId.Player:
                _settings.SetupPlayer = ((_settings.SetupPlayer + direction) % GameSettings.PlayerCount + GameSettings.PlayerCount) % GameSettings.PlayerCount;
                return true;
            case MenuId.Shadow: Player.Shadow = !Player.Shadow; return true;
            case MenuId.Smooth: Player.Smooth = !Player.Smooth; return true;
            case MenuId.ContinuousDown: Player.ContinuousDown = !Player.ContinuousDown; return true;
            case MenuId.HorizontalSpeed: Player.HorizontalSpeed = Wrap(Player.HorizontalSpeed + direction); return true;
            case MenuId.VerticalSpeed: Player.VerticalSpeed = Wrap(Player.VerticalSpeed + direction); return true;
        }
        return false;
    }

    /// <summary>A typed character, while a text field is being edited.</summary>
    public bool TypeCharacter(char c)
    {
        if (Mode is not (SetupMode.EditingName or SetupMode.EditingPassword) || !IsTypable(c))
            return false;
        if (Mode == SetupMode.EditingName)
        {
            if (Player.Name.Length < PlayerSettings.MaxNameLength)
                Player.Name += c;
        }
        else if (Player.Password.Length < MaxPasswordLength)
        {
            Player.Password += c;
        }
        return true;
    }

    public bool Backspace()
    {
        if (Mode == SetupMode.EditingName && Player.Name.Length > 0)
            Player.Name = Player.Name[..^1];
        else if (Mode == SetupMode.EditingPassword && Player.Password.Length > 0)
            Player.Password = Player.Password[..^1];
        else
            return Mode is SetupMode.EditingName or SetupMode.EditingPassword;
        return true;
    }

    /// <summary>Enter: ends typing.</summary>
    public bool Commit()
    {
        if (Mode is not (SetupMode.EditingName or SetupMode.EditingPassword))
            return false;
        Mode = SetupMode.Browsing;
        return true;
    }

    /// <summary>A key went down while capturing: it becomes the binding, and "set all keys" moves on.</summary>
    public bool KeyPressed(string keyName)
    {
        if (Mode != SetupMode.CapturingKey || CaptureAction is not { } action)
            return false;
        if (keyName == "Escape")
        {
            Cancel();
            return true;
        }
        if (keyName == "Enter")
            return true;        // refused, as in the original (Enter is for confirming)

        Player.Keys[action] = keyName;
        if (_allKeysStep >= 0 && ++_allKeysStep < AllKeysOrder.Length)
            BeginCapture(AllKeysOrder[_allKeysStep].Action, AllKeysOrder[_allKeysStep].Prompt);
        else
            EndCapture();
        return true;
    }

    /// <summary>Escape: stops typing or capturing (keeping what was typed or bound so far). False if there was nothing to cancel.</summary>
    public bool Cancel()
    {
        if (Mode == SetupMode.Browsing)
            return false;
        Mode = SetupMode.Browsing;
        EndCapture();
        return true;
    }

    private void BeginCapture(GameAction action, int prompt)
    {
        Mode = SetupMode.CapturingKey;
        CaptureAction = action;
        Prompt = _text.Get(prompt);
    }

    private void EndCapture()
    {
        Mode = SetupMode.Browsing;
        CaptureAction = null;
        Prompt = null;
        _allKeysStep = -1;
    }

    private string YesNo(bool value) => _text.Get(value ? LegacyText.Yes : LegacyText.No);

    private string Speed(int level) => _text.Get(level switch
    {
        0 => LegacyText.Slow,
        1 => LegacyText.Normal,
        2 => LegacyText.Fast,
        _ => LegacyText.Faster,
    });

    private static int Wrap(int level) => (level % PlayerSettings.SpeedLevels + PlayerSettings.SpeedLevels) % PlayerSettings.SpeedLevels;

    /// <summary>Printable Latin-1 (what the game's font can show).</summary>
    private static bool IsTypable(char c) => c is >= ' ' and <= '~' or >= ' ' and <= 'ÿ';
}
