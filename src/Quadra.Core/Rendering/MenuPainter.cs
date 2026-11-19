using Quadra.Core.Assets;
using Quadra.Core.Menus;
using Quadra.Core.Settings;

namespace Quadra.Core.Rendering;

/// <summary>
/// Paints the text and controls of the menu screens into a 640 x 480 overlay with a transparent
/// background; the screen's own picture goes underneath. Follows the original's look: framed value panels
/// (<c>Zone_state_text</c>, <c>Zone_text_input</c>) whose colours come from the picture's palette, hover
/// pictures drawn over the baked-in text, and yellow text buttons.
/// </summary>
public sealed class MenuPainter
{
    public delegate PixelBuffer? SpriteLookup(string file);

    private static readonly Rgb White = new(255, 255, 255);
    private static readonly Rgb Yellow = new(255, 255, 0);
    private static readonly Rgb Grey = new(170, 170, 170);
    private static readonly Rgb Magenta = new(255, 0, 255);

    private readonly LegacyFont _text;
    private readonly LegacyFont _numbers;
    private readonly SpriteLookup _sprites;
    private Rgb[] _palette = new Rgb[256];

    public MenuPainter(LegacyFont text, LegacyFont numbers, SpriteLookup sprites)
    {
        _text = text;
        _numbers = numbers;
        _sprites = sprites;
    }

    public PixelBuffer Overlay { get; } = new(SinglePlayerView.ScreenWidth, SinglePlayerView.ScreenHeight);

    /// <summary>The palette of the screen's picture; panel frames and fills use entries 210, 255 and 0.</summary>
    public void SetPalette(Rgb[] palette) => _palette = palette;

    private int PanelHeight => _text.Height + 2;

    /// <summary>Paints the screen the flow is on (everything except the running game).</summary>
    public void Paint(AppFlow flow, bool caretOn = true)
    {
        Overlay.Clear();
        switch (flow.Current)
        {
            case Screen.Main: PaintMain(flow); break;
            case Screen.Setup: PaintSetup(flow, caretOn); break;
            case Screen.Options: PaintOptions(flow); break;
            case Screen.HighScores: PaintHighScores(flow); break;
            case Screen.Lobby: PaintLobby(flow, caretOn); break;
        }
    }

    private void PaintMain(AppFlow flow)
    {
        PaintHover(flow.Menu);
        if (flow.Notice is { } notice)
            _text.Draw(Overlay, notice, (SinglePlayerView.ScreenWidth - _text.Measure(notice)) / 2, 455, Yellow);
    }

    private void PaintHover(MenuScreen menu)
    {
        if (menu.FocusedItem is { Hover: { } file } item && _sprites(file) is { } sprite)
            Overlay.Blit(sprite, item.X, item.Y);
    }

    // ---- setup --------------------------------------------------------------------------------------

    private void PaintSetup(AppFlow flow, bool caretOn)
    {
        var setup = flow.Setup;
        var menu = flow.Menu;
        PaintHover(menu);

        string? focusedId = menu.FocusedItem?.Id;
        foreach (var (id, (x, y, width)) in MenuLayouts.SetupValues)
        {
            string value = setup.ValueText(id);
            if (!caretOn && value.EndsWith('_'))
                value = value[..^1];
            bool typed = id is "name" or "password";
            Panel(x, y, width, value, centered: !typed, highlighted: focusedId == id);
        }

        if (setup.Prompt is { } prompt)
            Panel(MenuLayouts.SetupPromptX, MenuLayouts.SetupPromptY, MenuLayouts.SetupPromptWidth, prompt, centered: false, highlighted: true);

        BackButton(flow, focusedId == "back");
    }

    // ---- options ------------------------------------------------------------------------------------

    private void PaintOptions(AppFlow flow)
    {
        var text = flow.Text;
        string title = text.Get(LegacyText.GameOptions);
        _text.Draw(Overlay, title, (SinglePlayerView.ScreenWidth - _text.Measure(title)) / 2, 20, White);
        _text.Draw(Overlay, text.Get(LegacyText.SelectLanguage), 40, 100, Grey);

        string language = text.Get(flow.Language == 0 ? LegacyText.English : LegacyText.French);
        Panel(MenuLayouts.OptionsLanguageX, MenuLayouts.OptionsLanguageY, MenuLayouts.OptionsLanguageWidth, language,
            centered: true, highlighted: flow.Menu.FocusedItem?.Id == "language");
        BackButton(flow, flow.Menu.FocusedItem?.Id == "back");
    }

    // ---- lobby --------------------------------------------------------------------------------------

    private void PaintLobby(AppFlow flow, bool caretOn)
    {
        var lobby = flow.Lobby;
        string? focused = flow.Menu.FocusedItem?.Id;
        const string title = "Multi-player game";
        _text.Draw(Overlay, title, (SinglePlayerView.ScreenWidth - _text.Measure(title)) / 2, 20, White);

        int x = MenuLayouts.LobbyListX;
        _text.Draw(Overlay, "Game", x + 4, MenuLayouts.LobbyListY - 24, Grey);
        _text.Draw(Overlay, "Players", x + 340, MenuLayouts.LobbyListY - 24, Grey);
        _text.Draw(Overlay, "State", x + 450, MenuLayouts.LobbyListY - 24, Grey);
        for (int i = 0; i < Math.Min(lobby.Games.Count, MenuLayouts.LobbyRows); i++)
        {
            var parts = LobbyModel.Describe(lobby.Games[i]).Split('|');
            int y = MenuLayouts.LobbyListY + MenuLayouts.LobbyRowStep * i;
            var color = focused == $"game.{i}" ? Yellow : White;
            _text.Draw(Overlay, parts[0], x + 4, y, color);
            _text.Draw(Overlay, parts[1], x + 340, y, color);
            _text.Draw(Overlay, parts[2], x + 450, y, color);
        }

        _text.Draw(Overlay, "Host a game", MenuLayouts.LobbyHostX, MenuLayouts.LobbyButtonY + 2, focused == "host" ? White : Yellow);
        _text.Draw(Overlay, "Refresh", MenuLayouts.LobbyRefreshX, MenuLayouts.LobbyButtonY + 2, focused == "refresh" ? White : Yellow);

        _text.Draw(Overlay, "Team", MenuLayouts.LobbyTeamX - 50, MenuLayouts.LobbyButtonY + 2, Grey);
        Panel(MenuLayouts.LobbyTeamX, MenuLayouts.LobbyButtonY, MenuLayouts.LobbyTeamWidth, lobby.ValueText("team"), centered: true, highlighted: focused == "team");
        _text.Draw(Overlay, "Handicap", MenuLayouts.LobbyHandicapX - 90, MenuLayouts.LobbyButtonY + 2, Grey);
        Panel(MenuLayouts.LobbyHandicapX, MenuLayouts.LobbyButtonY, MenuLayouts.LobbyHandicapWidth, lobby.ValueText("handicap"), centered: true, highlighted: focused == "handicap");

        _text.Draw(Overlay, "Address", MenuLayouts.LobbyHostX, MenuLayouts.LobbyAddressY + 2, Grey);
        Panel(MenuLayouts.LobbyFieldX, MenuLayouts.LobbyAddressY, MenuLayouts.LobbyAddressWidth,
            Caret(lobby.FieldText("address"), caretOn), centered: false, highlighted: focused == "address");
        _text.Draw(Overlay, "Join", MenuLayouts.LobbyJoinX, MenuLayouts.LobbyAddressY + 2, focused == "join" ? White : Yellow);

        _text.Draw(Overlay, "Game list", MenuLayouts.LobbyHostX, MenuLayouts.LobbyServerY + 2, Grey);
        Panel(MenuLayouts.LobbyFieldX, MenuLayouts.LobbyServerY, MenuLayouts.LobbyServerWidth,
            Caret(lobby.FieldText("server"), caretOn), centered: false, highlighted: focused == "server");

        _text.Draw(Overlay, lobby.Status, MenuLayouts.LobbyHostX, MenuLayouts.LobbyStatusY, Grey);
        BackButton(flow, focused == "back");
    }

    private static string Caret(string value, bool caretOn) => !caretOn && value.EndsWith('_') ? value[..^1] : value;

    // ---- high scores --------------------------------------------------------------------------------

    private void PaintHighScores(AppFlow flow)
    {
        foreach (var item in flow.Menu.Items)
        {
            bool focused = flow.Menu.FocusedItem == item;
            if ((focused ? item.Hover : item.Rest) is { } file && _sprites(file) is { } sprite)
                Overlay.Blit(sprite, item.X, item.Y);
        }

        var text = flow.Text;
        int x = MenuLayouts.ScoreTableX;
        _text.Draw(Overlay, text.Get(LegacyText.HeroesLabel), x, MenuLayouts.ScoreHeadingY, White);
        _text.Draw(Overlay, text.Get(LegacyText.ScoreColumn), x + 190, MenuLayouts.ScoreHeadingY, White);
        _text.Draw(Overlay, text.Get(LegacyText.LinesColumn), x + 290, MenuLayouts.ScoreHeadingY, White);
        _text.Draw(Overlay, text.Get(LegacyText.LevelColumn), x + 390, MenuLayouts.ScoreHeadingY, White);

        var entries = flow.Scores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var color = i == flow.HighlightedRank ? Yellow : White;
            int y = MenuLayouts.ScoreRowY + MenuLayouts.ScoreRowStep * i;
            _text.Draw(Overlay, entry.Name, x, y, color);
            RightAligned(entry.Score, x + 160, y, 80, color);
            RightAligned(entry.Lines, x + 260, y, 80, color);
            RightAligned(entry.Level, x + 360, y, 80, color);
        }
    }

    private void RightAligned(int value, int x, int y, int width, Rgb color)
    {
        string text = value.ToString();
        _numbers.Draw(Overlay, text, x + width - _numbers.Measure(text) - 3, y + 2, color);
    }

    // ---- shared pieces ------------------------------------------------------------------------------

    private void BackButton(AppFlow flow, bool focused)
    {
        string text = flow.Text.Get(LegacyText.Back);
        _text.Draw(Overlay, text, MenuLayouts.BackButtonX, MenuLayouts.BackButtonY, focused ? White : Yellow);
    }

    /// <summary>
    /// A framed box with text: a border in palette entry 210 and a near-black inside (entry 0). A
    /// highlighted one has a bright inner line (entry 255), as the original's hovered panels do.
    /// </summary>
    private void Panel(int x, int y, int width, string text, bool centered, bool highlighted)
    {
        int height = PanelHeight;
        uint border = PixelBuffer.Pack(_palette[210]);
        uint inside = PixelBuffer.Pack(_palette[0]);
        Overlay.HLine(y, x, width, border);
        Overlay.HLine(y + height - 1, x, width, border);
        Overlay.VLine(x, y + 1, height - 2, border);
        Overlay.VLine(x + width - 1, y + 1, height - 2, border);
        for (int row = 1; row < height - 1; row++)
            Overlay.HLine(y + row, x + 1, width - 2, inside);
        if (highlighted)
        {
            uint bright = PixelBuffer.Pack(_palette[255]);
            Overlay.HLine(y + 1, x + 1, width - 2, bright);
            Overlay.HLine(y + height - 2, x + 1, width - 2, bright);
            Overlay.VLine(x + 1, y + 2, height - 4, bright);
            Overlay.VLine(x + width - 2, y + 2, height - 4, bright);
        }
        int textX = centered ? x + (width - _text.Measure(text)) / 2 : x + 4;
        _text.Draw(Overlay, text, textX, y + 2, White);
    }
}
