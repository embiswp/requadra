using Quadra.Core.Input;
using Quadra.Core.Net;
using Quadra.Core.Rules;

namespace Quadra.Core.Game;

/// <summary>
/// What one machine plays in a network game: the loop <see cref="SinglePlayerGame"/> runs for one
/// board, but for the local player's slot of a <see cref="GameClient"/>. Landings, deaths and respawns
/// go out through the client; what the other machines do arrives through it and changes the boards.
/// <para>
/// A front end feeds <see cref="Keys"/> and calls <see cref="Update"/> every 10 ms. After a landing the
/// local board plays the same clear animation as a single-player game (<see cref="Animation"/>) before the
/// next piece comes.
/// </para>
/// </summary>
public sealed class NetworkGame
{
    private readonly GameClient _client;
    private readonly List<AnimationFrame> _timeline = [];
    private int _timelineIndex;
    private int _pauseFramesLeft;
    private bool _wasStarted;

    /// <summary>Frames a dead player waits before a key press brings them back.</summary>
    public const int RespawnLockFrames = 100;

    private int _deadFrames;

    public NetworkGame(GameClient client, byte slot, KeyStates keys, KeyBindings bindings, int horizontalRepeat = 2, bool continuousDown = true)
    {
        _client = client;
        Slot = slot;
        Keys = keys;
        Bindings = bindings;
        Player.AttachInput(keys, bindings, horizontalRepeat, continuousDown);
        Player.RecordClearAnimation = true;
        client.Received += packet =>
        {
            if (packet is ChatPacket chat)
                Chat.Add(chat.Text);
        };
    }

    /// <summary>What was said since this machine joined.</summary>
    public ChatLog Chat { get; } = new();

    public const int MaxChatLength = 100;

    /// <summary>Says something to everyone, as "name: text"; the host sends it back, so it shows in <see cref="Chat"/> like the rest.</summary>
    public void Say(string text)
    {
        text = text.Trim();
        if (text.Length > MaxChatLength)
            text = text[..MaxChatLength];
        if (text.Length > 0)
            _client.Chat($"{Local.Name}: {text}");
    }

    public byte Slot { get; }
    public KeyStates Keys { get; }
    public KeyBindings Bindings { get; }

    public MultiplayerState State => _client.State ?? throw new InvalidOperationException("The host has not sent the game yet.");

    public RemotePlayer Local => State.Players[Slot];

    public PlayerSim Player => Local.Sim;

    /// <summary>The look of the local board during a line clear, or null when the live board should be drawn.</summary>
    public AnimationFrame? Animation => _timelineIndex < _timeline.Count ? _timeline[_timelineIndex] : null;

    /// <summary>True while a piece is in play (not during a clear, its pause, a death or before the start).</summary>
    public bool Falling => Started && !Paused && !Ended && !IsDead && _timelineIndex >= _timeline.Count && _pauseFramesLeft == 0;

    /// <summary>Frames left of the rising "level up" banner.</summary>
    public int LevelUpFrames { get; private set; }

    /// <summary>Frames the local player has been dead; 0 while alive.</summary>
    public int DeadFrames => _deadFrames;

    public bool IsDead { get; private set; }

    /// <summary>True from the first frame after the host's countdown has ended.</summary>
    public bool Started => _client.Started;

    public bool Paused => _client.Paused;

    /// <summary>The host ended the game: its end condition was met.</summary>
    public bool Ended => _client.Terminated;

    /// <summary>Who won, once <see cref="Ended"/>: "Team 2 (Bob) wins", "No winner" or "Drawn".</summary>
    public string Result()
    {
        var totals = GameGoals.TeamTotals(State, State.Rules.GameEnd);
        int team = GameGoals.Leader(totals, out bool draw);
        if (team < 0)
            return "No winner";
        var names = string.Join(", ", State.Players.Values.Where(p => p.Team == team).Select(p => p.Name));
        return draw ? "Drawn" : $"Team {team + 1} ({names}) wins";
    }

    /// <summary>Frames left of the countdown, or 0 once the game runs.</summary>
    public int Countdown => _client.DelayStart;

    /// <summary>Whether the front end should play the "landed" sound and so on; collected like <see cref="SinglePlayerGame.Events"/>.</summary>
    public List<GameEvent> Events { get; } = [];

    /// <summary>Advances one logic frame: takes in what the network delivered, then plays the local piece.</summary>
    public void Update()
    {
        _client.Pump();
        _client.Tick();

        if (!Started || Paused || Ended)
            return;
        _wasStarted = true;

        if (IsDead)
        {
            _deadFrames++;
            if (_deadFrames > RespawnLockFrames && AnyKeyDown())
                Respawn();
            return;
        }

        if (LevelUpFrames > 0)
            LevelUpFrames--;

        if (_timelineIndex < _timeline.Count)
        {
            _timelineIndex++;
            if (_timelineIndex < _timeline.Count)
                Emit(_timeline[_timelineIndex]);
            return;
        }
        if (_pauseFramesLeft > 0)
        {
            _pauseFramesLeft--;
            return;
        }

        int rotation = Player.Falling.Rotation;
        var result = Player.Frame();
        if (Player.Falling.Rotation != rotation)
            Events.Add(new GameEvent(GameEventKind.Rotated, Player.Falling.Column));
        if (result.Landed is not { } landed)
            return;

        int drop = LegacyScoring.DropPoints(Player.Falling.Frames);
        int levelBefore = Player.Level;
        Events.Add(new GameEvent(GameEventKind.Stamped, landed.X));
        _client.Stamp(Slot, landed.X, landed.Y, landed.Rotation, drop, Player.Falling.Frames);

        if (Player.SpawnBlocked)
        {
            Events.Add(new GameEvent(GameEventKind.GameOver));
            _client.Die(Slot);
            IsDead = true;
            _deadFrames = 0;
            return;
        }
        _timeline.Clear();
        _timeline.AddRange(ClearAnimation.Build(Player.LastClear, Player.Level > levelBefore ? Player.Level : 0));
        _timelineIndex = 0;
        _pauseFramesLeft = SinglePlayerGame.PauseFrames;
        if (_timeline.Count > 0)
            Emit(_timeline[0]);
    }

    /// <summary>Reports the events that belong to a frame that has just come on screen.</summary>
    private void Emit(AnimationFrame frame)
    {
        foreach (var e in frame.Events)
        {
            Events.Add(e);
            if (e.Kind == GameEventKind.LevelUp)
                LevelUpFrames = SinglePlayerGame.LevelUpBannerFrames;
        }
    }

    private bool AnyKeyDown() =>
        Enum.GetValues<GameAction>().Any(action => Keys.Get(Bindings[action]) != KeyFlags.None);

    private void Respawn()
    {
        _client.Respawn(Slot);
        IsDead = false;
        _deadFrames = 0;
        _pauseFramesLeft = 0;
        _timeline.Clear();
        Player.Input?.BeginPiece();
    }

    /// <summary>The other players' boards, by slot, in the order they are drawn.</summary>
    public IEnumerable<RemotePlayer> Others => State.Players.Values.Where(p => p.Slot != Slot);

    /// <summary>True if the countdown ran and play started at some point (a front end can tell a game that never began).</summary>
    public bool HasStarted => _wasStarted;
}
