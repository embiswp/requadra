using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quadra.Core.Menus;

namespace Quadra.Client;

/// <summary>Turns the mouse and keyboard into what the menu screens understand (<see cref="AppFlow"/>), with the sounds that go with it.</summary>
internal sealed class MenuInput(AppFlow flow, GameSounds sounds)
{
    private int _hoveredItem = -1;

    /// <summary>A new screen is shown: nothing is hovered yet.</summary>
    public void ScreenChanged() => _hoveredItem = -1;

    /// <param name="pointer">Where the mouse is, in the 640 x 480 picture's coordinates.</param>
    public void Update(KeyboardState keyboard, KeyboardState keyboardBefore, MouseState mouse, MouseState mouseBefore, Point pointer)
    {
        bool WasPressed(Keys key) => keyboard.IsKeyDown(key) && !keyboardBefore.IsKeyDown(key);

        var menu = flow.Menu;
        var setup = flow.Current == Screen.Setup ? flow.Setup : null;

        // Pointer: hovering focuses (with a sound), a click activates.
        if (mouse.X != mouseBefore.X || mouse.Y != mouseBefore.Y)
        {
            menu.PointerMoved(pointer.X, pointer.Y);
            if (menu.Focus != _hoveredItem)
            {
                _hoveredItem = menu.Focus;
                if (menu.Focus >= 0)
                    sounds.MenuHover();
            }
        }
        if (mouse.LeftButton == ButtonState.Pressed && mouseBefore.LeftButton == ButtonState.Released
            && menu.PointerPressed(pointer.X, pointer.Y) is { } clicked)
            Choose(clicked);

        // Keyboard.
        if (setup is { Mode: SetupMode.CapturingKey })
        {
            foreach (var key in keyboard.GetPressedKeys().Where(k => !keyboardBefore.IsKeyDown(k)))
            {
                setup.KeyPressed(key.ToString());
                break;      // one key at a time
            }
            return;
        }
        if (setup is { Mode: SetupMode.EditingName or SetupMode.EditingPassword })
        {
            if (WasPressed(Keys.Back))
                setup.Backspace();
            if (WasPressed(Keys.Enter))
                setup.Commit();
            if (WasPressed(Keys.Escape))
                flow.Back();
            return;
        }

        if (flow.Current == Screen.Lobby && flow.Lobby.Edit != LobbyEdit.None)
        {
            if (WasPressed(Keys.Back))
                flow.Lobby.Backspace();
            if (WasPressed(Keys.Enter) || WasPressed(Keys.Escape))
                flow.Back();       // Enter and Escape both end the typing; the text stays
            return;
        }

        if (WasPressed(Keys.Down))
            MoveFocus(menu, 1);
        if (WasPressed(Keys.Up))
            MoveFocus(menu, -1);
        if (menu.FocusedItem is { } focused)
        {
            if (WasPressed(Keys.Left))
                flow.Cycle(focused.Id, -1);
            if (WasPressed(Keys.Right))
                flow.Cycle(focused.Id, 1);
            if (WasPressed(Keys.Enter) || WasPressed(Keys.Space))
                Choose(focused.Id);
        }
        if (WasPressed(Keys.Escape))
            flow.Back();
    }

    private void MoveFocus(MenuScreen menu, int direction)
    {
        menu.MoveFocus(direction);
        _hoveredItem = menu.Focus;
        sounds.MenuHover();
    }

    /// <summary>Something was chosen: a click sound (a tap sound for a value), then the flow decides.</summary>
    private void Choose(string id)
    {
        bool isValue = flow.Current == Screen.Setup && id is MenuId.Player or MenuId.Shadow or MenuId.Smooth or MenuId.HorizontalSpeed or MenuId.VerticalSpeed or MenuId.ContinuousDown
                       || flow.Current == Screen.Options && id == MenuId.Language;
        if (isValue)
            sounds.MenuValue();
        else
            sounds.MenuClick();
        flow.Activate(id);
    }
}
