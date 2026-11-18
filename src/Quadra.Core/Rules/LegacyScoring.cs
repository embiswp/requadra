namespace Quadra.Core.Rules;

/// <summary>
/// Line-clear scoring for net versions below 23 (<c>Canvas::give_line</c> and
/// <c>Player_check_line::check_clean</c> in <c>legacy/source/</c>).
/// </summary>
public static class LegacyScoring
{
    /// <summary>Points for an empty board; awarded at once, without the level bonus.</summary>
    public const int CleanBoardBonus = 5000;

    /// <summary>
    /// Points for placing a piece, from how long it was in play: at most 50, and fewer once it has
    /// been falling for more than 50 frames (the formula in the current C++ client). Two extra frames
    /// count for the pause before the piece appears. It matches 87 % of the drop scores recorded in
    /// the 1.1.0 demos; the older clients used a slightly different formula that is not known.
    /// </summary>
    public static int DropPoints(int framesInPlay)
    {
        int slow = Math.Max(framesInPlay + 2 - 50, 0);
        return Math.Max(0, 100 - slow) >> 1;
    }

    /// <summary>
    /// Points for one move that cleared <paramref name="lines"/> rows in total over
    /// <paramref name="combo"/> successive passes (a combo of 1 is a plain clear).
    /// </summary>
    public static int Clear(int lines, int combo, int level)
    {
        int points = lines switch
        {
            1 => 250,
            2 => 500,
            3 => 1000,
            4 => 2000,
            _ => 200 * lines * lines,
        };
        points += 1000 * (combo - 1);
        points += points / 10 * level;
        return points;
    }
}
