using Quadra.Core.Input;
using Quadra.Core.Rules;

namespace Quadra.Tests;

public class KeyStatesTests
{
    [Fact]
    public void Key_down_sets_pressed_and_key_up_replaces_it_with_released()
    {
        var keys = new KeyStates();
        keys.Down(7);
        Assert.Equal(KeyFlags.Pressed, keys.Get(7));
        keys.Up(7);
        Assert.Equal(KeyFlags.Released, keys.Get(7));
    }

    [Fact]
    public void A_tap_that_ends_before_the_next_frame_shows_only_as_released()
    {
        var keys = new KeyStates();
        keys.Down(1);
        keys.Up(1);
        Assert.Equal(KeyFlags.Released, keys.Get(1));
    }

    [Fact]
    public void Unrelease_keeps_pressed_and_clear_forgets_everything()
    {
        var keys = new KeyStates();
        keys.Down(1);
        keys.Unrelease(1);
        Assert.Equal(KeyFlags.Pressed, keys.Get(1));
        keys.Up(1);
        keys.Unrelease(1);
        Assert.Equal(KeyFlags.None, keys.Get(1));
        keys.Down(2);
        keys.Clear(2);
        Assert.Equal(KeyFlags.None, keys.Get(2));
    }

    [Fact]
    public void Clones_are_independent()
    {
        var keys = new KeyStates();
        keys.Down(1);
        var copy = keys.Clone();
        keys.Clear(1);
        Assert.Equal(KeyFlags.Pressed, copy.Get(1));
    }
}

public class PieceControllerTests
{
    private const int Left = 1, Right = 2, RotateLeft = 3, Down = 4, RotateRight = 5, RotateTwice = 6, Drop = 7;

    private static readonly KeyBindings Bindings = new(Left, Right, RotateLeft, Down, RotateRight, RotateTwice, Drop);

    // Delay 3 (repeat setting 2): first repeat after 13 frames, then every 3.
    private static (PlayerSim Sim, KeyStates Keys) NewPlayer(int horizontalRepeat = 2, bool continuousDown = true)
    {
        var sim = new PlayerSim(2014368, 1);
        var keys = new KeyStates();
        sim.AttachInput(keys, Bindings, horizontalRepeat, continuousDown);
        return (sim, keys);
    }

    /// <summary>Puts a piece in a known state: the square (piece 0) is easy to reason about.</summary>
    private static (PlayerSim Sim, KeyStates Keys) NewPlayerWithSquare(int horizontalRepeat = 2, bool retryBlockedMoves = false)
    {
        for (int seed = 0; seed < 1000; seed++)
        {
            var sim = new PlayerSim(seed, 1);
            if (sim.CurrentPiece != 0)
                continue;
            var keys = new KeyStates();
            sim.AttachInput(keys, Bindings, horizontalRepeat, retryBlockedMoves: retryBlockedMoves);
            return (sim, keys);
        }
        throw new InvalidOperationException("no seed starts with a square");
    }

    [Fact]
    public void Rotation_happens_when_the_key_is_released_not_when_it_is_pressed()
    {
        var (sim, keys) = NewPlayer();
        keys.Down(RotateRight);
        Assert.Equal(MoveFlags.None, sim.Frame().Moves);
        Assert.Equal(0, sim.Falling.Rotation);
        keys.Up(RotateRight);
        Assert.Equal(MoveFlags.RotateRight, sim.Frame().Moves);
        Assert.Equal(1, sim.Falling.Rotation);
        Assert.Equal(MoveFlags.None, sim.Frame().Moves); // handled and cleared
    }

    [Fact]
    public void Two_rotations_bound_to_the_same_key_only_turn_left()
    {
        var sim = new PlayerSim(2014368, 1);
        var keys = new KeyStates();
        const int up = 9;
        sim.AttachInput(keys, new KeyBindings(Left, Right, up, Down, up, RotateTwice, Drop), 2);
        keys.Down(up);
        keys.Up(up);
        Assert.Equal(MoveFlags.RotateLeft, sim.Frame().Moves);
        Assert.Equal(3, sim.Falling.Rotation);
    }

    [Fact]
    public void Down_acts_every_frame_while_held_and_once_for_a_tap()
    {
        var (sim, keys) = NewPlayer();
        keys.Down(Down);
        for (int n = 0; n < 5; n++)
            Assert.Equal(MoveFlags.Down, sim.Frame().Moves);
        keys.Up(Down);
        Assert.Equal(MoveFlags.Down, sim.Frame().Moves);   // the release still counts once
        Assert.Equal(MoveFlags.None, sim.Frame().Moves);
    }

    [Fact]
    public void Drop_acts_once_and_the_piece_lands_on_a_following_frame()
    {
        var (sim, keys) = NewPlayer();
        keys.Down(Drop);
        Assert.Equal(MoveFlags.Drop, sim.Frame().Moves);
        Assert.Equal(MoveFlags.None, sim.Frame().Moves);
        Assert.True(sim.Board.IsValidPlacement(sim.Falling.Piece, sim.Falling.Rotation, sim.Falling.Column, sim.Falling.Row));
    }

    [Fact]
    public void A_held_left_key_repeats_after_thirteen_frames_then_every_three_and_every_frame_when_blocked()
    {
        var (sim, keys) = NewPlayerWithSquare(retryBlockedMoves: true);   // the current C++ client's behaviour
        keys.Down(Left);

        var attempts = new List<int>();
        for (int frame = 0; frame < 30; frame++)
            if ((sim.Frame().Moves & MoveFlags.Left) != 0)
                attempts.Add(frame);

        // Moves: 7->6 at 0, 6->5 at 13, 5->4 at 16, 4->3 at 19 (square hits the wall), then blocked every frame.
        Assert.Equal([0, 13, 16, 19, 22, 23, 24, 25, 26, 27, 28, 29], attempts);
        Assert.Equal(3, sim.Falling.Column);
    }

    [Fact]
    public void Without_the_blocked_retry_a_key_pushed_against_the_wall_keeps_the_normal_rhythm()
    {
        // The 1.1.0 clients that recorded the bundled demos did not retry a blocked move next frame;
        // this is the default.
        var (sim, keys) = NewPlayerWithSquare();
        keys.Down(Left);
        var attempts = new List<int>();
        for (int frame = 0; frame < 30; frame++)
            if ((sim.Frame().Moves & MoveFlags.Left) != 0)
                attempts.Add(frame);
        Assert.Equal([0, 13, 16, 19, 22, 25, 28], attempts);   // wall reached at 19, then every 3rd frame
    }

    [Fact]
    public void A_shorter_delay_setting_repeats_sooner()
    {
        var (sim, keys) = NewPlayerWithSquare(horizontalRepeat: 3);   // delay 1: 11 frames, then every frame
        keys.Down(Left);
        var attempts = new List<int>();
        for (int frame = 0; frame < 14; frame++)
            if ((sim.Frame().Moves & MoveFlags.Left) != 0)
                attempts.Add(frame);
        Assert.Equal([0, 11, 12, 13], attempts);
    }

    [Fact]
    public void A_tap_moves_once()
    {
        var (sim, keys) = NewPlayerWithSquare();
        keys.Down(Left);
        keys.Up(Left);
        Assert.Equal(MoveFlags.Left, sim.Frame().Moves);
        Assert.Equal(6, sim.Falling.Column);
        for (int n = 0; n < 20; n++)
            Assert.Equal(MoveFlags.None, sim.Frame().Moves);
    }

    [Fact]
    public void Pressing_left_and_right_together_moves_nothing()
    {
        var (sim, keys) = NewPlayerWithSquare();
        keys.Down(Left);
        keys.Down(Right);
        for (int n = 0; n < 5; n++)
            Assert.Equal(MoveFlags.None, sim.Frame().Moves);
        Assert.Equal(7, sim.Falling.Column);
    }

    [Fact]
    public void Key_repeat_starts_over_for_every_new_piece()
    {
        var (sim, keys) = NewPlayerWithSquare();
        keys.Down(Left);
        for (int n = 0; n < 5; n++)
            sim.Frame();                                  // attempt at 0, now waiting
        Assert.NotEqual(0, sim.Input!.HoldLeft);

        sim.Restart();                                    // a new piece appears
        Assert.Equal(0, sim.Input.HoldLeft);
        Assert.Equal(MoveFlags.Left, sim.Frame().Moves & MoveFlags.Left); // acts at once again
    }

    [Fact]
    public void With_continuous_down_off_a_held_down_key_is_ignored_for_the_next_piece()
    {
        var (sim, keys) = NewPlayer(continuousDown: false);
        keys.Down(Down);
        Assert.Equal(MoveFlags.Down, sim.Frame().Moves);
        sim.Restart();
        Assert.Equal(MoveFlags.None, sim.Frame().Moves);

        var (sim2, keys2) = NewPlayer(continuousDown: true);
        keys2.Down(Down);
        sim2.Restart();
        Assert.Equal(MoveFlags.Down, sim2.Frame().Moves);
    }

    [Fact]
    public void A_drop_key_pressed_before_a_piece_appears_is_discarded()
    {
        var (sim, keys) = NewPlayer();
        keys.Down(Drop);
        sim.Restart();
        Assert.Equal(MoveFlags.None, sim.Frame().Moves);
    }

    [Fact]
    public void Frames_without_input_end_with_the_piece_landing_and_a_stamp_starts_the_next_piece()
    {
        var (sim, keys) = NewPlayer();
        FrameResult result = default;
        int frames = 0;
        while (result.Landed is null && frames < 5000)
        {
            result = sim.Frame();
            frames++;
        }
        Assert.NotNull(result.Landed);
        Assert.Equal(frames, sim.Falling.Frames);

        // Recorded moves keep arriving until the stamp is processed; they are counted, not applied.
        Assert.Equal(MoveFlags.None, sim.Frame().Moves);
        Assert.Equal(1, sim.MovesAfterLanding);

        var landed = result.Landed!.Value;
        var stamp = sim.Stamp(landed.X, landed.Y, landed.Rotation, 0);
        Assert.True(stamp.Valid);
        Assert.Null(sim.Landed);
        Assert.Equal(0, sim.Falling.Frames);
    }

    [Fact]
    public void Frame_needs_an_attached_input()
    {
        var sim = new PlayerSim(1, 1);
        Assert.Throws<InvalidOperationException>(() => sim.Frame());
    }
}

public class DeathCheckTests
{
    [Fact]
    public void A_fresh_board_never_blocks_the_spawn()
    {
        for (int seed = 0; seed < 50; seed++)
            Assert.False(new PlayerSim(seed, 1).SpawnBlocked);
    }

    [Fact]
    public void A_piece_that_appears_on_top_of_blocks_means_death_and_cannot_be_played()
    {
        var sim = new PlayerSim(2014368, 1);
        sim.AttachInput(new Quadra.Core.Input.KeyStates(), new Quadra.Core.Input.KeyBindings(1, 2, 3, 4, 5, 6, 7), 2);
        // Fill the spawn area (rows 10-13, columns 6-10) directly, whatever the piece is.
        for (int x = 4; x <= 13; x++)
            for (int y = 10; y <= 13; y++)
                sim.Board.Stamp(0, 0, x - 1, y - 1);   // squares tile the area
        Assert.True(sim.SpawnBlocked);
        Assert.Throws<InvalidOperationException>(() => sim.Frame());
        sim.Die();
        sim.Restart();
        Assert.False(sim.SpawnBlocked);
        sim.Frame();
    }
}
