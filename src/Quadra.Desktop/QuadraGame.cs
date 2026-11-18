using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quadra.Core.Game;
using Quadra.Core.Input;
using Quadra.Core.Rules;

namespace Quadra.Desktop;

/// <summary>
/// The window: turns the keyboard into <see cref="KeyStates"/> events, steps a
/// <see cref="SinglePlayerGame"/> every 10 ms and draws it. All rules live in Quadra.Core.
/// </summary>
public sealed class QuadraGame : Game
{
    private const int Cell = 24;
    private const int VisibleRows = 20;
    private const int FirstVisibleRow = Board.FirstClearableRow;   // rows above are the spawn area
    private const int BoardX = 24;
    private const int BoardY = 56;
    private const int BoardWidth = 10 * Cell;
    private const int BoardHeight = VisibleRows * Cell;
    private const int PanelX = BoardX + BoardWidth + 28;
    private const int MaxCatchUpMs = 300;
    private const int StepMs = 1000 / SinglePlayerGame.FramesPerSecond;

    private static readonly Color[] PieceColors =
    [
        new(240, 220, 60), new(80, 200, 90), new(230, 70, 70), new(70, 120, 230),
        new(240, 150, 50), new(80, 210, 230), new(180, 90, 220),
    ];

    // The game's keys are plain integers; here they are the MonoGame key codes.
    private static readonly KeyBindings Bindings = new(
        left: (int)Keys.Left, right: (int)Keys.Right, rotateLeft: (int)Keys.Z, down: (int)Keys.Down,
        rotateRight: (int)Keys.Up, rotateTwice: (int)Keys.X, drop: (int)Keys.Space);

    private static readonly Keys[] GameKeys = [Keys.Left, Keys.Right, Keys.Z, Keys.Down, Keys.Up, Keys.X, Keys.Space];

    private readonly GraphicsDeviceManager _graphics;
    private readonly int _smokeUpdates;
    private readonly string? _screenshotPath;
    private SpriteBatch _batch = null!;
    private Texture2D _pixel = null!;
    private KeyboardState _previous;
    private KeyStates _keys = new();
    private SinglePlayerGame _game;
    private int _accumulatorMs;
    private int _updates;
    private bool _paused;

    /// <param name="smokeUpdates">If above zero, play automatically for this many window updates, then exit.</param>
    /// <param name="screenshotPath">In smoke mode, save the last frame to this PNG file.</param>
    public QuadraGame(int smokeUpdates = 0, string? screenshotPath = null)
    {
        _smokeUpdates = smokeUpdates;
        _screenshotPath = screenshotPath;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = PanelX + 160,
            PreferredBackBufferHeight = BoardY + BoardHeight + 24,
        };
        IsMouseVisible = true;
        IsFixedTimeStep = false;      // the logic runs on its own 10 ms clock, see Update
        Window.Title = "Quadra";
        _game = NewGame();
    }

    public SinglePlayerGame CurrentGame => _game;

    private SinglePlayerGame NewGame()
    {
        _keys = new KeyStates();
        return new SinglePlayerGame(Environment.TickCount, _keys, Bindings);
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.Escape))
            Exit();
        if (Pressed(keyboard, Keys.P) && !_game.IsOver)
            _paused = !_paused;
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
            }
        }

        _previous = keyboard;
        if (_smokeUpdates > 0 && ++_updates >= _smokeUpdates + (_screenshotPath is null ? 0 : 1))
            Exit();
        base.Update(gameTime);
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
        if (_screenshotPath is not null && _smokeUpdates > 0 && _updates >= _smokeUpdates)
            SaveScreenshot(_screenshotPath);

        DrawScene();
        base.Draw(gameTime);
    }

    private void SaveScreenshot(string path)
    {
        int width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        using var target = new RenderTarget2D(GraphicsDevice, width, height);
        GraphicsDevice.SetRenderTarget(target);
        DrawScene();
        GraphicsDevice.SetRenderTarget(null);
        using var stream = File.Create(path);
        target.SaveAsPng(stream, width, height);
    }

    private void DrawScene()
    {
        GraphicsDevice.Clear(new Color(18, 20, 28));
        _batch.Begin(samplerState: SamplerState.PointClamp);

        PixelFont.Draw(_batch, _pixel, "QUADRA", BoardX, 16, 5, Color.White);
        DrawBoard();
        DrawPanel();
        if (_game.IsOver)
            DrawOverlay("GAME OVER", "PRESS ENTER");
        else if (_paused)
            DrawOverlay("PAUSED", "PRESS P");

        _batch.End();
    }

    private void DrawBoard()
    {
        Fill(BoardX - 4, BoardY - 4, BoardWidth + 8, BoardHeight + 8, new Color(60, 64, 80));
        Fill(BoardX, BoardY, BoardWidth, BoardHeight, new Color(8, 9, 14));

        var board = _game.Player.Board;
        for (int row = FirstVisibleRow; row < Board.Rows; row++)
            for (int col = Board.FirstColumn; col < Board.EndColumn; col++)
                if (board.IsOccupied(row, col))
                    DrawCell(BoardX + (col - Board.FirstColumn) * Cell, BoardY + (row - FirstVisibleRow) * Cell,
                        board.Cell(row, col), Cell);

        if (_game.IsOver)
            return;

        // The falling piece moves smoothly: its row is a fraction, 288 vertical units to a row.
        var piece = _game.Player.Falling;
        float top = piece.Y / 288f + Board.FirstClearableRow - FirstVisibleRow;
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
            {
                byte shape = Pieces.Shapes[piece.Piece, piece.Rotation, j, i];
                if (shape == 0)
                    continue;
                int x = BoardX + (piece.Column + i - Board.FirstColumn) * Cell;
                int y = BoardY + (int)((top + j) * Cell);
                DrawCell(x, y, (byte)(shape + (piece.Piece << 4)), Cell, clipTop: BoardY);
            }
    }

    private void DrawPanel()
    {
        var player = _game.Player;
        int y = BoardY;
        foreach (var (label, value) in new[] { ("SCORE", player.Score), ("LINES", player.Lines), ("LEVEL", player.Level) })
        {
            PixelFont.Draw(_batch, _pixel, label, PanelX, y, 3, new Color(150, 160, 190));
            PixelFont.Draw(_batch, _pixel, value.ToString(), PanelX, y + 24, 4, Color.White);
            y += 72;
        }

        PixelFont.Draw(_batch, _pixel, "NEXT", PanelX, y, 3, new Color(150, 160, 190));
        y += 28;
        foreach (int piece in player.Upcoming)
        {
            const int small = 12;
            for (int j = 0; j < 4; j++)
                for (int i = 0; i < 4; i++)
                {
                    byte shape = Pieces.Shapes[piece, 0, j, i];
                    if (shape != 0)
                        DrawCell(PanelX + i * small, y + j * small, (byte)(shape + (piece << 4)), small);
                }
            y += 4 * small + 6;
        }
    }

    private void DrawOverlay(string title, string hint)
    {
        Fill(BoardX, BoardY + BoardHeight / 2 - 60, BoardWidth, 120, new Color(0, 0, 0, 200));
        PixelFont.Draw(_batch, _pixel, title, BoardX + (BoardWidth - PixelFont.Measure(title, 5)) / 2, BoardY + BoardHeight / 2 - 36, 5, Color.White);
        PixelFont.Draw(_batch, _pixel, hint, BoardX + (BoardWidth - PixelFont.Measure(hint, 3)) / 2, BoardY + BoardHeight / 2 + 16, 3, new Color(150, 160, 190));
    }

    /// <summary>
    /// Draws one block. The low four bits of a cell mark the sides that are NOT joined to a
    /// neighbouring block of the same piece; joined sides run flush so pieces look connected.
    /// </summary>
    private void DrawCell(int x, int y, byte cell, int size, int clipTop = int.MinValue)
    {
        var color = PieceColors[(cell >> 4) % PieceColors.Length];
        int inset = Math.Max(1, size / 12);
        int left = (cell & 1) != 0 ? inset : 0, top = (cell & 2) != 0 ? inset : 0;
        int right = (cell & 4) != 0 ? inset : 0, bottom = (cell & 8) != 0 ? inset : 0;
        FillClipped(x + left, y + top, size - left - right, size - top - bottom, color, clipTop);
        // a lighter top-left edge gives the blocks a little depth
        if ((cell & 2) != 0)
            FillClipped(x + left, y + top, size - left - right, Math.Max(1, size / 8), Color.Lerp(color, Color.White, 0.35f), clipTop);
    }

    private void Fill(int x, int y, int w, int h, Color color) => _batch.Draw(_pixel, new Rectangle(x, y, w, h), color);

    /// <summary>Fills a rectangle but never above <paramref name="clipTop"/> (pieces enter from above the board).</summary>
    private void FillClipped(int x, int y, int w, int h, Color color, int clipTop)
    {
        if (y < clipTop)
        {
            h -= clipTop - y;
            y = clipTop;
        }
        if (h > 0 && w > 0)
            Fill(x, y, w, h, color);
    }
}
