using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quadra.Core.Assets;
using Quadra.Core.Game;
using Quadra.Core.Menus;
using Quadra.Core.Qserv;
using Quadra.Core.Net;
using Quadra.Core.Rendering;

namespace Quadra.Desktop;

/// <summary>
/// Playing over the network, started with <c>--host [port]</c> or <c>--join host[:port]</c>. There are no
/// menu screens for it yet: the window goes straight to the game, waits for the other players, and
/// Enter starts the countdown.
/// </summary>
public sealed partial class QuadraGame
{
    private enum NetworkStage { Connecting, Joining, Waiting, Playing, Failed }

    private TcpGameServer? _server;
    private TcpGameLink? _network;
    private Task<TcpGameLink>? _connecting;
    private NetworkGame? _netGame;
    private NetworkStage _stage;
    private bool _networkActive;
    private string _networkMessage = "";
    private int _networkLevel;

    private bool _fromMenu;
    private Task<IReadOnlyList<ListedGame>>? _listing;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Starts a network game: <c>--host</c>/<c>--join</c> from the command line, or a choice on the lobby screen.</summary>
    private void StartNetwork(NetworkRequest request, bool fromMenu)
    {
        _fromMenu = fromMenu;
        string host = request.Address;
        int port = request.Port;
        if (request.Host)
        {
            var gameHost = new GameHost(new HostRules { Name = $"{_settings.CurrentPlayer.Name}'s game", Seed = Environment.TickCount });
            try
            {
                _server = new TcpGameServer(gameHost, IPAddress.Any, port);
                _server.Start();
            }
            catch (System.Net.Sockets.SocketException e)
            {
                _server = null;
                _flow.ReturnToLobby($"Could not host on port {port}: {e.Message}");
                return;
            }
            port = _server.Port;
            host = "127.0.0.1";
            AnnounceHost(gameHost, port);
        }

        _networkActive = true;
        _netGame = null;
        _accumulatorMs = 0;
        CreateBindings();
        _stage = NetworkStage.Connecting;
        _networkMessage = $"Connecting to {host}:{port}...";
        _connecting = Task.Run(() => TcpGameLink.ConnectAsync(host, port));
        ShowLevel(1);
    }

    private void StartNetworkFromOptions()
    {
        var request = _options.Host is { } hostPort
            ? new NetworkRequest(true, "127.0.0.1", hostPort == "" ? TcpGameServer.DefaultPort : int.Parse(hostPort))
            : LobbyModel.ParseAddress(_options.Join!) ?? throw new ArgumentException("--join needs host or host:port");
        StartNetwork(request, fromMenu: false);
    }

    // ---- the lobby: the game list from the list server ---------------------------------------------------

    private HostAnnouncer? _announcer;

    /// <summary>A hosted game announces itself to the list server, if one is set.</summary>
    private void AnnounceHost(GameHost host, int port)
    {
        if (_settings.ListServer.Length > 0 && Uri.TryCreate(_settings.ListServer, UriKind.Absolute, out var uri))
            _announcer = new HostAnnouncer(host, new QservClient(_http, uri), port);
    }

    private void UpdateLobby()
    {
        if (_listing is { IsCompleted: true } done)
        {
            _listing = null;
            if (done.IsFaulted)
                _flow.Lobby.Status = "Could not fetch the list: " + done.Exception!.GetBaseException().Message;
            else if (done.IsCompletedSuccessfully)
                _flow.SetLobbyGames(done.Result);
        }

        if (_flow.TakeRefreshRequest() && _listing is null
            && Uri.TryCreate(_settings.ListServer, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            _listing = new QservClient(_http, uri).GetGamesAsync();
        else if (_flow.Lobby.ServerUrl.Length > 0 && !Uri.TryCreate(_settings.ListServer, UriKind.Absolute, out _))
            _flow.Lobby.Status = "The game list address is not a URL";

        if (_flow.TakeNetworkRequest() is { } request)
            StartNetwork(request, fromMenu: true);
    }

    private void StopNetwork()
    {
        _announcer?.DisposeAsync().AsTask().Wait(3000);
        _announcer = null;
        _netGame = null;
        if (_network is { } link)
        {
            link.Client.Leave();
            link.DisposeAsync().AsTask().Wait(1000);
        }
        _network = null;
        _server?.DisposeAsync().AsTask().Wait(1000);
        _server = null;
    }

    private void UpdateNetwork(KeyboardState keyboard, GameTime gameTime)
    {
        if (Pressed(keyboard, Keys.Escape))
        {
            StopNetwork();
            _networkActive = false;
            if (_fromMenu)
                _flow.ReturnToLobby();
            else
                Exit();
            return;
        }

        switch (_stage)
        {
            case NetworkStage.Connecting:
                if (_connecting!.IsFaulted)
                {
                    _networkMessage = "Could not connect: " + _connecting.Exception!.GetBaseException().Message;
                    _stage = NetworkStage.Failed;
                }
                else if (_connecting.IsCompletedSuccessfully)
                {
                    _network = _connecting.Result;
                    _network.Client.RequestJoin();
                    _stage = NetworkStage.Joining;
                    _networkMessage = "Joining...";
                }
                break;

            case NetworkStage.Joining:
                _network!.Client.Pump();
                if (_network.Client.State is not null)
                    RequestLocalPlayer();
                else if (_network.Closed.IsCompleted)
                    Fail("The host closed the connection.");
                break;

            case NetworkStage.Waiting or NetworkStage.Playing:
                UpdateNetworkGame(keyboard, gameTime);
                break;
        }
    }

    private void RequestLocalPlayer()
    {
        var client = _network!.Client;
        var settings = _settings.CurrentPlayer;
        string name = _options.PlayerName ?? (settings.Name.Length == 0 ? "Player" : settings.Name);
        var used = client.State!.Players.Values.Select(p => (int)p.Team).ToHashSet();
        byte team = (byte)Enumerable.Range(0, PacketDecoder.MaxTeams).FirstOrDefault(t => !used.Contains(t));

        client.PlayerAnswered += OnPlayerAnswered;
        client.RequestPlayer(new PlayerWantJoinPacket
        {
            Name = name, Team = team, Smooth = settings.Smooth ? 1 : 0, Shadow = settings.Shadow ? 1 : 0,
            HRepeat = settings.HorizontalSpeed, VRepeat = settings.VerticalSpeed,
            PlayerHash = MD5.HashData(Encoding.UTF8.GetBytes(name + Environment.MachineName)),
        });
        _stage = NetworkStage.Waiting;
        _networkMessage = "Waiting for the host's answer...";
    }

    private void OnPlayerAnswered(PlayerAcceptedPacket answer)
    {
        var client = _network!.Client;
        client.PlayerAnswered -= OnPlayerAnswered;
        if (answer.Accepted != JoinAnswer.Accepted)
        {
            Fail($"The game did not take the player: {answer.Accepted}.");
            return;
        }

        // Pump has applied the answer already, so the local player is in the replicated state.
        var settings = _settings.CurrentPlayer;
        _netGame = new NetworkGame(client, answer.Pos, _keys, CreateBindings(), settings.HorizontalSpeed, settings.ContinuousDown);
        _view.ShowShadow = settings.Shadow;
        _view.SmoothMovement = settings.Smooth;
        _networkLevel = 0;
    }

    private void Fail(string message)
    {
        _networkMessage = message;
        _stage = NetworkStage.Failed;
    }

    private void UpdateNetworkGame(KeyboardState keyboard, GameTime gameTime)
    {
        var client = _network!.Client;
        if (_netGame is null)
        {
            client.Pump();      // the answer to the join request is applied here
            return;
        }
        var game = _netGame;

        if (!game.Started && game.Countdown == GameHost.WaitingForStart && Pressed(keyboard, Keys.Enter))
            client.RequestPause();

        foreach (var key in _gameKeys)
        {
            bool down = keyboard.IsKeyDown(key), was = _previousKeys.IsKeyDown(key);
            if (down && !was)
                _keys.Down((int)key);
            else if (!down && was)
                _keys.Up((int)key);
        }

        _accumulatorMs = Math.Min(_accumulatorMs + (int)gameTime.ElapsedGameTime.TotalMilliseconds, MaxCatchUpMs);
        while (_accumulatorMs >= StepMs)
        {
            _accumulatorMs -= StepMs;
            game.Update();
            PlayNetworkEvents(game);
        }

        _stage = game.Started ? NetworkStage.Playing : NetworkStage.Waiting;
        if (game.Player.Level != _networkLevel)
        {
            _networkLevel = game.Player.Level;
            ShowLevel(_networkLevel);
        }
        if (_network.Closed.IsCompleted)
            Fail("The connection to the host ended.");
    }

    private void PlayNetworkEvents(NetworkGame game)
    {
        var set = SoundCues.ForLevel(game.Player.Level);
        foreach (var e in game.Events)
        {
            SoundCue? cue = e.Kind switch
            {
                GameEventKind.Rotated => _cues.Rotate(set, e.Column),
                GameEventKind.Stamped => _cues.Stamp(set, e.Column),
                GameEventKind.GameOver => _cues.Death(set),
                _ => null,
            };
            if (cue is { } c)
                _assets.Play(c, MasterVolume);
        }
        game.Events.Clear();
    }

    private void DrawNetwork()
    {
        if (_netGame is { } game)
        {
            _view.Paint(game);
            string? first = null, second = null;
            if (!game.Started && game.Countdown == GameHost.WaitingForStart)
                (first, second) = ($"{game.State.Players.Count} player(s)", "Enter starts the game");
            else if (!game.Started)
                first = (game.Countdown / 100 + 1).ToString();
            else if (game.IsDead)
                (first, second) = ("You are dead", game.DeadFrames > NetworkGame.RespawnLockFrames ? "Press a key to return" : null);
            if (first is not null)
                _view.PaintMessage(first, second);
        }
        else
        {
            _view.Overlay.Clear();
            _view.PaintMessage(_networkMessage);
        }
        _overlay.SetData(_view.Overlay.Pixels);
        _batch.Draw(_background.Picture, Vector2.Zero, Color.White);
        _batch.Draw(_overlay, Vector2.Zero, Color.White);
    }
}
