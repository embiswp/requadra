using Quadra.Core.Assets;
using Quadra.Core.Game;
using Quadra.Core.Input;
using Quadra.Core.Rendering;
using Quadra.Core.Rules;

namespace Quadra.Tests;

internal static class Legacy
{
    public static string Path(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = System.IO.Path.Combine(dir.FullName, "legacy", relative);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(relative);
    }

    public static byte[] Bytes(string relative) => File.ReadAllBytes(Path(relative));

    public static Rgb[] Palette(int level) => PngPalette.Read(Bytes($"images/fond{(level - 1) % 10}.png"));

    public static uint Pack(Rgb c) => PixelBuffer.Pack(c);
}

public class PngPaletteTests
{
    [Fact]
    public void Every_background_has_a_full_256_colour_palette()
    {
        for (int level = 1; level <= 10; level++)
            Assert.Equal(256, Legacy.Palette(level).Length);
    }

    [Fact]
    public void Block_colours_sit_at_entry_184_and_up_with_eight_shades_each()
    {
        var palette = Legacy.Palette(1);
        Assert.Equal(new Rgb(0xff, 0x53, 0x1b), palette[184 + 4]);   // colour 0 (orange), main shade
        Assert.Equal(new Rgb(0xff, 0x00, 0x00), palette[200 + 7]);   // colour 2 (red), lightest
        Assert.Equal(new Rgb(0x00, 0x00, 0x8f), palette[232 + 4]);   // colour 6 (blue), main shade
    }

    [Fact]
    public void Block_colours_change_with_the_level()
    {
        var level1 = new BlockColors(Legacy.Palette(1));
        var level6 = new BlockColors(Legacy.Palette(6));
        Assert.NotEqual(level1.Shade(0, 4), level6.Shade(0, 4));
    }

    [Fact]
    public void The_shadow_uses_the_ramp_after_the_pieces_and_colour_7_the_one_after_that()
    {
        // The original swaps colours 7 and 8, so the landing shadow (8) reads entries 240+.
        var palette = Legacy.Palette(1);
        var colors = new BlockColors(palette);
        Assert.Equal(Legacy.Pack(palette[240 + 4]), colors.Shade(BlockColors.ShadowColor, 4));
        Assert.Equal(Legacy.Pack(palette[248 + 4]), colors.Shade(7, 4));
    }

    [Fact]
    public void Rejects_files_that_are_not_pngs()
    {
        Assert.Throws<InvalidDataException>(() => PngPalette.Read([1, 2, 3, 4, 5, 6, 7, 8, 9]));
    }
}

public class BlockPainterTests
{
    private static (PixelBuffer Buffer, BlockColors Colors) Setup() =>
        (new PixelBuffer(18, 18), new BlockColors(Legacy.Palette(1)));

    [Fact]
    public void A_cell_joined_on_all_sides_is_flat_main_colour()
    {
        var (b, c) = Setup();
        BlockPainter.DrawPlain(b, 0, 0, 0, 2, c);
        Assert.All(b.Pixels, p => Assert.Equal(c.Shade(2, 4), p));
    }

    [Fact]
    public void A_cell_with_all_four_edges_is_bevelled_light_top_left_and_dark_bottom_right()
    {
        var (b, c) = Setup();
        BlockPainter.DrawPlain(b, 0, 0, 15, 2, c);
        Assert.Equal(c.Shade(2, 7), b.Get(0, 0));     // left and top edge
        Assert.Equal(c.Shade(2, 7), b.Get(10, 0));
        Assert.Equal(c.Shade(2, 6), b.Get(10, 1));
        Assert.Equal(c.Shade(2, 5), b.Get(10, 2));
        Assert.Equal(c.Shade(2, 4), b.Get(9, 9));     // the fill
        Assert.Equal(c.Shade(2, 1), b.Get(17, 9));    // right edge
        Assert.Equal(c.Shade(2, 1), b.Get(9, 17));    // bottom edge
        Assert.Equal(c.Shade(2, 3), b.Get(15, 9));
    }

    [Fact]
    public void Only_the_sides_with_edges_get_a_bevel()
    {
        var (b, c) = Setup();
        BlockPainter.DrawPlain(b, 0, 0, 1, 0, c);     // left edge only
        Assert.Equal(c.Shade(0, 7), b.Get(0, 9));
        Assert.Equal(c.Shade(0, 5), b.Get(2, 9));
        Assert.Equal(c.Shade(0, 4), b.Get(3, 9));
        Assert.Equal(c.Shade(0, 4), b.Get(9, 0));     // no top edge
    }

    [Fact]
    public void Inner_corners_are_drawn_where_two_neighbours_meet()
    {
        var (b, c) = Setup();
        // joined on every side, but the left neighbour has a top edge and the one above a left edge
        BlockPainter.DrawCell(b, 0, 0, 0, 3, c, [2, 1, 0, 0]);
        Assert.Equal(c.Shade(3, 7), b.Get(0, 0));
        Assert.Equal(c.Shade(3, 6), b.Get(1, 0));
        Assert.Equal(c.Shade(3, 5), b.Get(2, 0));
        Assert.Equal(c.Shade(3, 4), b.Get(3, 3));
    }

    [Fact]
    public void Small_cells_use_one_pixel_edges()
    {
        var b = new PixelBuffer(6, 6);
        var c = new BlockColors(Legacy.Palette(1));
        BlockPainter.DrawSmall(b, 0, 0, 3, 5, c);     // left and top edges
        Assert.Equal(c.Shade(5, 7), b.Get(0, 3));
        Assert.Equal(c.Shade(5, 7), b.Get(3, 0));
        Assert.Equal(c.Shade(5, 4), b.Get(3, 3));
    }

    [Fact]
    public void Drawing_is_clipped_to_the_canvas()
    {
        var b = new PixelBuffer(10, 10);
        var c = new BlockColors(Legacy.Palette(1));
        BlockPainter.DrawPlain(b, -9, -9, 15, 0, c);  // mostly outside
        BlockPainter.DrawPlain(b, 5, 5, 0, 0, c);     // partly outside
        Assert.Equal(c.Shade(0, 4), b.Get(9, 9));
    }
}

public class LegacyFontTests
{
    private static readonly Rgb White = new(255, 255, 255);

    [Fact]
    public void The_text_font_has_every_printable_character()
    {
        var font = new LegacyFont(Legacy.Bytes("fonts/font.fnt"), 2);
        Assert.Equal(109, font.GlyphCount);
        Assert.Equal(18, font.Height);
        Assert.True(font.Measure("Score") > 40);
    }

    [Fact]
    public void The_number_font_has_digits_and_a_colon()
    {
        var font = new LegacyFont(Legacy.Bytes("fonts/courrier.fnt"), 1);
        Assert.Equal(11, font.GlyphCount);
        Assert.Equal(11, font.Measure("0"));            // advance 10 plus the overlap
        Assert.Equal(10 * 4 + 1, font.Measure("1234"));
    }

    [Fact]
    public void Glyph_brightness_scales_the_colour_and_zero_is_transparent()
    {
        var font = new LegacyFont(Legacy.Bytes("fonts/font.fnt"), 2);
        var b = new PixelBuffer(30, 20);
        font.Draw(b, "A", 0, 0, White);
        // Row 0 of the 'A' glyph is "...11111111...": dim outline pixels from column 3.
        Assert.Equal(PixelBuffer.Transparent, b.Get(0, 0));
        Assert.Equal(PixelBuffer.Pack(36, 36, 36), b.Get(3, 0));    // 255 * 1 / 7
        // ... and the row below has brighter pixels (value 7 = full colour) further in.
        Assert.Contains(Enumerable.Range(0, 14), x => b.Get(x, 2) == PixelBuffer.Pack(255, 255, 255));
    }

    [Fact]
    public void Colour_ramps_between_black_and_the_chosen_colour()
    {
        var font = new LegacyFont(Legacy.Bytes("fonts/font.fnt"), 2);
        var b = new PixelBuffer(30, 20);
        font.Draw(b, "A", 0, 0, new Rgb(0, 140, 70));
        Assert.Equal(PixelBuffer.Pack(0, 20, 10), b.Get(3, 0));      // 140/7 and 70/7
    }

    [Fact]
    public void Rejects_data_that_is_not_a_font()
    {
        Assert.Throws<InvalidDataException>(() => new LegacyFont([1, 0, 0, 0, 5, 0, 0, 0], 2));
    }
}

public class SoundCueTests
{
    private static SoundCues Cues(int seed = 1) =>
        new(new Random(seed), file => WavInfo.SampleRate(Legacy.Bytes($"sons/{file}")));

    [Theory]
    [InlineData("pwap2.wav", 22050)]
    [InlineData("knock.wav", 11025)]
    [InlineData("clang3.wav", 11025)]
    public void Sample_rates_are_read_from_the_wav_header(string file, int rate) =>
        Assert.Equal(rate, WavInfo.SampleRate(Legacy.Bytes($"sons/{file}")));

    [Fact]
    public void Menu_sounds_use_the_original_samples_quietly_and_sit_in_the_middle()
    {
        var cues = Cues();
        var hover = cues.MenuHover();
        var click = cues.MenuClick();
        var value = cues.MenuValue();
        Assert.Equal(("click_1.wav", "blip1.wav", "tapdrip.wav"), (hover.File, click.File, value.File));
        Assert.Equal((4096 - 2000) / 4096f, hover.Volume, 4);
        Assert.Equal((4096 - 1000) / 4096f, click.Volume, 4);
        Assert.Equal((4096 - 800) / 4096f, value.Volume, 4);
        Assert.All(new[] { hover, click, value }, c => Assert.Equal(0f, c.Pan));
        Assert.InRange(hover.Pitch, -0.01f, 0.13f);         // 22000..24047 Hz against a 22050 Hz sample
        Assert.InRange(click.Pitch, 0.34f, 0.40f);           // 14000..14511 Hz against an 11025 Hz sample
    }

    [Fact]
    public void Every_sound_the_game_can_play_exists()
    {
        for (int level = 1; level <= 10; level++)
        {
            var set = SoundCues.ForLevel(level);
            foreach (var file in new[] { set.Flash, set.DropLoud, set.DropMiddle, set.DropSoft, set.Rotate })
                Assert.True(File.Exists(Legacy.Path($"sons/{file}")), file);
        }
        foreach (var file in new[] { SoundCues.FallFile, SoundCues.LevelUpFile, SoundCues.PauseFile })
            Assert.True(File.Exists(Legacy.Path($"sons/{file}")), file);
    }

    [Fact]
    public void Levels_cycle_through_ten_sound_sets()
    {
        Assert.Equal("hitwood1.wav", SoundCues.ForLevel(1).DropLoud);
        Assert.Equal("knock.wav", SoundCues.ForLevel(2).DropLoud);
        Assert.Equal("glass01.wav", SoundCues.ForLevel(7).DropLoud);
        Assert.Equal(SoundCues.ForLevel(1), SoundCues.ForLevel(11));
        Assert.Equal(SoundCues.ForLevel(5), SoundCues.ForLevel(25));
    }

    [Fact]
    public void Rotation_sounds_come_from_the_side_the_piece_is_on_and_are_nearly_full_volume()
    {
        var cues = Cues();
        var set = SoundCues.ForLevel(1);
        for (int n = 0; n < 50; n++)
        {
            var right = cues.Rotate(set, 13);
            var left = cues.Rotate(set, 4);
            Assert.Equal(set.Rotate, right.File);
            Assert.InRange(right.Volume, (4096 - 200 - 127) / 4096f - 1e-5f, (4096 - 200) / 4096f + 1e-5f);
            Assert.Equal(4 * 300 / 4096f, right.Pan, 4);
            Assert.Equal(-5 * 300 / 4096f, left.Pan, 4);
            Assert.InRange(right.Pitch, -0.071f, 0.064f);     // 10500..11523 Hz against an 11025 Hz sample
        }
    }

    [Fact]
    public void A_sample_recorded_at_22050_Hz_is_played_an_octave_down_as_in_the_original()
    {
        // pwap2 (flash sound of level 1) is a 22050 Hz sample that the game plays at about 11000 Hz.
        var cue = Cues().LineFlash(SoundCues.ForLevel(1), 1);
        Assert.Equal("pwap2.wav", cue.File);
        Assert.Equal(-1f, cue.Pitch);   // clamped: log2(10744/22050) is just under -1
    }

    [Fact]
    public void Longer_combos_sound_lower()
    {
        var set = SoundCues.ForLevel(3);   // blip1.wav is an 11025 Hz sample
        float one = Cues(5).LineFlash(set, 1).Pitch;
        float four = Cues(5).LineFlash(set, 4).Pitch;
        Assert.True(four < one);
    }

    [Fact]
    public void Falling_cells_get_louder_with_more_steps_up_to_full_volume()
    {
        var cues = Cues();
        float one = cues.Fall(1, 1).Volume;
        float five = cues.Fall(5, 1).Volume;
        float many = cues.Fall(20, 1).Volume;
        Assert.True(one < five);
        Assert.Equal(1f, many);
        Assert.Equal((4096 - 450) / 4096f, one, 4);
    }

    [Fact]
    public void Stamp_sound_is_one_of_the_three_drop_samples()
    {
        var set = SoundCues.ForLevel(9);
        var seen = new HashSet<string>();
        var cues = Cues();
        for (int n = 0; n < 100; n++)
            seen.Add(cues.Stamp(set, 7).File);
        Assert.Equal(new[] { "explod03.wav", "explod05.wav", "explod06.wav" }.Order(), seen.Order());
    }
}

public class SinglePlayerViewTests
{
    private static readonly KeyBindings Bindings = new(1, 2, 3, 4, 5, 6, 7);
    private const int Drop = 7;

    private static SinglePlayerView NewView() =>
        new(new LegacyFont(Legacy.Bytes("fonts/font.fnt"), 2), new LegacyFont(Legacy.Bytes("fonts/courrier.fnt"), 1),
            new LegacyText(Legacy.Bytes("textes/anglais.txt")));

    private static (SinglePlayerGame Game, SinglePlayerView View, BlockColors Colors) NewGame(int seed)
    {
        var palette = Legacy.Palette(1);
        var view = NewView();
        view.SetPalette(palette);
        return (new SinglePlayerGame(seed, new KeyStates(), Bindings), view, new BlockColors(palette));
    }

    private static bool AnyPixel(PixelBuffer b, int x0, int y0, int w, int h) =>
        Enumerable.Range(y0, h).Any(y => Enumerable.Range(x0, w).Any(x => b.Get(x, y) != PixelBuffer.Transparent));

    [Fact]
    public void A_new_game_shows_the_next_pieces_and_the_scores_and_an_empty_board()
    {
        var (game, view, _) = NewGame(3);
        view.Paint(game);
        var o = view.Overlay;

        Assert.False(AnyPixel(o, SinglePlayerView.BoardX, SinglePlayerView.BoardY + 100, 180, 200));   // nothing on the empty board
        Assert.True(AnyPixel(o, SinglePlayerView.BoardX + 60, 0, 72, 36));       // the next piece
        Assert.True(AnyPixel(o, SinglePlayerView.BoardX + 32, 0, 24, 14));
        Assert.True(AnyPixel(o, SinglePlayerView.BoardX + 5, 0, 24, 14));
        Assert.True(AnyPixel(o, SinglePlayerView.BoardX - 5, 403, 50, 18));      // "Score"
        Assert.True(AnyPixel(o, SinglePlayerView.BoardX + 55, 403, 30, 16));     // its value, 0
    }

    [Fact]
    public void The_landing_shadow_is_drawn_in_the_shadow_colour_at_the_bottom()
    {
        var (game, view, colors) = NewGame(3);
        view.Paint(game);
        var piece = game.Player.Falling;
        // Pick a cell of the shadow: the lowest filled cell of the piece, resting on the floor row 31.
        int rest = piece.RestingRow();
        int low = Enumerable.Range(0, 4).Last(j => Enumerable.Range(0, 4).Any(i => Pieces.Shapes[piece.Piece, 0, j, i] != 0));
        int col = Enumerable.Range(0, 4).First(i => Pieces.Shapes[piece.Piece, 0, low, i] != 0);
        int x = SinglePlayerView.BoardX + (piece.Column + col - Board.FirstColumn) * 18 + 9;
        int y = SinglePlayerView.BoardY + (rest + low - Board.FirstClearableRow) * 18 + 9;
        Assert.Equal(31, rest + low);
        Assert.Equal(colors.Shade(BlockColors.ShadowColor, 4), view.Overlay.Get(x, y));
    }

    [Fact]
    public void A_landed_piece_is_drawn_in_its_own_colour()
    {
        var (game, view, colors) = NewGame(3);
        int piece = game.Player.CurrentPiece;
        game.Keys.Down(Drop);
        while (game.Phase == GamePhase.Falling)
            game.Update();
        view.Paint(game);

        // Find a stamped cell and check the middle of it.
        var board = game.Player.Board;
        var cell = Enumerable.Range(12, 20)
            .SelectMany(r => Enumerable.Range(4, 10).Select(c => (Row: r, Col: c)))
            .First(p => board.IsOccupied(p.Row, p.Col));
        int x = SinglePlayerView.BoardX + (cell.Col - 4) * 18 + 9;
        int y = SinglePlayerView.BoardY + (cell.Row - 12) * 18 + 9;
        Assert.Equal(colors.Shade(piece, 4), view.Overlay.Get(x, y));
    }

    [Fact]
    public void A_cleared_row_flashes_white_then_red_and_the_timeline_has_the_original_length()
    {
        // Prepare the bottom row so that the first piece, dropped straight down, completes it.
        var palette = Legacy.Palette(1);
        for (int seed = 0; seed < 200; seed++)
        {
            var keys = new KeyStates();
            var game = new SinglePlayerGame(seed, keys, Bindings);
            int piece = game.Player.CurrentPiece;
            var bottom = Enumerable.Range(0, 4)
                .Where(i => Pieces.Shapes[piece, 0, LowestRow(piece), i] != 0)
                .Select(i => FallingPiece.SpawnColumn + i).ToHashSet();
            for (int x = Board.FirstColumn; x < Board.EndColumn; x++)
                if (!bottom.Contains(x))
                    game.Player.Board.Fill(31, x);

            keys.Down(Drop);
            while (game.Phase == GamePhase.Falling)
                game.Update();
            if (game.Player.LastClear.Count == 0)
                continue;

            var view = NewView();
            view.SetPalette(palette);
            var pass = game.Player.LastClear[0];
            Assert.Contains(31, pass.Rows);

            // The first frame of the animation is on screen right after the landing: white.
            Assert.Contains(game.Events, e => e.Kind == GameEventKind.LineFlash);
            var first = game.Animation!;
            Assert.Equal(SinglePlayerGame.FlashWhite, first.FlashColor);
            view.Paint(game);
            Assert.Equal(PixelBuffer.Pack(palette[255]), view.Overlay.Get(SinglePlayerView.BoardX + 5, SinglePlayerView.BoardY + (31 - 12) * 18 + 5));

            // The colour alternates every two frames: white, white, red, red, ...
            game.Update();
            Assert.Equal(SinglePlayerGame.FlashWhite, game.Animation!.FlashColor);
            game.Update();
            Assert.Equal(SinglePlayerGame.FlashRed, game.Animation!.FlashColor);
            view.Paint(game);
            Assert.Equal(PixelBuffer.Pack(palette[200]), view.Overlay.Get(SinglePlayerView.BoardX + 5, SinglePlayerView.BoardY + (31 - 12) * 18 + 5));

            // The whole animation lasts 16 flash frames + 2 + 2 per fall step + 2 + 1 line-check frame.
            game.Events.Clear();
            int shown = 2;
            while (game.Animation is not null)
            {
                shown++;
                game.Update();
            }
            Assert.Equal(16 + 2 + 2 * pass.FallSteps.Count + 2 + 1, shown);
            Assert.Equal(pass.FallSteps.Count > 0, game.Events.Any(e => e.Kind == GameEventKind.BlocksFell));
            return;
        }
        Assert.Fail("no seed produced a clearable first piece");
    }

    private static int LowestRow(int piece) =>
        Enumerable.Range(0, 4).Last(j => Enumerable.Range(0, 4).Any(i => Pieces.Shapes[piece, 0, j, i] != 0));

    [Fact]
    public void Painting_needs_a_palette()
    {
        var view = NewView();
        var game = new SinglePlayerGame(1, new KeyStates(), Bindings);
        Assert.Throws<InvalidOperationException>(() => view.Paint(game));
    }
}

public class LegacyTextTests
{
    private static LegacyText Text() => new(Legacy.Bytes("textes/anglais.txt"));

    [Fact]
    public void Strings_are_found_by_the_original_numbers()
    {
        var text = Text();
        Assert.Equal("Score:", text.Get(LegacyText.ScoreLabel));
        Assert.Equal("Lines:", text.Get(LegacyText.LinesLabel));
        Assert.Equal("Level:", text.Get(LegacyText.LevelLabel));
        Assert.Equal("Single", text.Get(LegacyText.FirstClearName));
        Assert.Equal("Quad", text.Get(LegacyText.FirstClearName + 3));
        Assert.Equal("14-lines", text.Get(LegacyText.FirstClearName + 13));
        Assert.Equal("More!", text.Get(LegacyText.MoreClears));
        Assert.Equal("Total:", text.Get(LegacyText.Total));
    }

    [Fact]
    public void The_french_table_has_the_same_strings_in_the_same_places()
    {
        var french = new LegacyText(Legacy.Bytes("textes/francais.txt"));
        Assert.Equal("Simple", french.Get(LegacyText.FirstClearName));
        Assert.Throws<ArgumentOutOfRangeException>(() => Text().Get(0));
    }
}

public class StatisticsTests
{
    private static PlayerSim SimStartingWith(int piece)
    {
        for (int seed = 0; seed < 500; seed++)
        {
            var sim = new PlayerSim(seed, 1);
            if (sim.CurrentPiece == piece)
                return sim;
        }
        throw new InvalidOperationException();
    }

    [Fact]
    public void Placed_pieces_are_counted_by_kind()
    {
        var sim = SimStartingWith(3);
        sim.Stamp(7, 29, 0, 0);
        int next = sim.CurrentPiece;
        sim.Stamp(4, 28, 0, 0);
        Assert.Equal(2, sim.PieceCounts.Sum());
        Assert.Equal(next == 3 ? 2 : 1, sim.PieceCounts[3]);
        Assert.Equal(1, sim.PieceCounts[next] - (next == 3 ? 1 : 0));
    }

    [Fact]
    public void A_four_line_clear_is_counted_as_a_quad()
    {
        var sim = SimStartingWith(5);              // the straight piece
        for (int row = 28; row <= 31; row++)
            for (int x = 4; x < 14; x++)
                if (x != 5)
                    sim.Board.Fill(row, x);
        var result = sim.Stamp(4, 28, 1, 0);       // upright, in column 5

        Assert.Equal(4, result.Lines);
        Assert.Equal(1, sim.ClearCounts[3]);
        Assert.Equal(1, sim.ClearCounts.Sum());
        Assert.Equal(4, sim.Lines);
    }
}

public class SidePanelTests
{
    private static readonly KeyBindings Bindings = new(1, 2, 3, 4, 5, 6, 7);

    private static (SinglePlayerGame Game, SinglePlayerView View, Rgb[] Palette, BlockColors Colors) Setup()
    {
        var palette = Legacy.Palette(1);
        var view = new SinglePlayerView(new LegacyFont(Legacy.Bytes("fonts/font.fnt"), 2),
            new LegacyFont(Legacy.Bytes("fonts/courrier.fnt"), 1), new LegacyText(Legacy.Bytes("textes/anglais.txt")));
        view.SetPalette(palette);
        return (new SinglePlayerGame(3, new KeyStates(), Bindings), view, palette, new BlockColors(palette));
    }

    private static bool AnyPixel(PixelBuffer b, int x0, int y0, int w, int h) =>
        Enumerable.Range(y0, h).Any(y => Enumerable.Range(x0, w).Any(x => b.Get(x, y) != PixelBuffer.Transparent));

    [Fact]
    public void Number_fields_have_a_palette_frame_a_dark_inside_and_right_aligned_digits()
    {
        var (game, view, palette, _) = Setup();
        view.Paint(game);
        var o = view.Overlay;

        // The score field: x 278, y 403, 120 wide, 20 high.
        Assert.Equal(PixelBuffer.Pack(palette[210]), o.Get(278, 403));
        Assert.Equal(PixelBuffer.Pack(palette[210]), o.Get(278 + 119, 403 + 10));
        Assert.Equal(PixelBuffer.Pack(palette[0]), o.Get(280, 405));
        Assert.False(AnyPixel(o, 280, 405, 90, 14) && !AnyPixel(o, 280 + 90, 405, 28, 14));   // digits sit at the right end
        Assert.True(AnyPixel(o, 278 + 120 - 20, 405, 18, 14));                                 // the single "0"
    }

    [Fact]
    public void The_labels_use_the_original_text_with_colons()
    {
        var (game, view, _, _) = Setup();
        view.Paint(game);
        Assert.True(AnyPixel(view.Overlay, 218, 403, 55, 18));
        Assert.True(AnyPixel(view.Overlay, 218, 460, 55, 18));
    }

    [Fact]
    public void The_left_frame_lists_the_seven_pieces_with_counts_and_a_total()
    {
        var (game, view, _, colors) = Setup();
        view.Paint(game);
        var o = view.Overlay;

        // Piece 0 (the square) at (24, 39): the middle of its top-left cell is the main colour.
        Assert.Equal(colors.Shade(0, 4), o.Get(24 + 18 + 9, 39 + 18 + 9));
        // Piece 6 is drawn 6 rows of 38 pixels lower.
        Assert.True(AnyPixel(o, 24, 39 + 38 * 6, 72, 54));
        // Count boxes at x 99 for every piece, and the total at y 337.
        for (int i = 0; i < 7; i++)
            Assert.True(AnyPixel(o, 99, 67 + 38 * i, 50, 20), $"count box {i}");
        Assert.True(AnyPixel(o, 43, 337, 50, 18));     // "Total:"
        Assert.True(AnyPixel(o, 99, 337, 50, 20));
    }

    [Fact]
    public void The_right_frame_lists_the_clear_sizes_from_single_to_more()
    {
        var (game, view, palette, _) = Setup();
        view.Paint(game);
        var o = view.Overlay;
        for (int k = 0; k < 15; k++)
        {
            Assert.True(AnyPixel(o, 452, 52 + 20 * k, 60, 18), $"label {k}");
            Assert.Equal(PixelBuffer.Pack(palette[210]), o.Get(527, 52 + 20 * k));
        }
        Assert.True(AnyPixel(o, 452, 362, 60, 18));    // "Total:"
        Assert.Equal(PixelBuffer.Pack(palette[210]), o.Get(527, 362));
    }

    [Fact]
    public void The_counts_follow_the_game()
    {
        var (game, view, _, _) = Setup();

        uint[][] Boxes() => Enumerable.Range(0, 8).Select(i =>
        {
            int y = i < 7 ? 67 + 38 * i : 337;
            return Enumerable.Range(0, 20 * 50).Select(n => view.Overlay.Get(99 + n % 50, y + n / 50)).ToArray();
        }).ToArray();

        view.Paint(game);
        var before = Boxes();

        game.Keys.Down(7);                              // drop the first piece
        while (game.Phase == GamePhase.Falling)
            game.Update();
        view.Paint(game);
        var after = Boxes();

        int kind = Enumerable.Range(0, 7).First(i => game.Player.PieceCounts[i] == 1);
        for (int box = 0; box < 8; box++)
        {
            bool changed = !before[box].SequenceEqual(after[box]);
            Assert.Equal(box == kind || box == 7, changed);   // its own kind, and the total
        }
    }
}

public class LevelFadeTests
{
    private static readonly KeyBindings Bindings = new(1, 2, 3, 4, 5, 6, 7);

    [Fact]
    public void The_screen_fades_out_while_play_goes_on_then_the_level_changes_and_the_game_freezes()
    {
        var game = new SinglePlayerGame(5, new KeyStates(), Bindings);
        game.StartLevelChange(2);
        Assert.Equal(SinglePlayerGame.LevelUpBannerFrames, game.LevelUpFrames);
        Assert.Equal(1, game.DisplayedLevel);

        int framesBefore = game.Player.Falling.Frames;
        for (int n = 1; n <= SinglePlayerGame.FadeFrames; n++)
        {
            game.Update();
            Assert.Equal(1f - n / 16f, game.Brightness, 4);
            Assert.False(game.IsFrozen);
            Assert.Equal(1, game.DisplayedLevel);
        }
        Assert.Equal(0f, game.Brightness);
        Assert.Equal(framesBefore + 16, game.Player.Falling.Frames);     // the piece kept falling

        game.Update();                                                    // the screen is black: swap
        Assert.Equal(2, game.DisplayedLevel);
        Assert.True(game.IsFrozen);
        int frozenAt = game.Player.Falling.Frames;
        for (int n = 2; n <= SinglePlayerGame.FadeFrames; n++)
        {
            game.Update();
            Assert.True(game.IsFrozen);
            Assert.Equal(n / 16f, game.Brightness, 4);
        }
        for (int n = 0; n < SinglePlayerGame.FadeWaitFrames; n++)
        {
            game.Update();
            Assert.Equal(1f, game.Brightness);
        }
        Assert.Equal(frozenAt, game.Player.Falling.Frames);               // nothing moved while frozen
        Assert.True(game.IsFrozen);                                       // 30 frozen frames in total

        game.Update();                                                    // play resumes
        Assert.False(game.IsFrozen);
        Assert.Equal(frozenAt + 1, game.Player.Falling.Frames);
        Assert.Equal(1f, game.Brightness);
    }

    [Fact]
    public void The_banner_keeps_rising_even_while_the_game_is_frozen()
    {
        var game = new SinglePlayerGame(5, new KeyStates(), Bindings);
        game.StartLevelChange(2);
        for (int n = 0; n < 25; n++)
            game.Update();
        Assert.True(game.IsFrozen);
        Assert.Equal(SinglePlayerGame.LevelUpBannerFrames - 25, game.LevelUpFrames);
    }

    [Fact]
    public void A_second_level_up_during_a_change_shows_the_newest_level()
    {
        var game = new SinglePlayerGame(5, new KeyStates(), Bindings);
        game.StartLevelChange(2);
        game.Update();
        game.StartLevelChange(3);
        for (int n = 0; n < 17; n++)
            game.Update();
        Assert.Equal(3, game.DisplayedLevel);
    }
}
