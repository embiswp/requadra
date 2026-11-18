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

public class GarbageTests
{
    [Theory]
    // count, complexity, column -> hole columns. Values below were all seen in the bundled demos.
    [InlineData(3, 4, 9, new[] { 12, 5, 8 })]
    [InlineData(1, 1, 11, new[] { 11 })]
    [InlineData(2, 3, 12, new[] { 4, 6 })]
    public void Holes_are_placed_like_the_C_client(int count, int complexity, int column, int[] expected)
    {
        var sim = new PlayerSim(1, 1);
        sim.ReceiveLines(0, count, complexity, column);
        Assert.Equal(expected, sim.PendingGarbage);
    }

    [Fact]
    public void At_most_twenty_garbage_rows_are_queued()
    {
        var sim = new PlayerSim(1, 1);
        sim.ReceiveLines(0, 15, 1, 8);
        sim.ReceiveLines(0, 15, 1, 8);
        Assert.Equal(20, sim.PendingGarbage.Count);
    }

    [Fact]
    public void Garbage_is_credited_to_the_biggest_recent_attacker()
    {
        var sim = new PlayerSim(1, 1);
        sim.ReceiveLines(2, 1, 1, 8);
        Assert.Equal(2, sim.LastAttacker);
        sim.ReceiveLines(5, 3, 1, 8);
        Assert.Equal(5, sim.LastAttacker);
        sim.ReceiveLines(2, 1, 1, 8);   // 4 total for 2, still behind 6 for 5
        Assert.Equal(5, sim.LastAttacker);
        Assert.Equal(5, sim.Die());
        Assert.Empty(sim.PendingGarbage);
    }

    [Fact]
    public void Restart_drops_garbage_that_arrived_while_dead()
    {
        var sim = new PlayerSim(1, 1);
        sim.ReceiveLines(0, 2, 1, 8);
        sim.Restart();
        Assert.Empty(sim.PendingGarbage);
        Assert.Equal(PlayerSim.NoAttacker, sim.LastAttacker);
    }

    [Fact]
    public void A_garbage_row_pushes_the_board_up_and_leaves_one_hole()
    {
        var b = new Board();
        b.Stamp(5, 0, 4, 30);               // I piece on the floor row, columns 4-7
        b.AddGarbage([8]);

        for (int i = Board.FirstColumn; i < Board.EndColumn; i++)
            Assert.Equal(i != 8, b.IsOccupied(31, i));
        for (int i = 4; i <= 7; i++)
            Assert.True(b.IsOccupied(30, i), $"column {i} was pushed up one row");
    }
}

public class FallingPieceTests
{
    private static FallingPiece NewPiece(Board board, int piece, int level = 1, int verticalRepeat = 2)
    {
        var p = new FallingPiece(board, LegacySpeeds.Gravity(level), LegacySpeeds.SoftDrop(verticalRepeat));
        p.Spawn(piece);
        return p;
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 9)]
    [InlineData(10, 49)]
    [InlineData(11, 53)]
    public void Gravity_grows_with_level(int level, int expected) => Assert.Equal(expected, LegacySpeeds.Gravity(level));

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 56)]
    [InlineData(2, 113)]
    [InlineData(3, 180)]
    public void Soft_drop_speed_depends_on_the_repeat_setting(int repeat, int expected) =>
        Assert.Equal(expected, LegacySpeeds.SoftDrop(repeat));

    [Fact]
    public void A_new_piece_starts_in_the_spawn_position()
    {
        var piece = NewPiece(new Board(), 2);
        Assert.Equal((7, 10, 0, 0), (piece.Column, piece.Row, piece.Rotation, piece.Frames));
    }

    [Fact]
    public void Every_piece_falls_straight_down_and_lands_on_an_empty_board()
    {
        for (int q = 0; q < Pieces.Count; q++)
        {
            var board = new Board();
            var piece = NewPiece(board, q);
            Placement? landed = null;
            for (int frame = 0; frame < 5000 && landed is null; frame++)
                landed = piece.Step(MoveFlags.None);

            Assert.NotNull(landed);
            Assert.Equal(7, landed!.Value.X);
            Assert.Equal(0, landed.Value.Rotation);
            Assert.True(board.IsValidPlacement(q, 0, landed.Value.X, landed.Value.Y), $"piece {q}");
        }
    }

    [Fact]
    public void Hard_drop_moves_the_piece_to_its_resting_row()
    {
        var board = new Board();
        var piece = NewPiece(board, 5);          // horizontal I: its cells are in row 1 of the box
        piece.Step(MoveFlags.Drop);
        Assert.Equal(30, piece.Row);
        Assert.True(board.IsValidPlacement(5, 0, piece.Column, piece.Row));
    }

    [Fact]
    public void Pieces_move_sideways_until_they_hit_the_wall()
    {
        var board = new Board();
        var piece = NewPiece(board, 0);          // square: cells in columns 1-2 of the box
        piece.Step(MoveFlags.Left);
        Assert.Equal(6, piece.Column);
        for (int n = 0; n < 20; n++)
            piece.Step(MoveFlags.Left);
        Assert.Equal(3, piece.Column);           // leftmost cell on column 4
        for (int n = 0; n < 20; n++)
            piece.Step(MoveFlags.Right);
        Assert.Equal(11, piece.Column);          // rightmost cell on column 13
    }

    [Fact]
    public void Rotation_changes_the_orientation_and_wraps()
    {
        var piece = NewPiece(new Board(), 3);
        piece.Step(MoveFlags.RotateRight);
        Assert.Equal(1, piece.Rotation);
        piece.Step(MoveFlags.RotateLeft);
        piece.Step(MoveFlags.RotateLeft);
        Assert.Equal(3, piece.Rotation);
        piece.Step(MoveFlags.RotateTwice);
        Assert.Equal(1, piece.Rotation);
    }
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

    [Theory]
    [InlineData("demo00.rec")]
    [InlineData("demo01.rec")]
    [InlineData("demo02.rec")]
    [InlineData("demo03.rec")]
    public void Replay_reproduces_every_players_recorded_score_lines_frags_and_deaths(string name)
    {
        var demo = DemoFile.Load(DemoPath(name));
        var players = DemoReplay.Run(demo);
        int expectedCount = demo.Summary.Keys.Count(k => k.StartsWith("players/") && k.EndsWith("/name"));
        Assert.Equal(expectedCount, players.Count);

        foreach (var p in players)
        {
            int i = Enumerable.Range(0, 8).First(n => demo.Summary.GetValueOrDefault($"players/{n}/name") == p.Name);
            string prefix = $"players/{i}/";
            Assert.Equal(0, p.InvalidStamps);
            Assert.Equal(int.Parse(demo.Summary[prefix + "score"]), p.Score);
            Assert.Equal(int.Parse(demo.Summary[prefix + "lines"]), p.Lines);
            Assert.Equal(int.Parse(demo.Summary[prefix + "frags"]), p.Frags);
            Assert.Equal(int.Parse(demo.Summary[prefix + "deaths"]), p.Deaths);
        }
    }

    [Theory]
    [InlineData("demo00.rec")]
    [InlineData("demo01.rec")]
    [InlineData("demo02.rec")]
    [InlineData("demo03.rec")]
    public void Playing_the_moves_lands_every_piece_exactly_where_its_stamp_packet_says(string name)
    {
        var demo = DemoFile.Load(DemoPath(name));
        var players = DemoReplay.Run(demo, ReplayInput.Moves);

        foreach (var p in players)
        {
            Assert.True(p.Stamps > 0);
            Assert.Equal(0, p.LandingMismatches);
            Assert.Equal(0, p.MovesAfterLanding);

            int i = Enumerable.Range(0, 8).First(n => demo.Summary.GetValueOrDefault($"players/{n}/name") == p.Name);
            Assert.Equal(int.Parse(demo.Summary[$"players/{i}/score"]), p.Score);
            Assert.Equal(int.Parse(demo.Summary[$"players/{i}/lines"]), p.Lines);
        }
        Assert.Equal(demo.Packets.Count(d => d.Packet is Quadra.Core.Net.StampBlockPacket), players.Sum(p => p.Stamps));
    }

    [Fact]
    public void Single_player_demo_matches_the_known_figures()
    {
        var player = Assert.Single(DemoReplay.Run(DemoFile.Load(DemoPath("demo02.rec"))));
        Assert.Equal(54826, player.Score);
        Assert.Equal(18, player.Lines);
        Assert.Equal(49, player.Stamps);
    }

    [Fact]
    public void Rules_that_are_not_ported_are_refused_instead_of_replayed_wrongly()
    {
        var demo = DemoFile.Load(DemoPath("demo02.rec"));
        var newer = new DemoFileWithVersion(demo, 23);
        Assert.Throws<NotSupportedException>(() => DemoReplay.Run(newer.Demo));
    }

    // Builds a demo whose game packet claims a different net version.
    private sealed class DemoFileWithVersion
    {
        public DemoFile Demo { get; }

        public DemoFileWithVersion(DemoFile source, byte version)
        {
            var gs = source.GameServer! with { Version = version };
            Demo = DemoFile.FromParts(gs, source.Packets, source.Summary);
        }
    }
}
