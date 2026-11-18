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
    private const float MasterVolume = 0.6f;
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
    private SoundCues _cues = null!;
    private RenderTarget2D _screen = null!;      // the original's 640 x 480, scaled to the window
    private Texture2D _overlay = null!;          // painted board and texts, or the menu controls
    private Texture2D _pauseSign = null!;
    private Texture2D _pixel = null!;
    private Background _background = null!;

    private KeyboardState _previousKeys;
    private MouseState _previousMouse;
    private Screen _shownScreen = Screen.Main;
    private int _hoverIndex = -1;

    // The game in progress.
    private SinglePlayerGame? _game;
    private KeyStates _keys = new();
    private Keys[] _gameKeys = [];
    private int _shownLevel;
    private int _accumulatorMs;
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
        _cues = new SoundCues(new Random(), _assets.SampleRate);
        _screen = new RenderTarget2D(GraphicsDevice, SinglePlayerView.ScreenWidth, SinglePlayerView.ScreenHeight);
        _overlay = new Texture2D(GraphicsDevice, SinglePlayerView.ScreenWidth, SinglePlayerView.ScreenHeight);
        _pauseSign = _assets.ToTexture(_assets.PauseBanner);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);

        if (_options.Screen is { } name && Enum.TryParse<Screen>(name, ignoreCase: true, out var screen))
        {
            _flow.Show(screen == Screen.HighScores ? screen : screen);
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

        if (Pressed(keyboard, Keys.F11))
            ToggleFullscreen();

        if (_networkActive)
            UpdateNetwork(keyboard, gameTime);
        else if (_flow.Current == Screen.Playing)
            UpdatePlaying(keyboard, gameTime);
        else
            UpdateMenu(keyboard, mouse);

        if (_flow.Current == Screen.Lobby && !_networkActive)
            UpdateLobby();
        if (_flow.QuitRequested)
            Exit();
        if (_flow.Current != _shownScreen)
            OnScreenChanged();

        _previousKeys = keyboard;
        _previousMouse = mouse;

        _updates++;
        bool scriptDone = _script is { Count: 0 } && _scriptIdle++ > 4;
        if (_options.Script is not null ? scriptDone : _options.SmokeUpdates > 0 && _updates >= _options.SmokeUpdates + (_options.ScreenshotPath is null ? 0 : 1))
            Exit();
        base.Update(gameTime);
    }

    // ---- scripted input (for checking the program without a person) ------------------------------------

    private Queue<string>? _script;
    private int _scriptIdle;
    private Point _scriptMouse;

    /// <summary>
    /// The real keyboard and mouse, or in a scripted run one step of the script per update:
    /// <c>move x y</c> (screen coordinates of the 640 x 480 picture), <c>click</c>, <c>key Name</c>,
    /// <c>type text</c> and <c>wait n</c>. A key or click lasts one update, so each shows as a press.
    /// </summary>
    private (KeyboardState Keyboard, MouseState Mouse) ReadInput()
    {
        if (_options.Script is null)
            return (Keyboard.GetState(), Mouse.GetState());

        _script ??= new Queue<string>(_options.Script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var keys = new List<Keys>();
        bool click = false;
        if (_script.Count > 0)
        {
            string[] step = _script.Dequeue().Split(' ', 2, StringSplitOptions.TrimEntries);
            switch (step[0])
            {
                case "move":
                    var p = step[1].Split(' ');
                    _scriptMouse = ToWindow(int.Parse(p[0]), int.Parse(p[1]));
                    break;
                case "click": click = true; break;
                case "key": keys.Add(Enum.Parse<Keys>(step[1])); break;
                case "type":
                    foreach (char c in step[1])
                        OnTextInput(this, new TextInputEventArgs(c));
                    break;
                case "wait":
                    for (int n = int.Parse(step[1]) - 1; n > 0; n--)
                        _script = new Queue<string>(_script.Prepend("wait 1"));
                    break;
            }
        }
        return (new KeyboardState(keys.ToArray()),
            new MouseState(_scriptMouse.X, _scriptMouse.Y, 0, click ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
    }

    private Point ToWindow(int x, int y)
    {
        var r = FittedRectangle();
        return new Point(r.X + x * r.Width / SinglePlayerView.ScreenWidth, r.Y + y * r.Height / SinglePlayerView.ScreenHeight);
    }

    private void OnScreenChanged()
    {
        var previous = _shownScreen;
        _shownScreen = _flow.Current;
        _hoverIndex = -1;
        if (_flow.Current == Screen.Playing)
            StartGame();
        else if (previous == Screen.Playing)
            _game = null;
        if (_flow.Current == Screen.HighScores && _flow.HighlightedRank >= 0)
            _assets.Play(_cues.LevelUp(), MasterVolume);     // the original celebrates a new high score
    }

    private void UpdateMenu(KeyboardState keyboard, MouseState mouse)
    {
        var menu = _flow.Menu;
        var setup = _flow.Current == Screen.Setup ? _flow.Setup : null;

        // Pointer: hovering focuses (with a sound), a click activates.
        var point = ToScreen(mouse.X, mouse.Y);
        if (mouse.X != _previousMouse.X || mouse.Y != _previousMouse.Y)
        {
            menu.PointerMoved(point.X, point.Y);
            if (menu.Focus != _hoverIndex)
            {
                _hoverIndex = menu.Focus;
                if (menu.Focus >= 0)
                    _assets.Play(_cues.MenuHover(), MasterVolume);
            }
        }
        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
            && menu.PointerPressed(point.X, point.Y) is { } clicked)
            Choose(clicked);

        // Keyboard.
        if (setup is { Mode: SetupMode.CapturingKey })
        {
            foreach (var key in keyboard.GetPressedKeys().Where(k => !_previousKeys.IsKeyDown(k)))
            {
                setup.KeyPressed(key.ToString());
                break;      // one key at a time
            }
            return;
        }
        if (setup is { Mode: SetupMode.EditingName or SetupMode.EditingPassword })
        {
            if (Pressed(keyboard, Keys.Back))
                setup.Backspace();
            if (Pressed(keyboard, Keys.Enter))
                setup.Commit();
            if (Pressed(keyboard, Keys.Escape))
                _flow.Back();
            return;
        }

        if (_flow.Current == Screen.Lobby && _flow.Lobby.Edit != LobbyEdit.None)
        {
            if (Pressed(keyboard, Keys.Back))
                _flow.Lobby.Backspace();
            if (Pressed(keyboard, Keys.Enter))
                _flow.Back();       // Enter and Escape both end the typing; the text stays
            if (Pressed(keyboard, Keys.Escape))
                _flow.Back();
            return;
        }

        if (Pressed(keyboard, Keys.Down))
            Moved(menu, 1);
        if (Pressed(keyboard, Keys.Up))
            Moved(menu, -1);
        if (menu.FocusedItem is { } focused)
        {
            if (Pressed(keyboard, Keys.Left))
                _flow.Cycle(focused.Id, -1);
            if (Pressed(keyboard, Keys.Right))
                _flow.Cycle(focused.Id, 1);
            if (Pressed(keyboard, Keys.Enter) || Pressed(keyboard, Keys.Space))
                Choose(focused.Id);
        }
        if (Pressed(keyboard, Keys.Escape))
            _flow.Back();
    }

    private void Moved(MenuScreen menu, int direction)
    {
        menu.MoveFocus(direction);
        _hoverIndex = menu.Focus;
        _assets.Play(_cues.MenuHover(), MasterVolume);
    }

    /// <summary>Something was chosen: click sound (a tap sound for a value), then the flow decides.</summary>
    private void Choose(string id)
    {
        bool isValue = _flow.Current == Screen.Setup && id is "player" or "shadow" or "smooth" or "hspeed" or "vspeed" or "continuous"
                       || _flow.Current == Screen.Options && id == "language";
        _assets.Play(isValue ? _cues.MenuValue() : _cues.MenuClick(), MasterVolume);
        _flow.Activate(id);
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (_networkActive)
            TypeChat(e.Character);
        if (_flow is { Current: Screen.Lobby })
            _flow.Lobby.TypeCharacter(e.Character);
        if (_flow is { Current: Screen.Setup })
            _flow.Setup.TypeCharacter(e.Character);
    }

    /// <summary>The current player's keys as bindings; also remembers which keys to report to <see cref="_keys"/>.</summary>
    private KeyBindings CreateBindings()
    {
        var player = _settings.CurrentPlayer;
        var defaults = PlayerSettings.DefaultKeys();
        Keys Resolve(GameAction action) =>
            Enum.TryParse<Keys>(player.Keys.GetValueOrDefault(action, defaults[action]), out var key) ? key : Enum.Parse<Keys>(defaults[action]);

        _gameKeys = Enum.GetValues<GameAction>().Select(Resolve).Distinct().ToArray();
        _keys = new KeyStates();
        return new KeyBindings(
            (int)Resolve(GameAction.Left), (int)Resolve(GameAction.Right), (int)Resolve(GameAction.RotateLeft),
            (int)Resolve(GameAction.Down), (int)Resolve(GameAction.RotateRight), (int)Resolve(GameAction.RotateTwice),
            (int)Resolve(GameAction.Drop));
    }

    private void StartGame()
    {
        var player = _settings.CurrentPlayer;
        var bindings = CreateBindings();
        _game = new SinglePlayerGame(Environment.TickCount, _keys, bindings, player.HorizontalSpeed, player.VerticalSpeed,
            _options.StartLevel, player.ContinuousDown);
        _view.ShowShadow = player.Shadow;
        _view.SmoothMovement = player.Smooth;
        _paused = false;
        _gameOverMs = 0;
        _accumulatorMs = 0;
        _shownLevel = 0;
        ShowLevel(_game.DisplayedLevel);
    }

    private void UpdatePlaying(KeyboardState keyboard, GameTime gameTime)
    {
        var game = _game!;
        if (Pressed(keyboard, Keys.Escape))
        {
            _flow.Back();
            return;
        }
        if (Pressed(keyboard, Keys.P) && !game.IsOver)
        {
            _paused = !_paused;
            _assets.Play(_cues.Pause(), MasterVolume);
        }
        if (game.IsOver)
        {
            // As in the original, a death goes on to the high scores; the dead board shows for a moment first.
            _gameOverMs += (int)gameTime.ElapsedGameTime.TotalMilliseconds;
            if (_gameOverMs >= GameOverDelayMs || Pressed(keyboard, Keys.Enter) || Pressed(keyboard, Keys.Space))
                _flow.GameFinished(game.Player.Score, game.Player.Lines, game.Player.Level);
            return;
        }

        if (_paused)
            return;
        FeedKeys(keyboard);
        _accumulatorMs = Math.Min(_accumulatorMs + (int)gameTime.ElapsedGameTime.TotalMilliseconds, MaxCatchUpMs);
        while (_accumulatorMs >= StepMs)
        {
            _accumulatorMs -= StepMs;
            game.Update();
            PlayGameEvents(game);
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

    private void PlayGameEvents(SinglePlayerGame game)
    {
        var set = SoundCues.ForLevel(game.DisplayedLevel);
        foreach (var e in game.Events)
        {
            SoundCue? cue = e.Kind switch
            {
                GameEventKind.Rotated => _cues.Rotate(set, e.Column),
                GameEventKind.Stamped => _cues.Stamp(set, e.Column),
                GameEventKind.LineFlash => _cues.LineFlash(set, e.Combo),
                GameEventKind.BlocksFell => _cues.Fall(e.Value, e.Combo),
                GameEventKind.LevelUp => _cues.LevelUp(),
                GameEventKind.GameOver => _cues.Death(set),
                _ => null,
            };
            if (cue is { } c)
                _assets.Play(c, MasterVolume);
        }
        game.Events.Clear();
    }

    /// <summary>Reports key-down and key-up of the game's keys as events, the way an input layer should.</summary>
    private void FeedKeys(KeyboardState now)
    {
        foreach (var key in _gameKeys)
        {
            bool down = now.IsKeyDown(key), was = _previousKeys.IsKeyDown(key);
            if (down && !was)
                _keys.Down((int)key);
            else if (!down && was)
                _keys.Up((int)key);
        }

        // A smoke run of the game has no one at the keyboard: tap a few keys so every part of it runs.
        if (_options.SmokeUpdates > 0 && _options.Screen is null or "play")
            Autoplay();
        if (_options.FadeTest && _updates == 10)
            _game!.StartLevelChange(_game.DisplayedLevel + 1);
    }

    private void Autoplay()
    {
        int step = _updates % 40;
        int left = (int)Resolve(GameAction.Left), rotate = (int)Resolve(GameAction.RotateRight), drop = (int)Resolve(GameAction.Drop);
        if (step == 0) { _keys.Down(left); _keys.Up(left); }
        if (step == 10) { _keys.Down(rotate); _keys.Up(rotate); }
        if (step == 20) { _keys.Down(drop); }
    }

    private Keys Resolve(GameAction action) =>
        Enum.TryParse<Keys>(_settings.CurrentPlayer.Keys[action], out var key) ? key : Keys.Space;

    private bool Pressed(KeyboardState now, Keys key) => now.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

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
        if (_networkActive)
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
        _batch.Draw(_screen, FittedRectangle(), Color.White);
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

    private Rectangle FittedRectangle()
    {
        int w = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int h = GraphicsDevice.PresentationParameters.BackBufferHeight;
        float scale = Math.Min(w / (float)SinglePlayerView.ScreenWidth, h / (float)SinglePlayerView.ScreenHeight);
        int fw = (int)(SinglePlayerView.ScreenWidth * scale), fh = (int)(SinglePlayerView.ScreenHeight * scale);
        return new Rectangle((w - fw) / 2, (h - fh) / 2, fw, fh);
    }

    /// <summary>Window pixels to the original's 640 x 480 coordinates.</summary>
    private Point ToScreen(int x, int y)
    {
        var r = FittedRectangle();
        return new Point((x - r.X) * SinglePlayerView.ScreenWidth / Math.Max(r.Width, 1),
            (y - r.Y) * SinglePlayerView.ScreenHeight / Math.Max(r.Height, 1));
    }

    private void SaveScreenshot(string path)
    {
        using var stream = File.Create(path);
        _screen.SaveAsPng(stream, _screen.Width, _screen.Height);
    }
}
