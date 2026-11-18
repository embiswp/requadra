using Quadra.Core.Input;
using Quadra.Core.Rules;

namespace Quadra.Core.Game;

public enum GamePhase
{
    /// <summary>A piece is in play and the keys control it.</summary>
    Falling,

    /// <summary>The piece has landed; clearing and the pause before the next piece take a few frames.</summary>
    Settling,

    /// <summary>A new piece appeared on top of blocks. Nothing moves any more.</summary>
    GameOver,
}

public enum GameEventKind
{
    /// <summary>A piece turned. <see cref="GameEvent.Column"/> is where.</summary>
    Rotated,

    /// <summary>A piece was stamped on the board at <see cref="GameEvent.Column"/>.</summary>
    Stamped,

    /// <summary>Cleared rows start to flash; <see cref="GameEvent.Combo"/> is the step of the combo.</summary>
    LineFlash,

    /// <summary>Loose cells finished falling after <see cref="GameEvent.Value"/> steps.</summary>
    BlocksFell,

    /// <summary>The level rose to <see cref="GameEvent.Value"/>.</summary>
    LevelUp,

    GameOver,
}

public readonly record struct GameEvent(GameEventKind Kind, int Column = 0, int Value = 0, int Combo = 0);

/// <summary>
/// What the board looks like during a line clear. <paramref name="FlashColor"/> is a palette index (0
/// for no flash) with which <paramref name="FlashRows"/> are filled.
/// </summary>
public sealed record AnimationFrame(IBoardView Board, IReadOnlyList<int> FlashRows, int FlashColor, IReadOnlyList<GameEvent> Events);

/// <summary>
/// A single-player game on the 1.1 rules, advanced one logic frame at a time. It owns the loop the C++
/// client spreads over its executors: play the piece while keys are down, stamp it when it lands,
/// play the line-clear animation, then bring in the next piece or end the game.
/// A front end only feeds <see cref="Keys"/> and calls <see cref="Update"/> every 10 ms
/// (<see cref="FramesPerSecond"/>), draws <see cref="Player"/> (or the current
/// <see cref="Animation"/>) and plays the <see cref="Events"/>.
/// </summary>
public sealed class SinglePlayerGame
{
    /// <summary>The original steps its logic every 10 ms.</summary>
    public const int FramesPerSecond = 100;

    /// <summary>Frames between the end of a landing (or its clear) and the next piece, as in the original.</summary>
    public const int PauseFrames = 2;

    /// <summary>Frames the level-up banner rises (it is drawn 1 pixel higher each frame).</summary>
    public const int LevelUpBannerFrames = 220;

    /// <summary>Palette index a flashing row alternates between with <see cref="FlashWhite"/>.</summary>
    public const int FlashRed = 200;
    public const int FlashWhite = 255;

    private readonly List<AnimationFrame> _timeline = [];
    private int _timelineIndex;
    private int _pauseFramesLeft;

    public SinglePlayerGame(int seed, KeyStates keys, KeyBindings bindings, int horizontalRepeat = 2, int verticalRepeat = 2, int startLevel = 1)
    {
        Keys = keys;
        Player = new PlayerSim(seed, startLevel, verticalRepeat, levelUp: true) { RecordClearAnimation = true };
        Player.AttachInput(keys, bindings, horizontalRepeat);
        DisplayedLevel = Player.Level;
    }

    public KeyStates Keys { get; }
    public PlayerSim Player { get; }
    public GamePhase Phase { get; private set; } = GamePhase.Falling;

    /// <summary>Frames the game has been advanced (including the pauses).</summary>
    public int Frame { get; private set; }

    public bool IsOver => Phase == GamePhase.GameOver;

    /// <summary>
    /// The level that background, colours and sounds follow. It changes when the clear that earned
    /// the level has finished playing, not at the instant of the landing.
    /// </summary>
    public int DisplayedLevel { get; private set; }

    /// <summary>Frames left of the rising "level up" banner (0 when it is not shown).</summary>
    public int LevelUpFrames { get; private set; }

    /// <summary>What happened since the front end last cleared this list.</summary>
    public List<GameEvent> Events { get; } = [];

    /// <summary>The look of the board during a line clear, or null when the live board should be drawn.</summary>
    public AnimationFrame? Animation =>
        Phase == GamePhase.Settling && _timelineIndex < _timeline.Count ? _timeline[_timelineIndex] : null;

    /// <summary>Advances one logic frame.</summary>
    public void Update()
    {
        if (Phase == GamePhase.GameOver)
            return;
        Frame++;
        if (LevelUpFrames > 0)
            LevelUpFrames--;

        if (Phase == GamePhase.Settling)
        {
            Settle();
            return;
        }

        int rotation = Player.Falling.Rotation;
        var result = Player.Frame();
        if (Player.Falling.Rotation != rotation)
            Events.Add(new GameEvent(GameEventKind.Rotated, Player.Falling.Column));
        if (result.Landed is not { } landed)
            return;

        int levelBefore = Player.Level;
        int drop = LegacyScoring.DropPoints(Player.Falling.Frames);
        Events.Add(new GameEvent(GameEventKind.Stamped, landed.X));
        Player.Stamp(landed.X, landed.Y, landed.Rotation, drop);

        if (Player.SpawnBlocked)
        {
            Player.Die();
            Phase = GamePhase.GameOver;
            Events.Add(new GameEvent(GameEventKind.GameOver));
            return;
        }

        BuildTimeline(Player.LastClear, Player.Level > levelBefore ? Player.Level : 0);
        _timelineIndex = 0;
        _pauseFramesLeft = PauseFrames;
        Phase = GamePhase.Settling;
        if (_timeline.Count > 0)
            Emit(_timeline[0]);          // the first frame of the animation is on screen from now on
        else
            DisplayedLevel = Player.Level;
    }

    private void Settle()
    {
        if (_timelineIndex < _timeline.Count)
        {
            _timelineIndex++;
            if (_timelineIndex < _timeline.Count)
                Emit(_timeline[_timelineIndex]);
            return;
        }
        if (--_pauseFramesLeft <= 0)
            Phase = GamePhase.Falling;
    }

    /// <summary>Reports the events that belong to a frame that has just come on screen.</summary>
    private void Emit(AnimationFrame frame)
    {
        foreach (var e in frame.Events)
        {
            Events.Add(e);
            if (e.Kind == GameEventKind.LevelUp)
            {
                DisplayedLevel = e.Value;
                LevelUpFrames = LevelUpBannerFrames;
            }
        }
    }

    /// <summary>
    /// Lays out, frame by frame, what the original shows after a landing: each step of the clear flashes
    /// its rows for 17 frames, then loose cells fall one row every other frame. Frame counts follow
    /// <c>Player_flash_lines</c>, <c>Player_check_link</c> and <c>Player_check_line</c>.
    /// </summary>
    private void BuildTimeline(IReadOnlyList<ClearPass> passes, int newLevel)
    {
        _timeline.Clear();
        var none = Array.Empty<int>();
        var noEvents = Array.Empty<GameEvent>();

        foreach (var pass in passes)
        {
            // 16 frames of flashing (white, white, red, red, ...), then the flash is off for a frame
            // and the falling module starts up for another.
            for (int anim = 0; anim < 16; anim++)
            {
                var events = anim == 0 ? new[] { new GameEvent(GameEventKind.LineFlash, Value: pass.Rows.Count, Combo: pass.Combo) } : noEvents;
                _timeline.Add(new AnimationFrame(pass.AfterRemoval, pass.Rows, ((anim >> 1) & 1) == 1 ? FlashRed : FlashWhite, events));
            }
            _timeline.Add(new AnimationFrame(pass.AfterRemoval, none, 0, noEvents));
            _timeline.Add(new AnimationFrame(pass.AfterRemoval, none, 0, noEvents));

            // Falling: one frame to work out what is loose, one to move it a row.
            IBoardView shown = pass.AfterRemoval;
            foreach (var step in pass.FallSteps)
            {
                _timeline.Add(new AnimationFrame(shown, none, 0, noEvents));
                shown = step;
                _timeline.Add(new AnimationFrame(shown, none, 0, noEvents));
            }
            _timeline.Add(new AnimationFrame(shown, none, 0, noEvents));
            var done = pass.FallSteps.Count > 0
                ? new[] { new GameEvent(GameEventKind.BlocksFell, Value: pass.FallSteps.Count, Combo: pass.Combo) }
                : noEvents;
            _timeline.Add(new AnimationFrame(shown, none, 0, done));
            // The line check that follows (and starts the next step, or scores the move).
            _timeline.Add(new AnimationFrame(shown, none, 0, noEvents));
        }

        if (newLevel > 0 && _timeline.Count > 0)
        {
            var last = _timeline[^1];
            _timeline[^1] = last with { Events = [.. last.Events, new GameEvent(GameEventKind.LevelUp, Value: newLevel)] };
        }
    }
}
