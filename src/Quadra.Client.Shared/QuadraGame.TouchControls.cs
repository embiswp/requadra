using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;
using Quadra.Core.Assets;
using Quadra.Core.Input;
using Quadra.Core.Rendering;

namespace Quadra.Client;

/// <summary>
/// On a phone there is no keyboard: while playing, buttons drawn in the margins beside the letterboxed
/// picture (there is no gameplay there to cover) stand in for the arrow keys, rotate, drop, pause and
/// going back to the menu.
/// </summary>
public sealed partial class QuadraGame
{
    private enum TouchKind { Hold, Pause, Menu }

    private sealed record TouchButton(string Label, TouchKind Kind, GameAction Action = default);

    private static readonly TouchButton[] LeftBar =
    [
        new("MENU", TouchKind.Menu),
        new("<", TouchKind.Hold, GameAction.Left),
        new(">", TouchKind.Hold, GameAction.Right),
    ];

    // Drop and rotate are used the most, so they sit at the bottom, closest to the thumb.
    private static readonly TouchButton[] RightBar =
    [
        new("PAUSE", TouchKind.Pause),
        new("v", TouchKind.Hold, GameAction.Down),
        new("^", TouchKind.Hold, GameAction.RotateRight),
        new("DROP", TouchKind.Hold, GameAction.Drop),
    ];

    private Texture2D[]? _leftLabels;
    private Texture2D[]? _rightLabels;
    private readonly Rectangle[] _leftAreas = new Rectangle[LeftBar.Length];
    private readonly Rectangle[] _rightAreas = new Rectangle[RightBar.Length];
    private bool _pauseHeld;
    private bool _menuHeld;

    private void LoadTouchControls()
    {
        _leftLabels = BuildLabels(LeftBar);
        _rightLabels = BuildLabels(RightBar);
    }

    private Texture2D[] BuildLabels(TouchButton[] buttons) =>
        buttons.Select(b =>
        {
            var buffer = new PixelBuffer(_assets.TextFont.Measure(b.Label) + 2, _assets.TextFont.Height);
            _assets.TextFont.Draw(buffer, b.Label, 1, 0, new Rgb(255, 255, 255));
            return _assets.ToTexture(buffer);
        }).ToArray();

    private void LayoutTouchControls()
    {
        int windowWidth = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int windowHeight = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var picture = Fit.Area;
        const int margin = 8;
        var left = new Rectangle(margin, 0, Math.Max(picture.X - margin * 2, 0), windowHeight);
        var right = new Rectangle(picture.Right + margin, 0, Math.Max(windowWidth - picture.Right - margin * 2, 0), windowHeight);
        LayoutBar(left, LeftBar, _leftAreas);
        LayoutBar(right, RightBar, _rightAreas);
    }

    /// <summary>Pause and menu are rarely used and tapped rather than held, so they get half the height of the others.</summary>
    private static double Weight(TouchKind kind) => kind is TouchKind.Pause or TouchKind.Menu ? 0.5 : 1.0;

    /// <summary><paramref name="buttons"/> stacked top to bottom in <paramref name="bar"/>, sized by <see cref="Weight"/> with a gap between them.</summary>
    private static void LayoutBar(Rectangle bar, TouchButton[] buttons, Rectangle[] areas)
    {
        const int gap = 8;
        double totalWeight = buttons.Sum(b => Weight(b.Kind));
        double unitHeight = (bar.Height - gap * (buttons.Length + 1)) / totalWeight;
        int y = bar.Y + gap;
        for (int i = 0; i < buttons.Length; i++)
        {
            int height = (int)(Weight(buttons[i].Kind) * unitHeight);
            areas[i] = new Rectangle(bar.X, y, bar.Width, height);
            y += height + gap;
        }
    }

    /// <summary>Any touch currently inside a button's area holds (or, for pause/menu, taps) that button.</summary>
    private void UpdateTouchControls()
    {
        LayoutTouchControls();
        var touches = TouchPanel.GetState();
        bool Touched(Rectangle area) => touches.Any(t => t.State is TouchLocationState.Pressed or TouchLocationState.Moved
            && area.Contains((int)t.Position.X, (int)t.Position.Y));

        for (int i = 0; i < LeftBar.Length; i++)
            UpdateButton(LeftBar[i], Touched(_leftAreas[i]));
        for (int i = 0; i < RightBar.Length; i++)
            UpdateButton(RightBar[i], Touched(_rightAreas[i]));
    }

    private void UpdateButton(TouchButton button, bool touched)
    {
        switch (button.Kind)
        {
            case TouchKind.Hold:
                _controls.SetAction(button.Action, touched);
                break;
            case TouchKind.Pause:
                if (touched && !_pauseHeld && !NetworkActive && _game is { IsOver: false })
                {
                    _paused = !_paused;
                    _sounds.Pause();
                }
                _pauseHeld = touched;
                break;
            case TouchKind.Menu:
                if (touched && !_menuHeld)
                    GoToMenu();
                _menuHeld = touched;
                break;
        }
    }

    /// <summary>What Escape does: leave the game in progress, the same way for a tap as for a key.</summary>
    private void GoToMenu()
    {
        if (NetworkActive)
        {
            bool fromMenu = _fromMenu;
            StopNetwork();
            if (fromMenu)
                _flow.ReturnToLobby();
            else
                Exit();
        }
        else
        {
            _flow.Back();
        }
    }

    private void DrawTouchControls()
    {
        DrawBar(LeftBar, _leftLabels, _leftAreas);
        DrawBar(RightBar, _rightLabels, _rightAreas);
    }

    private void DrawBar(TouchButton[] buttons, Texture2D[]? labels, Rectangle[] areas)
    {
        if (labels is null)
            return;
        for (int i = 0; i < buttons.Length; i++)
        {
            var area = areas[i];
            if (area.Width <= 0 || area.Height <= 0)
                continue;
            _batch.Draw(_pixel, area, Color.White * 0.25f);
            var label = labels[i];
            float scale = Math.Min(area.Width * 0.6f / label.Width, area.Height * 0.6f / label.Height);
            int width = (int)(label.Width * scale), height = (int)(label.Height * scale);
            var destination = new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
            _batch.Draw(label, destination, Color.White);
        }
    }
}
