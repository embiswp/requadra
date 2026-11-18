using Quadra.Core.Game;
using Quadra.Core.Rules;
using Quadra.Core.Assets;
using Quadra.Core.Input;
using Quadra.Core.Menus;
using Quadra.Core.Rendering;
using Quadra.Core.Settings;

namespace Quadra.Tests;

internal static class MenuFixtures
{
    public static (int, int) ImageSize(string file) => PngInfo.Size(Legacy.Bytes($"images/{file}"));

    public static LegacyText Text(int language) => new(Legacy.Bytes(language == 1 ? "textes/francais.txt" : "textes/anglais.txt"));

    public static AppFlow NewFlow(int language = 0, Action? saveSettings = null, Action? saveScores = null)
    {
        var settings = GameSettings.CreateDefault();
        settings.Language = language;
        return new AppFlow(settings, new HighScoreTable(), ImageSize, Text, saveSettings ?? (() => { }), saveScores ?? (() => { }));
    }
}

public class MenuScreenTests
{
    private static MenuScreen Screen() => new(
    [
        new MenuItem("a", 10, 10, 100, 20),
        new MenuItem("b", 10, 40, 100, 20),
        new MenuItem("value", 200, 10, 50, 20) { KeyboardFocusable = false },
        new MenuItem("c", 10, 70, 100, 20),
    ]);

    [Fact]
    public void The_pointer_focuses_the_item_under_it_and_nothing_elsewhere()
    {
        var m = Screen();
        m.PointerMoved(50, 45);
        Assert.Equal("b", m.FocusedItem!.Id);
        m.PointerMoved(150, 150);
        Assert.Null(m.FocusedItem);
        Assert.Equal(-1, m.Focus);
    }

    [Fact]
    public void Edges_are_inside_on_the_left_and_top_and_outside_on_the_right_and_bottom()
    {
        var item = new MenuItem("x", 10, 10, 100, 20);
        Assert.True(item.Contains(10, 10));
        Assert.True(item.Contains(109, 29));
        Assert.False(item.Contains(110, 10));
        Assert.False(item.Contains(10, 30));
        Assert.False(item.Contains(9, 15));
    }

    [Fact]
    public void A_click_returns_the_id_under_the_pointer()
    {
        var m = Screen();
        Assert.Equal("c", m.PointerPressed(20, 80));
        Assert.Null(m.PointerPressed(500, 500));
    }

    [Fact]
    public void Arrow_keys_move_through_the_reachable_items_and_wrap_round()
    {
        var m = Screen();
        m.MoveFocus(1);
        Assert.Equal("a", m.FocusedItem!.Id);
        m.MoveFocus(1);
        Assert.Equal("b", m.FocusedItem!.Id);
        m.MoveFocus(1);
        Assert.Equal("c", m.FocusedItem!.Id);       // the value area is skipped
        m.MoveFocus(1);
        Assert.Equal("a", m.FocusedItem!.Id);
        m.MoveFocus(-1);
        Assert.Equal("c", m.FocusedItem!.Id);
        Assert.Equal("c", m.ActivateFocused());
    }

    [Fact]
    public void Going_back_with_nothing_focused_starts_from_the_last_item()
    {
        var m = Screen();
        m.MoveFocus(-1);
        Assert.Equal("c", m.FocusedItem!.Id);
    }

    [Fact]
    public void A_focus_can_be_set_by_id()
    {
        var m = Screen();
        m.FocusId("b");
        Assert.Equal("b", m.FocusedItem!.Id);
        m.FocusId("value");                          // not keyboard reachable
        Assert.Equal("b", m.FocusedItem!.Id);
    }
}

public class MenuLayoutTests
{
    [Theory]
    [InlineData(160 + 5, 99 + 5, "single")]       // the first item of the English main menu
    [InlineData(295 + 5, 392 + 5, "quit")]
    [InlineData(221 + 100, 267 + 20, "setup")]
    public void English_main_menu_items_are_where_the_original_has_them(int x, int y, string id)
    {
        var menu = MenuLayouts.Main(0, MenuFixtures.ImageSize);
        Assert.Equal(id, menu.PointerPressed(x, y));
    }

    [Fact]
    public void The_main_menu_has_eight_items_with_hover_pictures_in_both_languages()
    {
        foreach (int language in new[] { 0, 1 })
        {
            var menu = MenuLayouts.Main(language, MenuFixtures.ImageSize);
            Assert.Equal(["single", "multi", "demo", "scores", "setup", "options", "help", "quit"], menu.Items.Select(i => i.Id));
            Assert.All(menu.Items, i =>
            {
                Assert.NotNull(i.Hover);
                Assert.True(File.Exists(Legacy.Path($"images/{i.Hover}")), i.Hover);
                Assert.True(i.Width > 50 && i.Height > 20);
            });
        }
    }

    [Fact]
    public void The_french_main_menu_has_its_own_positions_and_an_overlay()
    {
        var menu = MenuLayouts.Main(1, MenuFixtures.ImageSize);
        Assert.Equal("single", menu.PointerPressed(223 + 10, 94 + 10));
        Assert.Null(MenuLayouts.MainOverlay(0));
        Assert.Equal(("debutof.png", 155, 93), MenuLayouts.MainOverlay(1));
    }

    [Fact]
    public void Every_picture_a_layout_names_exists()
    {
        foreach (int language in new[] { 0, 1 })
        {
            foreach (var layout in new[]
            {
                MenuLayouts.Setup(language, MenuFixtures.ImageSize),
                MenuLayouts.HighScores(language, true, MenuFixtures.ImageSize),
            })
                foreach (var item in layout.Items)
                    foreach (var file in new[] { item.Rest, item.Hover }.Where(f => f is not null))
                        Assert.True(File.Exists(Legacy.Path($"images/{file}")), file);
            Assert.True(File.Exists(Legacy.Path($"images/{MenuLayouts.SetupBackground(language)}")));
        }
        Assert.True(File.Exists(Legacy.Path($"images/{MenuLayouts.MainBackground}")));
        Assert.True(File.Exists(Legacy.Path($"images/{MenuLayouts.HighScoreBackground}")));
        Assert.True(File.Exists(Legacy.Path($"images/{MenuLayouts.TextScreenBackground}")));
        Assert.True(File.Exists(Legacy.Path("images/hscoretf.png")));
        Assert.True(File.Exists(Legacy.Path("images/debutof.png")));
    }

    [Fact]
    public void The_setup_screen_has_all_sixteen_labels_the_values_and_a_back_button()
    {
        var menu = MenuLayouts.Setup(0, MenuFixtures.ImageSize);
        foreach (var id in new[] { "player", "name", "password", "shadow", "smooth", "hspeed", "vspeed", "continuous",
                                   "key.left", "key.right", "key.down", "key.drop", "key.rotatecw", "key.rotateccw", "key.twice", "allkeys", "back" })
            Assert.Contains(menu.Items, i => i.Id == id);

        // A label and its value both lead to the same item.
        Assert.Equal("shadow", menu.PointerPressed(237 + 5, 161 + 5));
        Assert.Equal("shadow", menu.PointerPressed(333 + 5, 161 + 5));
        Assert.Equal("key.rotatecw", menu.PointerPressed(513 + 5, 350 + 5));
    }

    [Fact]
    public void Value_areas_stay_on_the_screen_and_clear_of_each_other()
    {
        var values = MenuLayouts.SetupValues;
        Assert.All(values.Values, v => Assert.True(v.X + v.Width <= 640));
        foreach (var a in values)
            foreach (var b in values.Where(b => b.Key != a.Key))
            {
                bool overlap = a.Value.X < b.Value.X + b.Value.Width && b.Value.X < a.Value.X + a.Value.Width
                    && a.Value.Y < b.Value.Y + 22 && b.Value.Y < a.Value.Y + 22;
                Assert.False(overlap, $"{a.Key} overlaps {b.Key}");
            }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_value_panel_never_covers_another_items_label(int language)
    {
        var menu = MenuLayouts.Setup(language, MenuFixtures.ImageSize);
        foreach (var (id, (x, y, w)) in MenuLayouts.SetupValues)
            foreach (var label in menu.Items.Where(i => i.Hover is not null && i.Id != id))
            {
                bool overlap = x < label.X + label.Width && label.X < x + w && y < label.Y + label.Height && label.Y < y + 22;
                Assert.False(overlap, $"value of {id} covers the label of {label.Id}");
            }
    }

    [Fact]
    public void The_high_score_screen_offers_play_again_only_after_a_game()
    {
        Assert.Equal(["back"], MenuLayouts.HighScores(0, false, MenuFixtures.ImageSize).Items.Select(i => i.Id));
        Assert.Equal(["again", "back"], MenuLayouts.HighScores(0, true, MenuFixtures.ImageSize).Items.Select(i => i.Id));
    }
}

public class KeyNamesTests
{
    [Theory]
    [InlineData("Left", "Left")]
    [InlineData("RightShift", "Right Shift")]
    [InlineData("LeftControl", "Left Control")]
    [InlineData("D5", "5")]
    [InlineData("NumPad7", "Num Pad 7")]
    [InlineData("OemComma", "Comma")]
    [InlineData("F12", "F 12")]
    [InlineData("", "?")]
    public void Names_are_made_readable(string name, string shown) => Assert.Equal(shown, KeyNames.Friendly(name));
}

public class SetupModelTests
{
    private static (SetupModel Model, GameSettings Settings) New(int language = 0)
    {
        var settings = GameSettings.CreateDefault();
        return (new SetupModel(settings, MenuFixtures.Text(language)), settings);
    }

    [Fact]
    public void Values_are_shown_with_the_original_words()
    {
        var (m, _) = New();
        Assert.Equal("1", m.ValueText("player"));
        Assert.Equal("#1", m.ValueText("name"));
        Assert.Equal("No", m.ValueText("shadow"));
        Assert.Equal("Yes", m.ValueText("smooth"));
        Assert.Equal("Fast", m.ValueText("hspeed"));
        Assert.Equal("Yes", m.ValueText("continuous"));
        Assert.Equal("Left", m.ValueText("key.left"));
        Assert.Equal("Right Shift", m.ValueText("key.twice"));
    }

    [Fact]
    public void Values_follow_the_language()
    {
        var (m, _) = New(1);
        Assert.Equal("Non", m.ValueText("shadow"));
        Assert.Equal("Oui", m.ValueText("smooth"));
        Assert.Equal("Rapide", m.ValueText("hspeed"));
    }

    [Fact]
    public void Clicking_a_yes_no_item_flips_it_and_a_speed_cycles_through_four_steps()
    {
        var (m, s) = New();
        m.Activate("shadow");
        Assert.True(s.CurrentPlayer.Shadow);
        m.Activate("shadow");
        Assert.False(s.CurrentPlayer.Shadow);

        Assert.Equal(2, s.CurrentPlayer.HorizontalSpeed);
        m.Activate("hspeed");
        Assert.Equal(3, s.CurrentPlayer.HorizontalSpeed);
        m.Activate("hspeed");
        Assert.Equal(0, s.CurrentPlayer.HorizontalSpeed);
        Assert.Equal("Slow", m.ValueText("hspeed"));
        m.Cycle("vspeed", -1);
        Assert.Equal(1, s.CurrentPlayer.VerticalSpeed);
        m.Cycle("vspeed", -1);
        m.Cycle("vspeed", -1);
        Assert.Equal(3, s.CurrentPlayer.VerticalSpeed);     // wraps below zero
    }

    [Fact]
    public void The_player_item_switches_between_the_three_profiles()
    {
        var (m, s) = New();
        m.Activate("player");
        Assert.Equal(1, s.SetupPlayer);
        Assert.Equal("#2", m.ValueText("name"));
        m.Activate("player");
        m.Activate("player");
        Assert.Equal(0, s.SetupPlayer);
        m.Cycle("player", -1);
        Assert.Equal(2, s.SetupPlayer);
    }

    [Fact]
    public void Typing_a_name_replaces_it_character_by_character_until_enter()
    {
        var (m, s) = New();
        m.Activate("name");
        Assert.Equal(SetupMode.EditingName, m.Mode);
        while (m.Backspace() && s.CurrentPlayer.Name.Length > 0) { }
        foreach (char c in "Hero")
            Assert.True(m.TypeCharacter(c));
        Assert.Equal("Hero_", m.ValueText("name"));       // the underscore is the cursor
        Assert.True(m.Commit());
        Assert.Equal("Hero", s.CurrentPlayer.Name);
        Assert.Equal(SetupMode.Browsing, m.Mode);
        Assert.False(m.TypeCharacter('x'));               // no longer editing
    }

    [Fact]
    public void A_name_stops_at_the_original_limit_and_control_characters_are_ignored()
    {
        var (m, s) = New();
        m.Activate("name");
        for (int i = 0; i < 80; i++)
            m.TypeCharacter('x');
        Assert.Equal(39, s.CurrentPlayer.Name.Length);
        Assert.False(m.TypeCharacter('\t'));
        Assert.False(m.TypeCharacter('\u0001'));
        Assert.False(m.TypeCharacter('Ő'));          // outside what the font can show
        m.Backspace();
        Assert.True(m.TypeCharacter('é'));
    }

    [Fact]
    public void A_password_is_shown_as_stars()
    {
        var (m, s) = New();
        m.Activate("password");
        foreach (char c in "secret")
            m.TypeCharacter(c);
        Assert.Equal("secret", s.CurrentPlayer.Password);
        Assert.Equal("******_", m.ValueText("password"));
    }

    [Fact]
    public void Clicking_a_key_waits_for_a_key_and_binds_it()
    {
        var (m, s) = New();
        m.Activate("key.drop");
        Assert.Equal(SetupMode.CapturingKey, m.Mode);
        Assert.Equal(GameAction.Drop, m.CaptureAction);
        Assert.Equal("Press a key...", m.Prompt);
        Assert.Equal("?", m.ValueText("key.drop"));

        Assert.True(m.KeyPressed("Enter"));                // refused: Enter confirms things
        Assert.Equal(SetupMode.CapturingKey, m.Mode);
        Assert.True(m.KeyPressed("LeftShift"));
        Assert.Equal("LeftShift", s.CurrentPlayer.Keys[GameAction.Drop]);
        Assert.Equal("Left Shift", m.ValueText("key.drop"));
        Assert.Equal(SetupMode.Browsing, m.Mode);
        Assert.Null(m.Prompt);
        Assert.False(m.KeyPressed("A"));
    }

    [Fact]
    public void Escape_leaves_a_key_binding_alone()
    {
        var (m, s) = New();
        m.Activate("key.left");
        Assert.True(m.Cancel());
        Assert.Equal("Left", s.CurrentPlayer.Keys[GameAction.Left]);
        Assert.False(m.Cancel());                          // nothing left to cancel
        m.Activate("key.left");
        m.KeyPressed("Escape");
        Assert.Equal("Left", s.CurrentPlayer.Keys[GameAction.Left]);
    }

    [Fact]
    public void Two_actions_may_share_a_key()
    {
        var (m, s) = New();
        Assert.Equal(s.CurrentPlayer.Keys[GameAction.RotateLeft], s.CurrentPlayer.Keys[GameAction.RotateRight]);
        m.Activate("key.left");
        m.KeyPressed("Down");
        Assert.Equal("Down", s.CurrentPlayer.Keys[GameAction.Left]);
        Assert.Equal("Down", s.CurrentPlayer.Keys[GameAction.Down]);
    }

    [Fact]
    public void Set_all_keys_asks_for_each_key_in_the_original_order_with_its_prompt()
    {
        var (m, s) = New();
        m.Activate("allkeys");
        var expected = new (GameAction, string)[]
        {
            (GameAction.Left, "Press a key for left..."),
            (GameAction.Right, "Right..."),
            (GameAction.Down, "Down..."),
            (GameAction.RotateRight, "Rotate clock-wise..."),
            (GameAction.RotateLeft, "Rotate counter clock-wise..."),
            (GameAction.RotateTwice, "Rotate twice..."),
            (GameAction.Drop, "Instant drop..."),
        };
        string[] keys = ["A", "D", "S", "E", "Q", "W", "X"];
        for (int i = 0; i < 7; i++)
        {
            Assert.Equal(SetupMode.CapturingKey, m.Mode);
            Assert.Equal(expected[i].Item1, m.CaptureAction);
            Assert.Equal(expected[i].Item2, m.Prompt);
            m.KeyPressed(keys[i]);
        }
        Assert.Equal(SetupMode.Browsing, m.Mode);
        Assert.Equal("A", s.CurrentPlayer.Keys[GameAction.Left]);
        Assert.Equal("D", s.CurrentPlayer.Keys[GameAction.Right]);
        Assert.Equal("S", s.CurrentPlayer.Keys[GameAction.Down]);
        Assert.Equal("E", s.CurrentPlayer.Keys[GameAction.RotateRight]);
        Assert.Equal("Q", s.CurrentPlayer.Keys[GameAction.RotateLeft]);
        Assert.Equal("W", s.CurrentPlayer.Keys[GameAction.RotateTwice]);
        Assert.Equal("X", s.CurrentPlayer.Keys[GameAction.Drop]);
    }

    [Fact]
    public void Escape_stops_set_all_keys_and_keeps_the_keys_already_set()
    {
        var (m, s) = New();
        m.Activate("allkeys");
        m.KeyPressed("A");
        m.KeyPressed("Escape");
        Assert.Equal(SetupMode.Browsing, m.Mode);
        Assert.Equal("A", s.CurrentPlayer.Keys[GameAction.Left]);
        Assert.Equal("Right", s.CurrentPlayer.Keys[GameAction.Right]);
    }

    [Fact]
    public void Clicking_something_else_ends_typing()
    {
        var (m, _) = New();
        m.Activate("name");
        m.Activate("shadow");
        Assert.Equal(SetupMode.Browsing, m.Mode);
    }
}

public class AppFlowTests
{
    [Fact]
    public void The_program_starts_on_the_main_menu()
    {
        var flow = MenuFixtures.NewFlow();
        Assert.Equal(Screen.Main, flow.Current);
        Assert.False(flow.QuitRequested);
    }

    [Fact]
    public void Main_menu_items_lead_to_their_screens()
    {
        var flow = MenuFixtures.NewFlow();
        flow.Activate("setup");
        Assert.Equal(Screen.Setup, flow.Current);
        flow.Activate("back");
        Assert.Equal(Screen.Main, flow.Current);

        flow.Activate("options");
        Assert.Equal(Screen.Options, flow.Current);
        flow.Activate("back");
        flow.Activate("scores");
        Assert.Equal(Screen.HighScores, flow.Current);
        flow.Activate("back");
        flow.Activate("single");
        Assert.Equal(Screen.Playing, flow.Current);
    }

    [Fact]
    public void Quit_and_escape_on_the_main_menu_end_the_program()
    {
        var a = MenuFixtures.NewFlow();
        a.Activate("quit");
        Assert.True(a.QuitRequested);

        var b = MenuFixtures.NewFlow();
        b.Back();
        Assert.True(b.QuitRequested);
    }

    [Fact]
    public void Items_that_are_not_built_yet_show_a_notice_in_the_chosen_language()
    {
        var en = MenuFixtures.NewFlow(0);
        en.Activate("multi");
        Assert.Equal("Not available yet", en.Notice);
        Assert.Equal(Screen.Main, en.Current);
        en.Activate("demo");
        Assert.NotNull(en.Notice);
        en.Activate("help");
        Assert.NotNull(en.Notice);
        en.Activate("scores");
        Assert.Null(en.Notice);

        var fr = MenuFixtures.NewFlow(1);
        fr.Activate("multi");
        Assert.Equal("Pas encore disponible", fr.Notice);
    }

    [Fact]
    public void Escape_goes_back_one_level_and_first_cancels_typing_or_capturing()
    {
        var flow = MenuFixtures.NewFlow();
        flow.Activate("setup");
        flow.Activate("name");
        Assert.Equal(SetupMode.EditingName, flow.Setup.Mode);
        flow.Back();
        Assert.Equal(Screen.Setup, flow.Current);          // only the typing ended
        Assert.Equal(SetupMode.Browsing, flow.Setup.Mode);
        flow.Back();
        Assert.Equal(Screen.Main, flow.Current);

        flow.Activate("options");
        flow.Back();
        Assert.Equal(Screen.Main, flow.Current);
        flow.Show(Screen.Playing);
        flow.Back();
        Assert.Equal(Screen.Main, flow.Current);
    }

    [Fact]
    public void Leaving_setup_saves_the_settings_and_repairs_an_empty_name()
    {
        int saved = 0;
        var flow = MenuFixtures.NewFlow(saveSettings: () => saved++);
        flow.Activate("setup");
        flow.Activate("name");
        while (flow.Setup.Backspace() && flow.Settings.CurrentPlayer.Name.Length > 0) { }
        Assert.Equal("", flow.Settings.CurrentPlayer.Name);
        flow.Activate("back");
        Assert.Equal(1, saved);
        Assert.Equal("#1", flow.Settings.CurrentPlayer.Name);
    }

    [Fact]
    public void Changing_the_language_switches_the_menus_and_the_texts_and_saves()
    {
        int saved = 0;
        var flow = MenuFixtures.NewFlow(saveSettings: () => saved++);
        Assert.Equal("Score:", flow.Text.Get(LegacyText.ScoreLabel));
        flow.Activate("options");
        flow.Activate("language");
        Assert.Equal(1, flow.Settings.Language);
        Assert.Equal(1, saved);
        Assert.Equal("Oui", flow.Text.Get(LegacyText.Yes));
        flow.Activate("back");
        Assert.Equal("single", flow.Menu.PointerPressed(223 + 10, 94 + 10));    // French positions now
        flow.Activate("options");
        flow.Activate("language");
        Assert.Equal(0, flow.Settings.Language);
    }

    [Fact]
    public void A_finished_game_that_qualifies_is_recorded_with_the_players_name_and_highlighted()
    {
        int saved = 0;
        var flow = MenuFixtures.NewFlow(saveScores: () => saved++);
        flow.Settings.CurrentPlayer.Name = "Hero";
        flow.Show(Screen.Playing);
        flow.GameFinished(5000, 40, 3);

        Assert.Equal(Screen.HighScores, flow.Current);
        Assert.Equal(0, flow.HighlightedRank);
        Assert.Equal(1, saved);
        var entry = Assert.Single(flow.Scores.Entries);
        Assert.Equal(("Hero", 5000, 40, 3), (entry.Name, entry.Score, entry.Lines, entry.Level));
        Assert.Equal(["again", "back"], flow.Menu.Items.Select(i => i.Id));

        flow.Activate("again");
        Assert.Equal(Screen.Playing, flow.Current);
    }

    [Fact]
    public void A_game_that_does_not_make_the_list_still_shows_it_but_saves_nothing()
    {
        int saved = 0;
        var flow = MenuFixtures.NewFlow(saveScores: () => saved++);
        flow.GameFinished(0, 0, 1);
        Assert.Equal(Screen.HighScores, flow.Current);
        Assert.Equal(-1, flow.HighlightedRank);
        Assert.Equal(0, saved);
        Assert.Empty(flow.Scores.Entries);

        flow.Activate("back");
        Assert.Equal(Screen.Main, flow.Current);
        flow.Activate("scores");
        Assert.Equal(["back"], flow.Menu.Items.Select(i => i.Id));     // no "play again" when browsing
    }
}

public class FontTranslationTests
{
    private static readonly Rgb White = new(255, 255, 255);

    private static LegacyFont Font() => new(Legacy.Bytes("fonts/font.fnt"), 2);

    [Fact]
    public void Accented_letters_have_glyphs_of_their_own_or_use_the_plain_letter()
    {
        var font = Font();
        foreach (char c in "éèêëàçôîùû")
        {
            var b = new PixelBuffer(40, 24);
            font.Draw(b, c.ToString(), 0, 0, White);
            Assert.Contains(b.Pixels, p => p != PixelBuffer.Transparent);
        }
        Assert.Equal(font.Measure("a"), font.Measure("â"));
        Assert.Equal(font.Measure("E"), font.Measure("È"));
        Assert.Equal(font.Measure("o"), font.Measure("ö"));
    }

    [Fact]
    public void Unknown_characters_and_spaces_take_the_width_of_an_i_and_draw_nothing()
    {
        var font = Font();
        int space = font.Measure(" ");
        Assert.Equal(font.Measure("i"), space);
        Assert.Equal(space, font.Measure("Ő"));
        var b = new PixelBuffer(40, 24);
        font.Draw(b, " Ő", 0, 0, White);
        Assert.All(b.Pixels, p => Assert.Equal(PixelBuffer.Transparent, p));
    }

    [Fact]
    public void The_special_symbol_in_the_back_button_text_is_one_glyph()
    {
        var font = Font();
        var english = MenuFixtures.Text(0).Get(LegacyText.Back);       // "Back ·0"
        Assert.EndsWith("·0", english);
        var b = new PixelBuffer(120, 24);
        font.Draw(b, "·0", 0, 0, White);
        Assert.Contains(b.Pixels, p => p != PixelBuffer.Transparent);
        // It is one wide icon, not an unknown character followed by a digit.
        Assert.True(font.Measure("·0") > font.Measure("·") + font.Measure("0"));
    }
}

public class MenuPainterTests
{
    private static (MenuPainter Painter, AppFlow Flow, Rgb[] Palette) Setup(string background, int language = 0)
    {
        var sprites = new Dictionary<string, PixelBuffer>();
        PixelBuffer? Lookup(string file)
        {
            if (!sprites.TryGetValue(file, out var sprite))
            {
                // Stand-in sprite: a solid block the size of the real picture.
                var (w, h) = MenuFixtures.ImageSize(file);
                sprite = new PixelBuffer(w, h);
                sprite.Clear(PixelBuffer.Pack(1, 2, 3));
                sprites[file] = sprite;
            }
            return sprite;
        }
        var palette = PngPalette.Read(Legacy.Bytes($"images/{background}"));
        var painter = new MenuPainter(new LegacyFont(Legacy.Bytes("fonts/font.fnt"), 2),
            new LegacyFont(Legacy.Bytes("fonts/courrier.fnt"), 1), Lookup);
        painter.SetPalette(palette);
        return (painter, MenuFixtures.NewFlow(language), palette);
    }

    private static bool AnyPixel(PixelBuffer b, int x0, int y0, int w, int h) =>
        Enumerable.Range(y0, h).Any(y => Enumerable.Range(x0, w).Any(x => b.Get(x, y) != PixelBuffer.Transparent));

    [Fact]
    public void The_main_menu_draws_nothing_until_an_item_is_hovered_then_its_picture()
    {
        var (painter, flow, _) = Setup("debuto.png");
        painter.Paint(flow);
        Assert.False(AnyPixel(painter.Overlay, 0, 0, 640, 480));

        flow.Menu.PointerMoved(200, 110);                       // over "single"
        painter.Paint(flow);
        Assert.Equal(PixelBuffer.Pack(1, 2, 3), painter.Overlay.Get(160 + 5, 99 + 5));
        Assert.False(AnyPixel(painter.Overlay, 0, 300, 150, 100));
    }

    [Fact]
    public void A_notice_is_shown_near_the_bottom_of_the_main_menu()
    {
        var (painter, flow, _) = Setup("debuto.png");
        flow.Activate("multi");
        painter.Paint(flow);
        Assert.True(AnyPixel(painter.Overlay, 200, 450, 240, 24));
    }

    [Fact]
    public void The_setup_screen_shows_every_value_in_a_panel_with_the_palettes_colours()
    {
        var (painter, flow, palette) = Setup("setup.png");
        flow.Show(Screen.Setup);
        painter.Paint(flow);
        var o = painter.Overlay;

        foreach (var (id, (x, y, w)) in MenuLayouts.SetupValues)
        {
            Assert.Equal(PixelBuffer.Pack(palette[210]), o.Get(x, y));                 // frame
            Assert.Equal(PixelBuffer.Pack(palette[210]), o.Get(x + w - 1, y + 10));
            Assert.True(AnyPixel(o, x + 2, y + 2, w - 4, 16), $"text of {id}");
        }
    }

    [Fact]
    public void A_focused_value_has_a_bright_inner_line()
    {
        var (painter, flow, palette) = Setup("setup.png");
        flow.Show(Screen.Setup);
        flow.Menu.FocusId("shadow");
        painter.Paint(flow);
        var (x, y, _) = MenuLayouts.SetupValues["shadow"];
        Assert.Equal(PixelBuffer.Pack(palette[255]), painter.Overlay.Get(x + 10, y + 1));
        var (nx, ny, _) = MenuLayouts.SetupValues["smooth"];
        Assert.NotEqual(PixelBuffer.Pack(palette[255]), painter.Overlay.Get(nx + 10, ny + 1));
    }

    [Fact]
    public void While_capturing_a_key_the_prompt_is_shown()
    {
        var (painter, flow, _) = Setup("setup.png");
        flow.Show(Screen.Setup);
        painter.Paint(flow);
        Assert.False(AnyPixel(painter.Overlay, MenuLayouts.SetupPromptX + 2, MenuLayouts.SetupPromptY + 2, 270, 16));

        flow.Activate("allkeys");
        painter.Paint(flow);
        Assert.True(AnyPixel(painter.Overlay, MenuLayouts.SetupPromptX + 2, MenuLayouts.SetupPromptY + 2, 270, 16));
    }

    [Fact]
    public void The_cursor_blinks_in_a_text_field()
    {
        var (painter, flow, _) = Setup("setup.png");
        flow.Show(Screen.Setup);
        flow.Activate("name");
        var (x, y, w) = MenuLayouts.SetupValues["name"];

        int Lit(bool caret)
        {
            painter.Paint(flow, caret);
            return Enumerable.Range(0, w - 4).Sum(i => Enumerable.Range(0, 16).Count(j =>
                painter.Overlay.Get(x + 2 + i, y + 2 + j) != PixelBuffer.Transparent
                && painter.Overlay.Get(x + 2 + i, y + 2 + j) != PixelBuffer.Pack(PngPalette.Read(Legacy.Bytes("images/setup.png"))[0])));
        }
        Assert.True(Lit(true) > Lit(false));
    }

    [Fact]
    public void The_options_screen_shows_the_title_the_language_and_the_back_button()
    {
        var (painter, flow, _) = Setup("multi.png");
        flow.Show(Screen.Options);
        painter.Paint(flow);
        Assert.True(AnyPixel(painter.Overlay, 200, 20, 240, 20));            // "Game options"
        Assert.True(AnyPixel(painter.Overlay, 40, 100, 300, 20));            // the label
        Assert.True(AnyPixel(painter.Overlay, MenuLayouts.OptionsLanguageX, MenuLayouts.OptionsLanguageY, 160, 20));
        Assert.True(AnyPixel(painter.Overlay, MenuLayouts.BackButtonX, MenuLayouts.BackButtonY, 80, 20));
    }

    [Fact]
    public void The_high_score_table_lists_the_entries_with_the_new_one_in_yellow()
    {
        var (painter, flow, _) = Setup("hscore.png");
        flow.Scores.Add(new HighScore("Zed", 900, 9, 1, DateTime.UtcNow));
        flow.GameFinished(1500, 15, 2);       // becomes the best
        painter.Paint(flow);
        var o = painter.Overlay;

        // Row 0 is the new entry (yellow: no blue in the text), row 1 the older one (white).
        uint YellowPixelCount(int row) => (uint)Enumerable.Range(0, 60).Sum(i => Enumerable.Range(0, 18).Count(j =>
        {
            uint p = o.Get(100 + i, 276 + 21 * row + j);
            return p != PixelBuffer.Transparent && ((p >> 16) & 0xff) == 0 && (p & 0xff) > 100;
        }));
        Assert.True(YellowPixelCount(0) > 0);
        Assert.Equal(0u, YellowPixelCount(1));
        Assert.True(AnyPixel(o, 100, 276 + 21, 60, 18));                      // the second row has a name
        Assert.True(AnyPixel(o, 260, 276, 80, 20));                           // a score
        Assert.True(AnyPixel(o, 485, 410, 100, 40));                          // the back button picture
        Assert.True(AnyPixel(o, 38, 410, 100, 40));                           // the play again picture
        Assert.False(AnyPixel(o, 100, 276 + 21 * 2, 450, 18));                // no third row
    }
}

public class SettingsInGameTests
{
    private static readonly KeyBindings Bindings = new(1, 2, 3, 4, 5, 6, 7);

    [Fact]
    public void Without_the_shadow_setting_no_landing_shadow_is_drawn()
    {
        var palette = Legacy.Palette(1);
        var view = new SinglePlayerView(new LegacyFont(Legacy.Bytes("fonts/font.fnt"), 2), new LegacyFont(Legacy.Bytes("fonts/courrier.fnt"), 1),
            MenuFixtures.Text(0));
        view.SetPalette(palette);
        var colors = new BlockColors(palette);
        var game = new SinglePlayerGame(3, new KeyStates(), Bindings);

        bool HasShadow()
        {
            view.Paint(game);
            return view.Overlay.Pixels.Contains(colors.Shade(BlockColors.ShadowColor, 4));
        }
        view.ShowShadow = true;
        Assert.True(HasShadow());
        view.ShowShadow = false;
        Assert.False(HasShadow());
    }

    [Fact]
    public void Without_smooth_movement_the_piece_is_drawn_on_the_cell_grid()
    {
        var palette = Legacy.Palette(1);
        var view = new SinglePlayerView(new LegacyFont(Legacy.Bytes("fonts/font.fnt"), 2), new LegacyFont(Legacy.Bytes("fonts/courrier.fnt"), 1),
            MenuFixtures.Text(0));
        view.SetPalette(palette);
        view.ShowShadow = false;
        var game = new SinglePlayerGame(3, new KeyStates(), Bindings);
        for (int n = 0; n < 700; n++)
            game.Update();                                 // let the piece fall into view

        view.SmoothMovement = false;
        view.Paint(game);
        var p = game.Player.Falling;
        var colors = new BlockColors(palette);
        // The top-left cell of the piece's box sits exactly at the grid position of (column, row).
        int gridX = SinglePlayerView.BoardX + (p.Column - 4) * 18, gridY = SinglePlayerView.BoardY + (p.Row - 12) * 18;
        int j = Enumerable.Range(0, 4).First(r => Enumerable.Range(0, 4).Any(i => Pieces.Shapes[p.Piece, p.Rotation, r, i] != 0));
        int i = Enumerable.Range(0, 4).First(c => Pieces.Shapes[p.Piece, p.Rotation, j, c] != 0);
        Assert.Equal(colors.Shade(p.Piece, 4), view.Overlay.Get(gridX + i * 18 + 9, gridY + j * 18 + 9));
    }

    [Fact]
    public void Continuous_down_off_makes_a_held_down_key_stop_at_the_next_piece()
    {
        var keys = new KeyStates();
        var game = new SinglePlayerGame(3, keys, Bindings, continuousDown: false);
        keys.Down(4);                                      // hold down
        while (game.Phase == GamePhase.Falling)
            game.Update();
        while (game.Phase == GamePhase.Settling)
            game.Update();
        Assert.Equal(MoveFlags.None, game.Player.Frame().Moves);    // the new piece ignores the held key
    }
}
