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
    private HostingSession? _hosting;
    private ClientSession? _session;
    private string? _chatText;       // the line being typed, or null
    private int _networkLevel;

    private bool NetworkActive => _session is not null;

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
            var gameHost = new GameHost(new HostRules
            {
                Name = $"{_settings.CurrentPlayer.Name}'s game", Seed = Environment.TickCount,
                GameEnd = fromMenu ? _settings.Rules.End : _options.GameEnd,
                GameEndValue = fromMenu ? _settings.Rules.EndValue : _options.GameEndValue,
                Survivor = fromMenu ? _settings.Rules.Survivor : _options.Survivor,
                ComboMin = (byte)(fromMenu ? _settings.Rules.ComboMin : 0),
            });
            try
            {
                _hosting = new HostingSession(gameHost, port, ListServerUri(), _http);
            }
            catch (System.Net.Sockets.SocketException e)
            {
                _flow.ReturnToLobby($"Could not host on port {port}: {e.Message}");
                return;
            }
            port = _hosting.Port;
            host = "127.0.0.1";
        }

        _unplayedMs = 0;
        _controls.Reset(_settings.CurrentPlayer);
        _session = new ClientSession(host, port, _settings.CurrentPlayer, _options.PlayerName, CreateNetworkGame);
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

    /// <summary>The list server the settings name, or null if none is set or it is not an address.</summary>
    private Uri? ListServerUri() =>
        _settings.ListServer.Length > 0 && Uri.TryCreate(_settings.ListServer, UriKind.Absolute, out var uri) ? uri : null;

    private void UpdateLobby()
    {
        if (_listing is { IsCompleted: true } done)
        {
            _listing = null;
            if (done.IsFaulted)
                _flow.Lobby.Status = "Could not fetch the list: " + done.Exception!.GetBaseException().Message;   // (also a failed LAN search)
            else if (done.IsCompletedSuccessfully)
                _flow.SetLobbyGames(done.Result);
        }

        if (_flow.TakeRefreshRequest() && _listing is null
            && Uri.TryCreate(_settings.ListServer, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            _listing = new QservClient(_http, uri).GetGamesAsync();
        else if (_flow.Lobby.ServerUrl.Length > 0 && !Uri.TryCreate(_settings.ListServer, UriKind.Absolute, out _))
            _flow.Lobby.Status = "The game list address is not a URL";

        if (_flow.TakeLanRequest() && _listing is null)
            _listing = LanDiscovery.FindAsync(LanDiscovery.UdpPort(TcpGameServer.DefaultPort), TimeSpan.FromSeconds(1.5));

        if (_flow.TakeNetworkRequest() is { } request)
            StartNetwork(request, fromMenu: true);
    }

    private void StopNetwork()
    {
        _session?.Stop();
        _session = null;
        _hosting?.Stop();
        _hosting = null;
    }

    /// <summary>The host has given the local player a place: make the game they play.</summary>
    private NetworkGame CreateNetworkGame(GameClient client, byte slot)
    {
        var settings = _settings.CurrentPlayer;
        _view.ShowShadow = settings.Shadow;
        _view.SmoothMovement = settings.Smooth;
        _networkLevel = 0;
        return new NetworkGame(client, slot, _controls.State, _controls.Reset(settings), settings.HorizontalSpeed, settings.ContinuousDown);
    }

    private void UpdateNetwork(KeyboardState keyboard, GameTime gameTime)
    {
        if (WasPressed(keyboard, Keys.Escape) && _chatText is not null)
        {
            _chatText = null;
        }
        else if (WasPressed(keyboard, Keys.Escape))
        {
            bool fromMenu = _fromMenu;
            StopNetwork();
            if (fromMenu)
                _flow.ReturnToLobby();
            else
                Exit();
            return;
        }

        var session = _session!;
        session.Update();
        if (session.Game is { } game && session.Stage is SessionStage.Waiting or SessionStage.Playing)
            PlayNetworkGame(game, session, keyboard, gameTime);
    }

    private void PlayNetworkGame(NetworkGame game, ClientSession session, KeyboardState keyboard, GameTime gameTime)
    {
        bool chatting = _chatText is not null;
        if (chatting)
        {
            if (WasPressed(keyboard, Keys.Back) && _chatText!.Length > 0)
                _chatText = _chatText[..^1];
            if (WasPressed(keyboard, Keys.Enter))
            {
                game.Say(_chatText!);
                _chatText = null;
            }
            _controls.State.ClearAll();
        }
        else if (WasPressed(keyboard, Keys.T))
        {
            _chatText = "";
        }
        else if (!game.Started && game.Countdown == GameHost.WaitingForStart && WasPressed(keyboard, Keys.Enter))
        {
            session.Client!.RequestPause();
        }

        if (!chatting)
            _controls.Feed(keyboard, _previousKeys);

        _unplayedMs = Math.Min(_unplayedMs + (int)gameTime.ElapsedGameTime.TotalMilliseconds, MaxCatchUpMs);
        while (_unplayedMs >= StepMs)
        {
            _unplayedMs -= StepMs;
            game.Update();
            _sounds.PlayEvents(game.Events, game.Player.Level);
        }

        session.GameMoved();
        if (game.Player.Level != _networkLevel && game.Animation is null)
        {
            _networkLevel = game.Player.Level;
            ShowLevel(_networkLevel);
        }
    }

    private void DrawNetwork()
    {
        if (_session is { Game: { } game, Stage: not SessionStage.Failed })
        {
            _view.Paint(game);
            _view.PaintRoom(game, _chatText);
            string? first = null, second = null;
            if (!game.Started && game.Countdown == GameHost.WaitingForStart)
                (first, second) = ("Enter starts the game", "T to chat");
            else if (!game.Started)
                first = (game.Countdown / 100 + 1).ToString();
            else if (game.Ended)
                (first, second) = ("Game over", game.Result());
            else if (game.IsDead)
                (first, second) = game.Survivor
                    ? ("Waiting for the next round", null)
                    : ("You are dead", game.DeadFrames > NetworkGame.RespawnLockFrames ? "Press a key to return" : null);
            if (first is not null)
                _view.PaintMessage(first, second, low: !game.Started);
        }
        else
        {
            _view.Overlay.Clear();
            _view.PaintMessage(_session!.Message);
        }
        _overlay.SetData(_view.Overlay.Pixels);
        _batch.Draw(_background.Picture, Vector2.Zero, Color.White);
        _batch.Draw(_overlay, Vector2.Zero, Color.White);
    }
}
