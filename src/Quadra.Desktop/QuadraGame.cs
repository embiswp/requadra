using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quadra.Core.Assets;
using Quadra.Core.Game;
using Quadra.Core.Input;
using Quadra.Core.Rendering;

namespace Quadra.Desktop;

/// <summary>
/// The window: turns the keyboard into <see cref="KeyStates"/> events, steps a
/// <see cref="SinglePlayerGame"/> every 10 ms, draws it with the original art and plays the original
/// sounds. All rules live in Quadra.Core; this class only connects them to MonoGame.
/// </summary>
public sealed class QuadraGame : Game
{
    private const int MaxCatchUpMs = 300;
    private const int StepMs = 1000 / SinglePlayerGame.FramesPerSecond;
    private const float MasterVolume = 0.6f;

    // The game's keys are plain integers; here they are the MonoGame key codes.
    private static readonly KeyBindings Bindings = new(
        left: (int)Keys.Left, right: (int)Keys.Right, rotateLeft: (int)Keys.Z, down: (int)Keys.Down,
        rotateRight: (int)Keys.Up, rotateTwice: (int)Keys.X, drop: (int)Keys.Space);

    private static readonly Keys[] GameKeys = [Keys.Left, Keys.Right, Keys.Z, Keys.Down, Keys.Up, Keys.X, Keys.Space];

    private readonly GraphicsDeviceManager _graphics;
    private readonly int _smokeUpdates;
    private readonly string? _screenshotPath;
    private readonly int _startLevel;
    private readonly bool _pauseAtEnd;
    private SpriteBatch _batch = null!;
    private AssetLibrary _assets = null!;
    private SinglePlayerView _view = null!;
    private SoundCues _cues = null!;
    private RenderTarget2D _screen = null!;      // the original's 640 x 480, scaled to the window
    private Texture2D _overlay = null!;          // the painted board, next pieces and scores
    private Texture2D _pauseSign = null!;
    private Background _background = null!;
    private KeyboardState _previous;
    private KeyStates _keys = new();
    private SinglePlayerGame _game;
    private int _shownLevel;
    private int _accumulatorMs;
    private int _updates;
    private bool _paused;

    /// <param name="smokeUpdates">If above zero, play automatically for this many window updates, then exit.</param>
    /// <param name="screenshotPath">In smoke mode, save the last frame to this PNG file.</param>
    /// <param name="startLevel">Level to start at (to look at other levels' colours and sounds).</param>
    /// <param name="pauseAtEnd">In smoke mode, show the pause sign in the last frame.</param>
    public QuadraGame(int smokeUpdates = 0, string? screenshotPath = null, int startLevel = 1, bool pauseAtEnd = false)
    {
        _smokeUpdates = smokeUpdates;
        _screenshotPath = screenshotPath;
        _startLevel = startLevel;
        _pauseAtEnd = pauseAtEnd;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = SinglePlayerView.ScreenWidth * 3 / 2,
            PreferredBackBufferHeight = SinglePlayerView.ScreenHeight * 3 / 2,
        };
        IsMouseVisible = true;
        IsFixedTimeStep = false;      // the logic runs on its own 10 ms clock, see Update
        Window.AllowUserResizing = true;
        Window.Title = "Quadra";
        _game = NewGame();
    }

    public SinglePlayerGame CurrentGame => _game;

    private SinglePlayerGame NewGame()
    {
        _keys = new KeyStates();
        _shownLevel = 0;
        return new SinglePlayerGame(Environment.TickCount, _keys, Bindings, startLevel: _startLevel);
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _assets = new AssetLibrary(GraphicsDevice);
        _view = new SinglePlayerView(_assets.TextFont, _assets.NumberFont) { LevelUpBanner = _assets.LevelUpBanner };
        _cues = new SoundCues(new Random(), _assets.SampleRate);
        _screen = new RenderTarget2D(GraphicsDevice, SinglePlayerView.ScreenWidth, SinglePlayerView.ScreenHeight);
        _overlay = new Texture2D(GraphicsDevice, SinglePlayerView.ScreenWidth, SinglePlayerView.ScreenHeight);
        _pauseSign = _assets.ToTexture(_assets.PauseBanner);
        ShowLevel(_game.DisplayedLevel);
    }

    protected override void UnloadContent()
    {
        _assets.Dispose();
        base.UnloadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.Escape))
            Exit();
        if (Pressed(keyboard, Keys.P) && !_game.IsOver)
        {
            _paused = !_paused;
            _assets.Play(_cues.Pause(), MasterVolume);
        }
        if (Pressed(keyboard, Keys.Enter) && _game.IsOver)
            _game = NewGame();

        if (!_paused)
        {
            FeedKeys(keyboard);
            _accumulatorMs = Math.Min(_accumulatorMs + (int)gameTime.ElapsedGameTime.TotalMilliseconds, MaxCatchUpMs);
            while (_accumulatorMs >= StepMs)
            {
                _accumulatorMs -= StepMs;
                _game.Update();
                PlayEvents();
            }
            if (_game.DisplayedLevel != _shownLevel)
                ShowLevel(_game.DisplayedLevel);
        }

        _previous = keyboard;
        if (_pauseAtEnd && _smokeUpdates > 0 && _updates + 1 >= _smokeUpdates)
            _paused = true;
        if (_smokeUpdates > 0 && ++_updates >= _smokeUpdates + (_screenshotPath is null ? 0 : 1))
            Exit();
        base.Update(gameTime);
    }

    /// <summary>The level sets the background, the block colours and which sounds are used.</summary>
    private void ShowLevel(int level)
    {
        _shownLevel = level;
        _background = _assets.BackgroundFor(level);
        _view.SetPalette(_background.Palette);
    }

    private void PlayEvents()
    {
        var set = SoundCues.ForLevel(_game.DisplayedLevel);
        foreach (var e in _game.Events)
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
        _game.Events.Clear();
    }

    /// <summary>Reports key-down and key-up as events, the way an input layer should.</summary>
    private void FeedKeys(KeyboardState now)
    {
        foreach (var key in GameKeys)
        {
            bool down = now.IsKeyDown(key), was = _previous.IsKeyDown(key);
            if (down && !was)
                _keys.Down((int)key);
            else if (!down && was)
                _keys.Up((int)key);
        }

        // Smoke mode has no one at the keyboard: tap a few keys so every part of the game runs.
        if (_smokeUpdates > 0)
            Autoplay();
    }

    private void Autoplay()
    {
        int step = _updates % 40;
        if (step == 0) { _keys.Down((int)Keys.Left); _keys.Up((int)Keys.Left); }
        if (step == 10) { _keys.Down((int)Keys.Up); _keys.Up((int)Keys.Up); }
        if (step == 20) { _keys.Down((int)Keys.Space); }
    }

    private bool Pressed(KeyboardState now, Keys key) => now.IsKeyDown(key) && !_previous.IsKeyDown(key);

    protected override void Draw(GameTime gameTime)
    {
        // Paint the 640 x 480 picture, then scale it to the window keeping its proportions.
        _view.Paint(_game);
        if (_game.IsOver)
            _view.PaintMessage("Game Over", "Press Enter");
        _overlay.SetData(_view.Overlay.Pixels);

        GraphicsDevice.SetRenderTarget(_screen);
        _batch.Begin(samplerState: SamplerState.PointClamp);
        _batch.Draw(_background.Picture, Vector2.Zero, Color.White);
        _batch.Draw(_overlay, Vector2.Zero, Color.White);
        if (_paused)
            _batch.Draw(_pauseSign, new Vector2((SinglePlayerView.ScreenWidth - _pauseSign.Width) / 2, (SinglePlayerView.ScreenHeight - _pauseSign.Height) / 2), Color.White);
        _batch.End();
        GraphicsDevice.SetRenderTarget(null);

        if (_screenshotPath is not null && _smokeUpdates > 0 && _updates >= _smokeUpdates)
            SaveScreenshot(_screenshotPath);

        GraphicsDevice.Clear(Color.Black);
        _batch.Begin(samplerState: SamplerState.LinearClamp);
        _batch.Draw(_screen, FittedRectangle(), Color.White);
        _batch.End();
        base.Draw(gameTime);
    }

    private Rectangle FittedRectangle()
    {
        int w = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int h = GraphicsDevice.PresentationParameters.BackBufferHeight;
        float scale = Math.Min(w / (float)SinglePlayerView.ScreenWidth, h / (float)SinglePlayerView.ScreenHeight);
        int fw = (int)(SinglePlayerView.ScreenWidth * scale), fh = (int)(SinglePlayerView.ScreenHeight * scale);
        return new Rectangle((w - fw) / 2, (h - fh) / 2, fw, fh);
    }

    private void SaveScreenshot(string path)
    {
        using var stream = File.Create(path);
        _screen.SaveAsPng(stream, _screen.Width, _screen.Height);
    }
}
