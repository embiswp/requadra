using Quadra.Core.Input;
using Quadra.Core.Net;
using Quadra.Core.Rules;

namespace Quadra.Core.Game;

/// <summary>
/// What one machine plays in a network game: the loop <see cref="SinglePlayerGame"/> runs for one
/// board, but for the local player's slot of a <see cref="GameClient"/>. Landings, deaths and respawns
/// go out through the client; what the other machines do arrives through it and changes the boards.
/// <para>
/// A front end feeds <see cref="Keys"/> and calls <see cref="Update"/> every 10 ms. Line clears are not
/// animated yet: a landing is followed at once by the pause before the next piece.
/// </para>
/// </summary>
public sealed class NetworkGame
{
    private readonly GameClient _client;
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

    /// <summary>Frames the local player has been dead; 0 while alive.</summary>
    public int DeadFrames => _deadFrames;

    public bool IsDead { get; private set; }

    /// <summary>True from the first frame after the host's countdown has ended.</summary>
    public bool Started => _client.Started;

    public bool Paused => _client.Paused;

    /// <summary>Frames left of the countdown, or 0 once the game runs.</summary>
    public int Countdown => _client.DelayStart;

    /// <summary>Whether the front end should play the "landed" sound and so on; collected like <see cref="SinglePlayerGame.Events"/>.</summary>
    public List<GameEvent> Events { get; } = [];

    /// <summary>Advances one logic frame: takes in what the network delivered, then plays the local piece.</summary>
    public void Update()
    {
        _client.Pump();
        _client.Tick();

        if (!Started || Paused)
            return;
        _wasStarted = true;

        if (IsDead)
        {
            _deadFrames++;
            if (_deadFrames > RespawnLockFrames && AnyKeyDown())
                Respawn();
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
        _pauseFramesLeft = SinglePlayerGame.PauseFrames;
    }

    private bool AnyKeyDown() =>
        Enum.GetValues<GameAction>().Any(action => Keys.Get(Bindings[action]) != KeyFlags.None);

    private void Respawn()
    {
        _client.Respawn(Slot);
        IsDead = false;
        _deadFrames = 0;
        _pauseFramesLeft = 0;
        Player.Input?.BeginPiece();
    }

    /// <summary>The other players' boards, by slot, in the order they are drawn.</summary>
    public IEnumerable<RemotePlayer> Others => State.Players.Values.Where(p => p.Slot != Slot);

    /// <summary>True if the countdown ran and play started at some point (a front end can tell a game that never began).</summary>
    public bool HasStarted => _wasStarted;
}
