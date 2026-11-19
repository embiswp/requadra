using Quadra.Core.Rules;

namespace Quadra.Core.Game;

/// <summary>The frames of a line-clear animation, shared by single-player and network games.</summary>
public static class ClearAnimation
{
    /// <summary>
    /// Lays out, frame by frame, what the original shows after a landing: each step of the clear flashes
    /// its rows for 17 frames, then loose cells fall one row every other frame. Frame counts follow
    /// <c>Player_flash_lines</c>, <c>Player_check_link</c> and <c>Player_check_line</c>.
    /// </summary>
    public static List<AnimationFrame> Build(IReadOnlyList<ClearPass> passes, int newLevel)
    {
        var timeline = new List<AnimationFrame>();
        var none = Array.Empty<int>();
        var noEvents = Array.Empty<GameEvent>();

        foreach (var pass in passes)
        {
            // 16 frames of flashing (white, white, red, red, ...), then the flash is off for a frame
            // and the falling module starts up for another.
            for (int anim = 0; anim < 16; anim++)
            {
                var events = anim == 0 ? new[] { new GameEvent(GameEventKind.LineFlash, Value: pass.Rows.Count, Combo: pass.Combo) } : noEvents;
                timeline.Add(new AnimationFrame(pass.AfterRemoval, pass.Rows, ((anim >> 1) & 1) == 1 ? SinglePlayerGame.FlashRed : SinglePlayerGame.FlashWhite, events));
            }
            timeline.Add(new AnimationFrame(pass.AfterRemoval, none, 0, noEvents));
            timeline.Add(new AnimationFrame(pass.AfterRemoval, none, 0, noEvents));

            // Falling: one frame to work out what is loose, one to move it a row.
            IBoardView shown = pass.AfterRemoval;
            foreach (var step in pass.FallSteps)
            {
                timeline.Add(new AnimationFrame(shown, none, 0, noEvents));
                shown = step;
                timeline.Add(new AnimationFrame(shown, none, 0, noEvents));
            }
            timeline.Add(new AnimationFrame(shown, none, 0, noEvents));
            var done = pass.FallSteps.Count > 0
                ? new[] { new GameEvent(GameEventKind.BlocksFell, Value: pass.FallSteps.Count, Combo: pass.Combo) }
                : noEvents;
            timeline.Add(new AnimationFrame(shown, none, 0, done));
            // The line check that follows (and starts the next step, or scores the move).
            timeline.Add(new AnimationFrame(shown, none, 0, noEvents));
        }

        if (newLevel > 0 && timeline.Count > 0)
        {
            var last = timeline[^1];
            timeline[^1] = last with { Events = [.. last.Events, new GameEvent(GameEventKind.LevelUp, Value: newLevel)] };
        }
        return timeline;
    }
}
