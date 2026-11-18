using Quadra.Core.Demos;
using Quadra.Core.Rules;

namespace Quadra.Tests;

public class LegacyRandomTests
{
    [Fact]
    public void Matches_values_computed_from_the_C_algorithm()
    {
        // Reference: tmp = seed*0x41c64e6d + 0x3039 (int32 wrap), seed = tmp >> 10, value = tmp & 0xFFFF.
        var r = new LegacyRandom(2014368);
        int seed = 2014368;
        for (int n = 0; n < 1000; n++)
        {
            int tmp = unchecked(seed * 0x41c64e6d + 0x3039);
            seed = tmp >> 10;
            Assert.Equal((ushort)(tmp & 0xFFFF), r.Next());
        }
    }

    [Fact]
    public void Negative_seeds_are_handled_like_signed_32_bit_ints()
    {
        var r = new LegacyRandom(-1487427);
        Assert.InRange(r.NextPiece(), 0, 6);
        Assert.True(r.Seed != -1487427);
    }
}

public class PiecesTests
{
    [Fact]
    public void Every_piece_has_four_cells_in_every_rotation()
    {
        for (int q = 0; q < Pieces.Count; q++)
            for (int r = 0; r < Pieces.Rotations; r++)
            {
                int cells = 0;
                for (int j = 0; j < 4; j++)
                    for (int i = 0; i < 4; i++)
                        if (Pieces.Shapes[q, r, j, i] != 0)
                            cells++;
                Assert.Equal(4, cells);
            }
    }
}

public class BoardTests
{
    [Fact]
    public void New_board_has_walls_and_floor_but_an_empty_playfield()
    {
        var b = new Board();
        Assert.True(b.IsEmpty());
        Assert.True(b.IsOccupied(5, 3));
        Assert.True(b.IsOccupied(5, 14));
        Assert.True(b.IsOccupied(32, 8));
        Assert.False(b.IsOccupied(5, 8));
    }

    [Fact]
    public void Straight_piece_stamped_on_the_floor_is_a_valid_placement()
    {
        var b = new Board();
        // piece 5 (I), rotation 0 occupies row 1 of its 4x4 box
        Assert.True(b.IsValidPlacement(5, 0, 5, 30));
        Assert.False(b.IsValidPlacement(5, 0, 5, 29)); // floating
        Assert.False(b.IsValidPlacement(5, 0, 5, 31)); // inside the floor
    }

    [Fact]
    public void Clearing_a_row_drops_the_cells_that_lost_their_support()
    {
        var b = new Board();
        b.Stamp(5, 0, 4, 30);   // I piece: row 31, columns 4-7
        b.Stamp(5, 0, 8, 30);   // I piece: row 31, columns 8-11
        b.Stamp(0, 0, 11, 29);  // square: rows 30-31, columns 12-13
        Assert.True(b.IsOccupied(30, 12) && b.IsOccupied(30, 13));

        Assert.Equal(1, b.ClearFullRows());          // only row 31 is complete
        Assert.False(b.IsOccupied(31, 8));
        Assert.True(b.IsOccupied(30, 12));           // nothing has fallen yet

        b.DropUnsupported();
        Assert.False(b.IsOccupied(30, 12));
        Assert.True(b.IsOccupied(31, 12) && b.IsOccupied(31, 13)); // the square's top half landed on the floor
        Assert.Equal(2, CountOccupied(b));
    }

    [Fact]
    public void A_row_with_a_gap_is_not_cleared()
    {
        var b = new Board();
        b.Stamp(5, 0, 4, 30);
        b.Stamp(5, 0, 8, 30);
        Assert.Equal(0, b.ClearFullRows());
        Assert.Equal(8, CountOccupied(b));
    }

    private static int CountOccupied(Board b)
    {
        int n = 0;
        for (int j = 0; j < Board.Rows; j++)
            for (int i = Board.FirstColumn; i < Board.EndColumn; i++)
                if (b.IsOccupied(j, i))
                    n++;
        return n;
    }
}

public class ScoringTests
{
    [Theory]
    [InlineData(1, 1, 1, 275)]    // 250 + 10 % level 1
    [InlineData(2, 1, 1, 550)]
    [InlineData(3, 1, 1, 1100)]
    [InlineData(4, 1, 1, 2200)]
    [InlineData(5, 1, 1, 5500)]   // 200 * 25 = 5000, +10 %
    [InlineData(1, 2, 1, 1375)]   // (250 + 1000), +10 %
    public void Line_clear_points(int lines, int combo, int level, int expected) =>
        Assert.Equal(expected, LegacyScoring.Clear(lines, combo, level));
}

public class ReplayTests
{
    private static string DemoPath(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "legacy", "demos", name);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(name);
    }

    [Fact]
    public void Single_player_demo_reproduces_the_recorded_score_and_lines_exactly()
    {
        var demo = DemoFile.Load(DemoPath("demo02.rec"));
        var player = Assert.Single(DemoReplay.Run(demo));

        Assert.True(player.ExpectedExact);
        Assert.Equal(0, player.InvalidStamps);
        Assert.Equal(49, player.Stamps);
        Assert.Equal(int.Parse(demo.Summary["players/0/score"]), player.Score);
        Assert.Equal(int.Parse(demo.Summary["players/0/lines"]), player.Lines);
        Assert.Equal(54826, player.Score);
        Assert.Equal(18, player.Lines);
    }

    [Theory]
    [InlineData("demo00.rec")]
    [InlineData("demo01.rec")]
    [InlineData("demo03.rec")]
    public void Multiplayer_demos_run_and_flag_players_hit_by_garbage_as_not_exact(string name)
    {
        var demo = DemoFile.Load(DemoPath(name));
        var players = DemoReplay.Run(demo);
        Assert.Equal(4, players.Count);
        // Garbage rows are not ported, so every player who received some is marked not exact.
        Assert.Contains(players, p => !p.ExpectedExact);
        foreach (var p in players.Where(p => p.ExpectedExact))
        {
            int i = Enumerable.Range(0, 8).First(n => demo.Summary.GetValueOrDefault($"players/{n}/name") == p.Name);
            Assert.Equal(int.Parse(demo.Summary[$"players/{i}/score"]), p.Score);
        }
    }
}
