namespace Quadra.Core.Input;

/// <summary>State of one key as the game sees it between two frames (<c>PRESSED</c> and <c>RELEASED</c> in <c>input.h</c>).</summary>
[Flags]
public enum KeyFlags : byte
{
    None = 0,

    /// <summary>The key went down since the last frame and has not gone up.</summary>
    Pressed = 1,

    /// <summary>The key went up since the last frame (a tap shows as Released only).</summary>
    Released = 2,
}

/// <summary>
/// Key state as <c>Input_SDL</c> keeps it: a key-down event sets Pressed, a key-up event replaces the
/// state with Released, and the game clears what it has handled. Keys are plain integers so any
/// platform (keyboard, gamepad, touch buttons) can feed it.
/// </summary>
public sealed class KeyStates
{
    private readonly Dictionary<int, KeyFlags> _states = [];

    public KeyFlags Get(int key) => _states.GetValueOrDefault(key);

    /// <summary>Key went down.</summary>
    public void Down(int key) => _states[key] = Get(key) | KeyFlags.Pressed;

    /// <summary>Key went up.</summary>
    public void Up(int key) => _states[key] = KeyFlags.Released;

    /// <summary>Forgets everything about a key (<c>Canvas::clear_key</c>).</summary>
    public void Clear(int key) => _states.Remove(key);

    /// <summary>Marks a Released key as handled but keeps Pressed (<c>Canvas::unrelease_key</c>).</summary>
    public void Unrelease(int key) => _states[key] = Get(key) & ~KeyFlags.Released;

    public void ClearAll() => _states.Clear();

    public KeyStates Clone()
    {
        var copy = new KeyStates();
        copy.CopyFrom(this);
        return copy;
    }

    public void CopyFrom(KeyStates other)
    {
        _states.Clear();
        foreach (var (key, flags) in other._states)
            _states[key] = flags;
    }
}
