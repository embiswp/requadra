using Quadra.Core.Game;
using Quadra.Core.Input;
using Quadra.Core.Rules;

namespace Quadra.Tests;

public class DropPointsTests
{
    [Theory]
    [InlineData(1, 50)]    // quick drops earn the maximum
    [InlineData(48, 50)]
    [InlineData(49, 49)]   // 2 extra frames: 51 - 50 = 1 frame over
    [InlineData(98, 25)]
    [InlineData(148, 0)]
    [InlineData(500, 0)]
    public void Points_fall_with_time_in_play(int frames, int expected) =>
        Assert.Equal(expected, LegacyScoring.DropPoints(frames));
}

public class LevelUpTests
{
    private static PlayerSim NewSim(bool levelUp) => new(7, 1, 2, levelUp);

    /// <summary>Clears one line with the piece the sim currently holds (board is reset first).</summary>
    private static int ClearOneLine(PlayerSim sim)
    {
        int piece = sim.CurrentPiece;
        sim.Board.Reset();
        int y = 0;
        while (!sim.Board.Collides(piece, 0, 4, y + 1))
            y++;

        // Fill every cell of the bottom row that the piece does not cover.
        var covered = new HashSet<int>();
        for (int i = 0; i < 4; i++)
            if (Pieces.Shapes[piece, 0, 31 - y, i] != 0)
                covered.Add(4 + i);
        for (int x = 4; x < 14; x++)
            if (!covered.Contains(x))
                sim.Board.Fill(31, x);

        return sim.Stamp(4, y, 0, 0).Lines;
    }

    [Fact]
    public void The_level_rises_after_fifteen_lines_when_level_up_is_on()
    {
        var sim = NewSim(levelUp: true);
        for (int n = 1; n <= 14; n++)
        {
            Assert.Equal(1, ClearOneLine(sim));
            Assert.Equal(1, sim.Level);
        }
        Assert.Equal(1, ClearOneLine(sim));
        Assert.Equal(15, sim.LinesThisRun);
        Assert.Equal(2, sim.Level);
    }

    [Fact]
    public void The_next_level_needs_thirty_lines_in_total()
    {
        var sim = NewSim(levelUp: true);
        for (int n = 0; n < 29; n++)
            ClearOneLine(sim);
        Assert.Equal(2, sim.Level);
        ClearOneLine(sim);
        Assert.Equal(3, sim.Level);
    }

    [Fact]
    public void Gravity_follows_the_level()
    {
        var sim = NewSim(levelUp: true);
        // Level 1 falls a row in 72 frames; level 2 is faster (9 units a frame instead of 4).
        int Frames()
        {
            sim.Restart();
            var piece = sim.Falling;
            int start = piece.Row;
            int frames = 0;
            while (piece.Row == start && frames < 1000)
            {
                piece.Step(MoveFlags.None);
                frames++;
            }
            return frames;
        }
        int level1 = Frames();
        // Restart resets the level, so raise it by clearing lines on a fresh run and measure then.
        sim = NewSim(levelUp: true);
        for (int n = 0; n < 15; n++)
            ClearOneLine(sim);
        Assert.Equal(2, sim.Level);
        int before = sim.Falling.Row;
        int frames2 = 0;
        while (sim.Falling.Row == before && frames2 < 1000)
        {
            sim.Falling.Step(MoveFlags.None);
            frames2++;
        }
        Assert.True(frames2 < level1, $"level 2 took {frames2} frames per row, level 1 took {level1}");
    }

    [Fact]
    public void Level_stays_put_without_level_up_and_resets_on_restart()
    {
        var plain = NewSim(levelUp: false);
        for (int n = 0; n < 20; n++)
            ClearOneLine(plain);
        Assert.Equal(1, plain.Level);

        var sim = NewSim(levelUp: true);
        for (int n = 0; n < 15; n++)
            ClearOneLine(sim);
        Assert.Equal(2, sim.Level);
        sim.Restart();
        Assert.Equal(1, sim.Level);
        Assert.Equal(0, sim.LinesThisRun);
    }
}

public class SinglePlayerGameTests
{
    private static readonly KeyBindings Bindings = new(1, 2, 3, 4, 5, 6, 7);
    private const int Drop = 7;

    [Fact]
    public void A_new_game_starts_with_a_falling_piece_and_nothing_scored()
    {
        var game = new SinglePlayerGame(123, new KeyStates(), Bindings);
        Assert.Equal(GamePhase.Falling, game.Phase);
        Assert.Equal(0, game.Player.Score);
        Assert.Equal(1, game.Player.Level);
        Assert.Equal(3, game.Player.Upcoming.Count);
    }

    [Fact]
    public void Doing_nothing_lets_the_piece_land_and_pause_before_the_next_one()
    {
        var game = new SinglePlayerGame(123, new KeyStates(), Bindings);
        int frames = 0;
        while (game.Phase == GamePhase.Falling && frames < 5000)
        {
            game.Update();
            frames++;
        }
        Assert.Equal(GamePhase.Settling, game.Phase);
        Assert.Equal(1, game.Player.Stamps);
        Assert.Equal(0, game.Player.InvalidStamps);
        // The piece fell from row 10 at 4 units per frame (72 frames per row) down to the floor.
        Assert.InRange(frames, 1000, 1700);

        for (int n = 0; n < SinglePlayerGame.PauseFrames; n++)
            game.Update();
        Assert.Equal(GamePhase.Falling, game.Phase);
        Assert.Equal(3, game.Player.Upcoming.Count);
        Assert.NotEqual(-1, game.Player.CurrentPiece);
    }

    [Fact]
    public void Dropping_every_piece_in_the_middle_ends_the_game_with_a_stacked_board()
    {
        var keys = new KeyStates();
        var game = new SinglePlayerGame(99, keys, Bindings);
        int frames = 0;
        while (!game.IsOver && frames < 200_000)
        {
            keys.Down(Drop);          // a drop on every frame; the controller consumes it once per piece
            game.Update();
            frames++;
        }
        Assert.True(game.IsOver, "the board never filled up");
        Assert.True(game.Player.Stamps >= 8);          // 20 visible rows fill after roughly ten pieces
        Assert.Equal(1, game.Player.Deaths);
        Assert.Equal(0, game.Player.InvalidStamps);
        Assert.True(game.Player.Score > 0);
    }

    [Fact]
    public void Once_the_game_is_over_updates_do_nothing()
    {
        var keys = new KeyStates();
        var game = new SinglePlayerGame(99, keys, Bindings);
        while (!game.IsOver)
        {
            keys.Down(Drop);
            game.Update();
        }
        int frame = game.Frame;
        game.Update();
        Assert.Equal(frame, game.Frame);
    }

    [Fact]
    public void The_same_seed_and_keys_play_the_same_game()
    {
        static (int Score, int Stamps, int Frame) Play(int seed)
        {
            var keys = new KeyStates();
            var game = new SinglePlayerGame(seed, keys, Bindings);
            var rng = new Random(5);
            for (int n = 0; n < 20_000 && !game.IsOver; n++)
            {
                int key = rng.Next(1, 8);
                if (rng.Next(3) == 0) { keys.Down(key); if (rng.Next(2) == 0) keys.Up(key); }
                game.Update();
            }
            return (game.Player.Score, game.Player.Stamps, game.Frame);
        }
        Assert.Equal(Play(42), Play(42));
        Assert.NotEqual(Play(42), Play(43));
    }
}
