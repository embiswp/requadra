using Quadra.Core.Net;

namespace Quadra.Core.Game;

/// <summary>
/// How a network game is scored for its end conditions, as <c>Net_list::check_end_game</c> does it: each
/// team's total of the goal statistic over the players it has now (frags, or score, or lines), the
/// leading team, and whether the two best teams are level.
/// </summary>
public static class GameGoals
{
    /// <summary>Frames per minute: the original counts game time in hundredths of a second.</summary>
    public const int FramesPerMinute = 6000;

    /// <summary>What a team counts: frags for frag and timed games (and for none), score for points, lines for lines.</summary>
    public static IReadOnlyDictionary<int, int> TeamTotals(MultiplayerState state, GameEnd end)
    {
        var totals = new Dictionary<int, int>();
        foreach (var p in state.Players.Values)
        {
            int value = end switch
            {
                GameEnd.Points => p.Sim.Score,
                GameEnd.Lines => p.Sim.Lines,
                _ => p.Frags,
            };
            totals[p.Team] = totals.GetValueOrDefault(p.Team) + value;
        }
        return totals;
    }

    /// <summary>The goal in the unit the totals use: frags, points (the wire value is in thousands) or lines.</summary>
    public static int Goal(GameEnd end, int wireValue) => end == GameEnd.Points ? wireValue * 1000 : wireValue;

    /// <summary>The team in front, or -1 if no team has anything; <paramref name="draw"/> says the best two are level.</summary>
    public static int Leader(IReadOnlyDictionary<int, int> totals, out bool draw)
    {
        draw = false;
        var ordered = totals.OrderByDescending(t => t.Value).ThenBy(t => t.Key).ToList();
        if (ordered.Count == 0)
            return -1;
        draw = ordered.Count > 1 && ordered[1].Value == ordered[0].Value;
        return ordered[0].Value > 0 ? ordered[0].Key : -1;
    }
}
