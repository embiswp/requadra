using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quadra.Core.Assets;
using Quadra.Core.Game;
using Quadra.Core.Input;
using Quadra.Core.Menus;
using Quadra.Core.Rendering;
using Quadra.Core.Settings;

namespace Quadra.Desktop;

/// <summary>
/// The window. It turns mouse, keyboard and text input into calls on <see cref="AppFlow"/> (the menu
/// structure) and on a <see cref="SinglePlayerGame"/>, and draws what they report with the original art
/// and sounds. All rules and screen logic live in Quadra.Core; this class only connects them to MonoGame.
/// </summary>
public sealed partial class QuadraGame : Game
{
    private const int MaxCatchUpMs = 300;
    private const int StepMs = 1000 / SinglePlayerGame.FramesPerSecond;
    private const int GameOverDelayMs = 2500;

    private readonly StartupOptions _options;
    private readonly GraphicsDeviceManager _graphics;
    private readonly string _settingsPath;
    private readonly string _scoresPath;
    private readonly GameSettings _settings;
    private readonly HighScoreTable _scores;

    private SpriteBatch _batch = null!;
    private AssetLibrary _assets = null!;
    private AppFlow _flow = null!;
    private SinglePlayerView _view = null!;
    private MenuPainter _menus = null!;
    private GameSounds _sounds = null!;
    private MenuInput _menuInput = null!;
    private ScriptedInput? _script;
    private RenderTarget2D _screen = null!;      // the original's 640 x 480, scaled to the window
    private Texture2D _overlay = null!;          // painted board and texts, or the menu controls
    private Texture2D _pauseSign = null!;
    private Texture2D _pixel = null!;
    private Background _background = null!;

    private KeyboardState _previousKeys;
    private MouseState _previousMouse;
    private Screen _shownScreen = Screen.Main;

    // The game in progress.
    private SinglePlayerGame? _game;
    private readonly PlayerControls _controls = new();
    private int _shownLevel;
    private int _unplayedMs;           // time that has passed but not yet been played as 10 ms game steps
    private int _updates;
    private bool _paused;
    private bool _fullscreen;
    private int _gameOverMs;                      // how long the dead board has been showing

    public QuadraGame(StartupOptions options)
    {
        _options = options;
        string dataDirectory = options.DataDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quadra");
        _settingsPath = Path.Combine(dataDirectory, "settings.json");
        _scoresPath = Path.Combine(dataDirectory, "highscores.json");
        _settings = SettingsStore.Load(_settingsPath);
        _scores = HighScoreTable.Load(_scoresPath);
        if (options.Language is { } language)
            _settings.Language = Math.Clamp(language, 0, GameSettings.LanguageCount - 1);

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = SinglePlayerView.ScreenWidth * 3 / 2,
            PreferredBackBufferHeight = SinglePlayerView.ScreenHeight * 3 / 2,
        };
        IsMouseVisible = true;
        IsFixedTimeStep = false;      // the game logic runs on its own 10 ms clock, see Update
        Window.AllowUserResizing = true;
        Window.Title = "Quadra";
        Window.TextInput += OnTextInput;
    }

    /// <summary>What a smoke run reports when it finishes.</summary>
    public string SmokeSummary
    {
        get
        {
            var player = _settings.CurrentPlayer;
            string settings = $"player {_settings.SetupPlayer + 1} \"{player.Name}\", shadow {player.Shadow}, smooth {player.Smooth}, " +
                $"speeds {player.HorizontalSpeed}/{player.VerticalSpeed}, continuous {player.ContinuousDown}, " +
                $"keys {string.Join(",", player.Keys.Select(k => $"{k.Key}={k.Value}"))}";
            string scores = string.Join(",", _scores.Entries.Select(e => $"{e.Name}:{e.Score}"));
            return _game is { } game
                ? $"smoke run finished: frame {game.Frame}, stamps {game.Player.Stamps}, score {game.Player.Score}, phase {game.Phase}"
                : $"smoke run finished: screen {_flow.Current}, language {_flow.Language}, {settings}, scores [{scores}]";
        }
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _assets = new AssetLibrary(GraphicsDevice);
        _flow = new AppFlow(_settings, _scores, file => _assets.ImageSize(file), _assets.TextFor,
            () => SaveQuietly(() => SettingsStore.Save(_settingsPath, _settings)),
            () => SaveQuietly(() => _scores.Save(_scoresPath)));
        _view = new SinglePlayerView(_assets.TextFont, _assets.NumberFont, _flow.Text) { LevelUpBanner = _assets.LevelUpBanner };
        _menus = new MenuPainter(_assets.TextFont, _assets.NumberFont, _assets.Sprite);
        _sounds = new GameSounds(_assets, new SoundCues(new Random(), _assets.SampleRate));
        _menuInput = new MenuInput(_flow, _sounds);
        _screen = new RenderTarget2D(GraphicsDevice, SinglePlayerView.ScreenWidth, SinglePlayerView.ScreenHeight);
        _overlay = new Texture2D(GraphicsDevice, SinglePlayerView.ScreenWidth, SinglePlayerView.ScreenHeight);
        _pauseSign = _assets.ToTexture(_assets.PauseBanner);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);

        if (_options.Screen is { } name && Enum.TryParse<Screen>(name, ignoreCase: true, out var screen))
        {
            _flow.Show(screen);
            if (screen == Screen.Playing)
                StartGame();
            if (_options.Focus is { } focus)
                _flow.Menu.FocusId(focus);
        }
        else if (_options.Screen == "scores")
        {
            _flow.Show(Screen.HighScores);
        }
        _shownScreen = _flow.Current;
        if (_options.Host is not null || _options.Join is not null)
            StartNetworkFromOptions();
    }

    protected override void UnloadContent()
    {
        StopNetwork();
        _assets.Dispose();
        base.UnloadContent();
    }

    // ---- update ----------------------------------------------------------------------------------------

    protected override void Update(GameTime gameTime)
    {
        var (keyboard, mouse) = ReadInput();

        if (WasPressed(keyboard, Keys.F11))
            ToggleFullscreen();

        if (NetworkActive)
            UpdateNetwork(keyboard, gameTime);
        else if (_flow.Current == Screen.Playing)
            UpdatePlaying(keyboard, gameTime);
        else
            _menuInput.Update(keyboard, _previousKeys, mouse, _previousMouse, Fit.ToPicture(mouse.X, mouse.Y));

        if (_flow.Current == Screen.Lobby && !NetworkActive)
            UpdateLobby();
        if (_flow.QuitRequested)
            Exit();
        if (_flow.Current != _shownScreen)
            OnScreenChanged();

        _previousKeys = keyboard;
        _previousMouse = mouse;

        _updates++;
        if (_script is not null ? _script.IsDone : _options.SmokeUpdates > 0 && _updates >= _options.SmokeUpdates + (_options.ScreenshotPath is null ? 0 : 1))
            Exit();
        base.Update(gameTime);
    }

    /// <summary>The real keyboard and mouse, or in a scripted run what the script does in this update.</summary>
    private (KeyboardState Keyboard, MouseState Mouse) ReadInput()
    {
        if (_options.Script is null)
            return (Keyboard.GetState(), Mouse.GetState());
        _script ??= new ScriptedInput(_options.Script, (x, y) => Fit.ToWindow(x, y), c => OnTextInput(this, new TextInputEventArgs(c)));
        return _script.Next();
    }

    private void OnScreenChanged()
    {
        var previous = _shownScreen;
        _shownScreen = _flow.Current;
        _menuInput.ScreenChanged();
        if (_flow.Current == Screen.Playing)
            StartGame();
        else if (previous == Screen.Playing)
            _game = null;
        if (_flow.Current == Screen.HighScores && _flow.HighlightedRank >= 0)
            _sounds.NewHighScore();
    }

    private void TypeChat(char c)
    {
        if (_chatText is not null && c >= ' ' && c <= '~' && _chatText.Length < NetworkGame.MaxChatLength)
            _chatText += c;
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (NetworkActive)
            TypeChat(e.Character);
        if (_flow is { Current: Screen.Lobby })
            _flow.Lobby.TypeCharacter(e.Character);
        if (_flow is { Current: Screen.Setup })
            _flow.Setup.TypeCharacter(e.Character);
    }

    private void StartGame()
    {
        var player = _settings.CurrentPlayer;
        var bindings = _controls.Reset(player);
        _game = new SinglePlayerGame(Environment.TickCount, _controls.State, bindings, player.HorizontalSpeed, player.VerticalSpeed,
            _options.StartLevel, player.ContinuousDown);
        _view.ShowShadow = player.Shadow;
        _view.SmoothMovement = player.Smooth;
        _paused = false;
        _gameOverMs = 0;
        _unplayedMs = 0;
        _shownLevel = 0;
        ShowLevel(_game.DisplayedLevel);
    }

    private void UpdatePlaying(KeyboardState keyboard, GameTime gameTime)
    {
        var game = _game!;
        if (WasPressed(keyboard, Keys.Escape))
        {
            _flow.Back();
            return;
        }
        if (WasPressed(keyboard, Keys.P) && !game.IsOver)
        {
            _paused = !_paused;
            _sounds.Pause();
        }
        if (game.IsOver)
        {
            // As in the original, a death goes on to the high scores; the dead board shows for a moment first.
            _gameOverMs += (int)gameTime.ElapsedGameTime.TotalMilliseconds;
            if (_gameOverMs >= GameOverDelayMs || WasPressed(keyboard, Keys.Enter) || WasPressed(keyboard, Keys.Space))
                _flow.GameFinished(game.Player.Score, game.Player.Lines, game.Player.Level);
            return;
        }

        if (_paused)
            return;
        _controls.Feed(keyboard, _previousKeys);
        PlaySmokeInputs();
        _unplayedMs = Math.Min(_unplayedMs + (int)gameTime.ElapsedGameTime.TotalMilliseconds, MaxCatchUpMs);
        while (_unplayedMs >= StepMs)
        {
            _unplayedMs -= StepMs;
            game.Update();
            _sounds.PlayEvents(game.Events, game.DisplayedLevel);
        }
        if (game.DisplayedLevel != _shownLevel)
            ShowLevel(game.DisplayedLevel);
    }

    /// <summary>The level sets the background, the block colours and which sounds are used.</summary>
    private void ShowLevel(int level)
    {
        _shownLevel = level;
        _background = _assets.BackgroundFor(level);
        _view.SetPalette(_background.Palette);
    }

    /// <summary>A smoke run has no one at the keyboard: tap a few keys so every part of the game runs.</summary>
    private void PlaySmokeInputs()
    {
        if (_options.SmokeUpdates > 0 && _options.Screen is null or "play")
        {
            int step = _updates % 40;
            if (step == 0)
                _controls.Tap(GameAction.Left);
            if (step == 10)
                _controls.Tap(GameAction.RotateRight);
            if (step == 20)
                _controls.State.Down((int)_controls.KeyFor(GameAction.Drop));
        }
        if (_options.FadeTest && _updates == 10)
            _game!.StartLevelChange(_game.DisplayedLevel + 1);
    }

    private bool WasPressed(KeyboardState now, Keys key) => now.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

    private void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        if (_fullscreen)
        {
            _graphics.PreferredBackBufferWidth = GraphicsDevice.Adapter.CurrentDisplayMode.Width;
            _graphics.PreferredBackBufferHeight = GraphicsDevice.Adapter.CurrentDisplayMode.Height;
        }
        else
        {
            _graphics.PreferredBackBufferWidth = SinglePlayerView.ScreenWidth * 3 / 2;
            _graphics.PreferredBackBufferHeight = SinglePlayerView.ScreenHeight * 3 / 2;
        }
        _graphics.IsFullScreen = _fullscreen;
        _graphics.ApplyChanges();
    }

    private static void SaveQuietly(Action save)
    {
        try
        {
            save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not save: {e.Message}");
        }
    }

    // ---- drawing ---------------------------------------------------------------------------------------

    protected override void Draw(GameTime gameTime)
    {
        // Paint the 640 x 480 picture, then scale it to the window keeping its proportions.
        GraphicsDevice.SetRenderTarget(_screen);
        _batch.Begin(samplerState: SamplerState.PointClamp);
        if (NetworkActive)
            DrawNetwork();
        else if (_flow.Current == Screen.Playing && _game is not null)
            DrawGame(_game);
        else
            DrawMenu(gameTime);
        _batch.End();
        GraphicsDevice.SetRenderTarget(null);

        if (_options.ScreenshotPath is not null && _options.SmokeUpdates > 0 && _updates >= _options.SmokeUpdates)
            SaveScreenshot(_options.ScreenshotPath);

        GraphicsDevice.Clear(Color.Black);
        _batch.Begin(samplerState: SamplerState.LinearClamp);
        _batch.Draw(_screen, Fit.Area, Color.White);
        _batch.End();
        base.Draw(gameTime);
    }

    private void DrawGame(SinglePlayerGame game)
    {
        _view.Paint(game);
        _overlay.SetData(_view.Overlay.Pixels);

        _batch.Draw(_background.Picture, Vector2.Zero, Color.White);
        _batch.Draw(_overlay, Vector2.Zero, Color.White);
        if (game.Brightness < 1f)      // the original fades its palette; this darkens the whole picture
            _batch.Draw(_pixel, new Rectangle(0, 0, SinglePlayerView.ScreenWidth, SinglePlayerView.ScreenHeight), Color.Black * (1f - game.Brightness));
        if (_paused)
            _batch.Draw(_pauseSign, new Vector2((SinglePlayerView.ScreenWidth - _pauseSign.Width) / 2, (SinglePlayerView.ScreenHeight - _pauseSign.Height) / 2), Color.White);
    }

    private void DrawMenu(GameTime gameTime)
    {
        int language = _flow.Language;
        string file;
        (string File, int X, int Y)? extra = null;
        switch (_flow.Current)
        {
            case Screen.Setup: file = MenuLayouts.SetupBackground(language); break;
            case Screen.Options:
            case Screen.Lobby: file = MenuLayouts.TextScreenBackground; break;
            case Screen.HighScores:
                file = MenuLayouts.HighScoreBackground;
                extra = MenuLayouts.HighScoreOverlay(language);
                break;
            default:
                file = MenuLayouts.MainBackground;
                extra = MenuLayouts.MainOverlay(language);
                break;
        }

        var picture = _assets.Picture(file);
        _menus.SetPalette(picture.Palette);
        bool caretOn = (int)(gameTime.TotalGameTime.TotalMilliseconds / 500) % 2 == 0;
        _menus.Paint(_flow, caretOn);
        _overlay.SetData(_menus.Overlay.Pixels);

        _batch.Draw(picture.Picture, Vector2.Zero, Color.White);
        // The French pictures replace the English text: they are copied over the background opaquely.
        if (extra is { } e)
            _batch.Draw(_assets.Picture(e.File).Picture, new Vector2(e.X, e.Y), Color.White);
        _batch.Draw(_overlay, Vector2.Zero, Color.White);
    }

    private PictureFit Fit =>
        PictureFit.In(GraphicsDevice.PresentationParameters.BackBufferWidth, GraphicsDevice.PresentationParameters.BackBufferHeight);

    private void SaveScreenshot(string path)
    {
        using var stream = File.Create(path);
        _screen.SaveAsPng(stream, _screen.Width, _screen.Height);
    }
}
