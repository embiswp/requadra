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
