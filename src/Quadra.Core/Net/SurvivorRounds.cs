using Quadra.Core.Game;

namespace Quadra.Core.Net;

/// <summary>
/// The host's side of survivor games (<c>Net_list::check_first_frag</c>): when one team is left the survivors
/// step aside, then everyone waits for the others, a new seed goes out and all restart together.
/// The host calls <see cref="Check"/> every frame.
/// </summary>
internal sealed class SurvivorRounds(MultiplayerState state, Action<Packet> broadcast)
{
    /// <summary>Whether at least two teams have been alive in this round, so that one left alone means a win.</summary>
    private bool _contested;

    /// <summary>Where the game is in its round change; <see cref="RoundState.Last"/> while a round runs.</summary>
    public RoundState SyncPoint { get; private set; } = RoundState.Last;

    /// <summary>Rounds that have finished.</summary>
    public int Rounds { get; private set; }

    /// <summary>Where the seed of a new round comes from.</summary>
    public Func<uint> NewSeed { get; set; } = () => (uint)Random.Shared.NextInt64(0, 1L << 32);

    /// <summary>
    /// Moves the round change along. Returns whether the game may end now: anywhere in a game that is not
    /// survivor, and in a survivor game when a round has just ended (or in any frame of a time-limited one).
    /// </summary>
    public bool Check()
    {
        if (!state.Rules.Survivor)
            return true;
        bool mayEnd = state.Rules.GameEnd == GameEnd.Time;
        var players = state.Players.Values.ToList();

        if (players.Count > 0 && players.All(p => p.Round == RoundState.WaitForWinner))
        {
            MoveTo(RoundState.WaitForRestart);
            mayEnd = true;
        }
        if (players.Count > 0 && players.All(p => p.Round == RoundState.WaitForRestart))
        {
            if (SyncPoint == RoundState.WaitForRestart)
                ReseedEveryone();
            MoveTo(RoundState.Playing);
        }
        if (players.Count == 0 || players.All(p => p.Round == RoundState.Playing))
            MoveTo(RoundState.Last);

        if (SyncPoint == RoundState.Last)
            WatchForRoundEnd(players);
        return mayEnd;
    }

    // A round is on. It is over when the players left alive are all of one team and others are waiting to play.
    private void WatchForRoundEnd(List<RemotePlayer> players)
    {
        var teamsAlive = players.Where(p => p.Alive).Select(p => p.Team).Distinct().ToList();
        if (teamsAlive.Count > 1)
        {
            _contested = true;
        }
        else if (_contested)
        {
            broadcast(new ChatPacket(-1, -1, teamsAlive.Count == 0 ? "Nobody wins the round" : $"Team {teamsAlive[0] + 1} wins the round")
                { Id = (byte)PacketType.Chat });
            MoveTo(RoundState.WaitForWinner);
            Rounds++;
            _contested = false;
        }
    }

    private void ReseedEveryone()
    {
        var seed = new ServerRandomPacket(NewSeed()) { Id = (byte)PacketType.ServerRandom };
        state.Apply(seed);
        broadcast(seed);
    }

    private void MoveTo(RoundState point)
    {
        if (SyncPoint == point)
            return;
        SyncPoint = point;
        broadcast(new StatePacket((byte)point) { Id = (byte)PacketType.ServerState, Slot = 255 });
    }
}
