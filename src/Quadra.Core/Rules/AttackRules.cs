namespace Quadra.Core.Rules;

/// <summary>Lines one scoring move sends, before the receiver's handicap is taken into account.</summary>
/// <param name="Clean">Sent because the board was cleared; goes out before the ordinary lines.</param>
public readonly record struct OutgoingLines(bool Clean, int Count, int Combo);

/// <summary>
/// How many garbage rows a clear sends (<c>Canvas::give_line</c> and <c>Net_list::send</c> in
/// legacy/source/canvas.cc and net_list.cc, net versions below 23).
/// </summary>
public static class AttackRules
{
    /// <summary>
    /// The attacks of one move, in the order they are sent. A clean board sends (1 + lines) / 2 rows; the
    /// move itself sends lines - 1 once it reaches <paramref name="comboMin"/> lines. Zero-row attacks are left out.
    /// </summary>
    public static IEnumerable<OutgoingLines> For(ClearEvent clear, int comboMin)
    {
        int depth = clear.Lines;
        if (depth == 0)
            yield break;

        if (clear.Clean && (1 + depth) / 2 > 0)
            yield return new OutgoingLines(true, (1 + depth) / 2, clear.Combo);

        if (depth - 1 > 0 && depth >= comboMin)
            yield return new OutgoingLines(false, depth - 1, clear.Combo);
    }

    /// <summary>
    /// What reaches a receiver: when the receiver is more handicapped than the sender it gets
    /// (difference / 2 + 1) times as many rows and as much combo, the combo capped at 8. The result
    /// is rows and combo.
    /// </summary>
    public static (int Count, int Combo) ForReceiver(OutgoingLines attack, int senderHandicap, int receiverHandicap)
    {
        int count = attack.Count, combo = attack.Combo;
        int multiplier = receiverHandicap > senderHandicap ? receiverHandicap - senderHandicap : 0;
        count += (count * multiplier + 1) / 2;
        combo += (combo * multiplier + 1) / 2;
        if (multiplier != 0 && combo > 8)
            combo = 8;
        return (count, combo);
    }
}
