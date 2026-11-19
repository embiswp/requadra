using System.Security.Cryptography;
using System.Text;
using Quadra.Core.Game;
using Quadra.Core.Settings;

namespace Quadra.Core.Net;

public enum SessionStage { Connecting, Joining, Waiting, Playing, Failed }

/// <summary>
/// A player's way into a network game, step by step: connect to the host, join the game, ask for a place
/// for the local player, and then play. The window calls <see cref="Update"/> every frame until
/// <see cref="Game"/> exists and shows <see cref="Message"/> meanwhile.
/// </summary>
public sealed class ClientSession
{
    private readonly Task<TcpGameLink> _connecting;
    private readonly PlayerSettings _player;
    private readonly string _playerName;
    private readonly Func<GameClient, byte, NetworkGame> _createGame;
    private TcpGameLink? _link;

    /// <param name="playerName">The name to play under instead of the player's own, or null.</param>
    /// <param name="createGame">Makes the game for the place the host gave the local player (it needs the window's keys).</param>
    public ClientSession(string host, int port, PlayerSettings player, string? playerName, Func<GameClient, byte, NetworkGame> createGame)
    {
        _player = player;
        _playerName = playerName ?? (player.Name.Length == 0 ? "Player" : player.Name);
        _createGame = createGame;
        Message = $"Connecting to {host}:{port}...";
        _connecting = Task.Run(() => TcpGameLink.ConnectAsync(host, port));
    }

    public SessionStage Stage { get; private set; } = SessionStage.Connecting;

    /// <summary>What to tell the player while there is no game to look at yet, or why it failed.</summary>
    public string Message { get; private set; }

    /// <summary>The game of the local player; null until the host has given them a place.</summary>
    public NetworkGame? Game { get; private set; }

    /// <summary>The connection to the host, once it is made.</summary>
    public GameClient? Client => _link?.Client;

    /// <summary>Moves the join along; once there is a <see cref="Game"/> this does nothing, and the window plays it and calls <see cref="GameMoved"/>.</summary>
    public void Update()
    {
        switch (Stage)
        {
            case SessionStage.Connecting:
                if (_connecting.IsFaulted)
                    Fail("Could not connect: " + _connecting.Exception!.GetBaseException().Message);
                else if (_connecting.IsCompletedSuccessfully)
                    Connected(_connecting.Result);
                break;

            case SessionStage.Joining:
                _link!.Client.Pump();
                if (_link.Client.State is not null)
                    AskForPlace();
                else if (_link.Closed.IsCompleted)
                    Fail("The host closed the connection.");
                break;

            case SessionStage.Waiting when Game is null:
                _link!.Client.Pump();      // the answer to the request for a place is applied here
                break;
        }
    }

    /// <summary>The window has played a frame of <see cref="Game"/>: note whether it has started and whether the host is still there.</summary>
    public void GameMoved()
    {
        Stage = Game!.Started ? SessionStage.Playing : SessionStage.Waiting;
        if (_link!.Closed.IsCompleted)
            Fail("The connection to the host ended.");
    }

    /// <summary>Says goodbye to the host and closes the connection.</summary>
    public void Stop()
    {
        Game = null;
        if (_link is null && _connecting.IsCompletedSuccessfully)
            _link = _connecting.Result;
        if (_link is { } link)
        {
            link.Client.Leave();
            link.DisposeAsync().AsTask().Wait(1000);
        }
        _link = null;
    }

    private void Connected(TcpGameLink link)
    {
        _link = link;
        link.Client.RequestJoin();
        Stage = SessionStage.Joining;
        Message = "Joining...";
    }

    private void AskForPlace()
    {
        var client = _link!.Client;
        var teamsTaken = client.State!.Players.Values.Select(p => (int)p.Team).ToHashSet();
        byte team = _player.Team >= 0
            ? (byte)_player.Team
            : (byte)Enumerable.Range(0, PacketDecoder.MaxTeams).FirstOrDefault(t => !teamsTaken.Contains(t));

        client.PlayerAnswered += OnPlaceAnswered;
        client.RequestPlayer(new PlayerWantJoinPacket
        {
            Name = _playerName, Team = team, Handicap = _player.Handicap,
            Smooth = _player.Smooth ? 1 : 0, Shadow = _player.Shadow ? 1 : 0,
            HRepeat = _player.HorizontalSpeed, VRepeat = _player.VerticalSpeed,
            PlayerHash = MD5.HashData(Encoding.UTF8.GetBytes(_playerName + Environment.MachineName)),
        });
        Stage = SessionStage.Waiting;
        Message = "Waiting for the host's answer...";
    }

    private void OnPlaceAnswered(PlayerAcceptedPacket answer)
    {
        var client = _link!.Client;
        client.PlayerAnswered -= OnPlaceAnswered;
        if (answer.Accepted != JoinAnswer.Accepted)
            Fail($"The game did not take the player: {answer.Accepted}.");
        else
            Game = _createGame(client, answer.Pos);     // Pump has applied the answer already, so the local player is in the replicated state
    }

    private void Fail(string message)
    {
        Message = message;
        Stage = SessionStage.Failed;
    }
}
