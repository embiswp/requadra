namespace Quadra.Core.Menus;

/// <summary>
/// One clickable thing on a menu screen: a rectangle, and optionally the pictures drawn for it
/// (<c>Rest</c> always, <c>Hover</c> while the pointer or keyboard focus is on it). The original's main
/// menu has its text baked into the background, so those items only have a <c>Hover</c> picture.
/// </summary>
public sealed record MenuItem(string Id, int X, int Y, int Width, int Height, string? Rest = null, string? Hover = null)
{
    /// <summary>False for extra hit areas (such as a value next to its label) that the keyboard skips.</summary>
    public bool KeyboardFocusable { get; init; } = true;

    public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

/// <summary>
/// The items of one screen and which of them has the focus. Pointer and keyboard share the focus, the way
/// most menus of that era do: moving the pointer over an item focuses it, and arrow keys move on from there.
/// </summary>
public sealed class MenuScreen
{
    public MenuScreen(IReadOnlyList<MenuItem> items) => Items = items;

    public IReadOnlyList<MenuItem> Items { get; }

    /// <summary>Index of the focused item, or -1 for none.</summary>
    public int Focus { get; private set; } = -1;

    public MenuItem? FocusedItem => Focus >= 0 ? Items[Focus] : null;

    /// <summary>Focuses the item under the pointer; none if the pointer is over nothing.</summary>
    public void PointerMoved(int x, int y)
    {
        Focus = -1;
        for (int i = 0; i < Items.Count; i++)
            if (Items[i].Contains(x, y))
            {
                Focus = i;
                return;
            }
    }

    /// <summary>A click: returns the id of the item under the pointer, if any.</summary>
    public string? PointerPressed(int x, int y)
    {
        PointerMoved(x, y);
        return FocusedItem?.Id;
    }

    /// <summary>Moves the focus to the next (+1) or previous (-1) item that the keyboard can reach, wrapping round.</summary>
    public void MoveFocus(int direction)
    {
        var reachable = Enumerable.Range(0, Items.Count).Where(i => Items[i].KeyboardFocusable).ToList();
        if (reachable.Count == 0)
            return;
        int at = reachable.IndexOf(Focus);
        if (at < 0)
            Focus = direction > 0 ? reachable[0] : reachable[^1];
        else
            Focus = reachable[((at + direction) % reachable.Count + reachable.Count) % reachable.Count];
    }

    /// <summary>The id of the focused item (Enter pressed).</summary>
    public string? ActivateFocused() => FocusedItem?.Id;

    /// <summary>Makes the item with an id the focused one.</summary>
    public void FocusId(string id)
    {
        int i = Items.ToList().FindIndex(item => item.Id == id && item.KeyboardFocusable);
        if (i >= 0)
            Focus = i;
    }
}
