using Quadra.Core.Assets;
using Quadra.Core.Game;
using Quadra.Core.Rules;

namespace Quadra.Core.Rendering;

/// <summary>
/// Paints the single-player screen of the original (<c>Pane_startgame</c>, <c>Canvas::blit_back</c>
/// and <c>blit_bloc</c>) into a 640 x 480 overlay with a transparent background. The overlay goes on
/// top of the level's background picture, whose middle frame holds the board.
/// </summary>
public sealed class SinglePlayerView
{
    public const int ScreenWidth = 640;
    public const int ScreenHeight = 480;

    /// <summary>The board's top-left corner on the screen: the middle one of the three frames.</summary>
    public const int BoardX = 223;
    public const int BoardY = 37;
    public const int BoardWidth = 10 * BlockPainter.CellSize;
    public const int BoardHeight = 20 * BlockPainter.CellSize;

    private readonly PixelBuffer _board = new(BoardWidth, BoardHeight);
    private readonly LegacyFont _text;
    private readonly LegacyFont _numbers;
    private readonly LegacyText _strings;
    private BlockColors? _colors;
    private Rgb[]? _palette;

    public SinglePlayerView(LegacyFont text, LegacyFont numbers, LegacyText strings)
    {
        _text = text;
        _numbers = numbers;
        _strings = strings;
    }

    public PixelBuffer Overlay { get; } = new(ScreenWidth, ScreenHeight);

    /// <summary>Show where the piece would land (the player's "shadow" setting).</summary>
    public bool ShowShadow { get; set; } = true;

    /// <summary>Let the piece glide smoothly (the player's "smooth" setting) instead of jumping from cell to cell.</summary>
    public bool SmoothMovement { get; set; } = true;

    /// <summary>The "level up" banner sprite, if loaded.</summary>
    public PixelBuffer? LevelUpBanner { get; set; }

    /// <summary>Sets the palette of the current level's background (it holds the block colours).</summary>
    public void SetPalette(Rgb[] palette)
    {
        _palette = palette;
        _colors = new BlockColors(palette);
    }

    public void Paint(SinglePlayerGame game) =>
        PaintPlayer(game.Player, game.Animation, game.Phase == GamePhase.Falling, game.LevelUpFrames, clearStatistics: true);

    /// <summary>
    /// Paints a network game: the local board as in a single-player game, and the other players' boards
    /// small, in the right frame (at most <see cref="MaxShownOpponents"/>), each with its name.
    /// </summary>
    public void Paint(NetworkGame game)
    {
        var local = game.Player;
        PaintPlayer(local, null, !game.IsDead && game.Started && !game.Paused, 0, clearStatistics: false);

        int shown = 0;
        foreach (var other in game.Others)
        {
            if (shown == MaxShownOpponents)
                break;
            int x = OpponentX + shown % 2 * OpponentColumnStep;
            int y = OpponentY + shown / 2 * OpponentRowStep;
            PaintOpponent(other, x, y);
            shown++;
        }
    }

    public const int MaxShownOpponents = 6;
    private const int OpponentX = 440, OpponentY = 40, OpponentColumnStep = 90, OpponentRowStep = 145;
    private const int OpponentCell = BlockPainter.SmallCellSize;

    private void PaintOpponent(RemotePlayer other, int x, int y)
    {
        var board = other.Sim.Board;
        for (int row = Board.FirstClearableRow; row < Board.Rows; row++)
            for (int col = Board.FirstColumn; col < Board.EndColumn; col++)
            {
                if (!board.IsOccupied(row, col))
                    continue;
                byte cell = board.Cell(row, col);
                BlockPainter.DrawSmall(Overlay, x + (col - Board.FirstColumn) * OpponentCell,
                    y + (row - Board.FirstClearableRow) * OpponentCell, cell & 15, cell >> 4, _colors!);
            }

        uint border = PixelBuffer.Pack(_palette![210]);
        int width = 10 * OpponentCell, height = 20 * OpponentCell;
        Overlay.HLine(y - 1, x - 1, width + 2, border);
        Overlay.HLine(y + height, x - 1, width + 2, border);
        Overlay.VLine(x - 1, y, height, border);
        Overlay.VLine(x + width, y, height, border);

        string name = other.Name.Length > 9 ? other.Name[..9] : other.Name;
        _text.Draw(Overlay, name, x, y + height + 3, White);
        _text.Draw(Overlay, other.Sim.Score.ToString(), x, y + height + 3 + _text.Height, new Rgb(200, 200, 200));
    }

    private void PaintPlayer(PlayerSim player, AnimationFrame? animation, bool showPiece, int levelUpFrames, bool clearStatistics)
    {
        if (_colors is null || _palette is null)
            throw new InvalidOperationException("Call SetPalette first.");

        Overlay.Clear();
        _board.Clear();

        IBoardView board = animation?.Board ?? player.Board;
        PaintCells(board);

        if (showPiece)
        {
            var piece = player.Falling;
            // The landing shadow first, then the piece (which slides smoothly in both directions).
            if (ShowShadow)
                PaintPiece(piece.Piece, piece.Rotation, (piece.Column - Board.FirstColumn) * BlockPainter.CellSize,
                    (piece.RestingRow() - Board.FirstClearableRow) * BlockPainter.CellSize, BlockColors.ShadowColor);
            if (SmoothMovement)
                PaintPiece(piece.Piece, piece.Rotation, piece.X >> 4, piece.Y >> 4, piece.Piece);
            else
                PaintPiece(piece.Piece, piece.Rotation, (piece.Column - Board.FirstColumn) * BlockPainter.CellSize,
                    (piece.Row - Board.FirstClearableRow) * BlockPainter.CellSize, piece.Piece);
        }

        if (animation is { FlashColor: not 0 } flash)
        {
            uint color = PixelBuffer.Pack(_palette[flash.FlashColor]);
            foreach (int row in flash.FlashRows)
                for (int i = 0; i < BlockPainter.CellSize; i++)
                    _board.HLine((row - Board.FirstClearableRow) * BlockPainter.CellSize + i, 0, BoardWidth - 1, color);
        }

        if (levelUpFrames > 0 && LevelUpBanner is { } banner)
            _board.Blit(banner, 10, levelUpFrames - 30);

        Overlay.Blit(_board, BoardX, BoardY);
        PaintNext(player);
        PaintScores(player);
        PaintPieceStatistics(player);
        if (clearStatistics)
            PaintClearStatistics(player);
    }

    private void PaintCells(IBoardView board)
    {
        Span<byte> to = stackalloc byte[4];
        for (int row = Board.FirstClearableRow; row < Board.Rows; row++)
            for (int col = Board.FirstColumn; col < Board.EndColumn; col++)
            {
                if (!board.IsOccupied(row, col))
                    continue;
                byte cell = board.Cell(row, col);
                to[0] = board.Cell(row, col - 1);
                to[1] = board.Cell(row - 1, col);
                to[2] = board.Cell(row, col + 1);
                to[3] = board.Cell(row + 1, col);
                BlockPainter.DrawCell(_board, (col - Board.FirstColumn) * BlockPainter.CellSize,
                    (row - Board.FirstClearableRow) * BlockPainter.CellSize, cell & 15, cell >> 4, _colors!, to);
            }
    }

    private void PaintPiece(int piece, int rotation, int x, int y, int color) =>
        PaintPiece(_board, piece, rotation, x, y, color, _colors!);

    /// <summary>Draws a piece's four cells at a pixel position (<c>Bloc::draw</c>).</summary>
    public static void PaintPiece(PixelBuffer target, int piece, int rotation, int x, int y, int color, BlockColors colors)
    {
        Span<byte> to = stackalloc byte[4];
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
            {
                byte t = Pieces.Shapes[piece, rotation, j, i];
                if (t == 0)
                    continue;
                to[0] = i > 0 ? Pieces.Shapes[piece, rotation, j, i - 1] : (byte)0;
                to[1] = j > 0 ? Pieces.Shapes[piece, rotation, j - 1, i] : (byte)0;
                to[2] = i < 3 ? Pieces.Shapes[piece, rotation, j, i + 1] : (byte)0;
                to[3] = j < 3 ? Pieces.Shapes[piece, rotation, j + 1, i] : (byte)0;
                BlockPainter.DrawCell(target, x + i * BlockPainter.CellSize, y + j * BlockPainter.CellSize, t & 15, color, colors, to);
            }
    }

    private static void PaintSmallPiece(PixelBuffer target, int piece, int x, int y, BlockColors colors)
    {
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
            {
                byte t = Pieces.Shapes[piece, 0, j, i];
                if (t != 0)
                    BlockPainter.DrawSmall(target, x + i * BlockPainter.SmallCellSize, y + j * BlockPainter.SmallCellSize, t & 15, piece, colors);
            }
    }

    /// <summary>The three upcoming pieces in the strip above the board (<c>Zone_next</c>, <c>Zone_small_next</c>).</summary>
    private void PaintNext(PlayerSim player)
    {
        var upcoming = player.Upcoming;
        PaintPiece(Overlay, upcoming[0], 0, BoardX + 60, 0 - BlockPainter.CellSize, upcoming[0], _colors!);
        PaintSmallPiece(Overlay, upcoming[1], BoardX + 32, 8 - BlockPainter.SmallCellSize, _colors!);
        PaintSmallPiece(Overlay, upcoming[2], BoardX + 5, 2 - BlockPainter.SmallCellSize, _colors!);
    }

    private static readonly Rgb White = new(255, 255, 255);

    private void PaintScores(PlayerSim player)
    {
        int x = BoardX - 5;
        _text.Draw(Overlay, _strings.Get(LegacyText.ScoreLabel), x, 403, White);
        NumberField(BoardX + 55, 403, 120, player.Score);
        _text.Draw(Overlay, _strings.Get(LegacyText.LinesLabel), x, 422, White);
        NumberField(BoardX + 55, 422, 65, player.LinesThisRun);
        _text.Draw(Overlay, _strings.Get(LegacyText.LevelLabel), x, 460, White);
        NumberField(BoardX + 55, 460, 35, player.Level);
    }

    /// <summary>The left frame: how many pieces of each kind have been placed (<c>Pane_blockinfo</c>).</summary>
    private void PaintPieceStatistics(PlayerSim player)
    {
        const int x = 24, y = 37, step = 2 * BlockPainter.CellSize + 2;
        for (int i = 0; i < Pieces.Count; i++)
        {
            PaintPiece(Overlay, i, 0, x, y + 2 + step * i, i, _colors!);
            NumberField(x + 75, y + 30 + step * i, 50, player.PieceCounts[i]);
        }
        _text.Draw(Overlay, _strings.Get(LegacyText.Total), x + 19, y + 300, White);
        NumberField(x + 75, y + 300, 50, player.Stamps);
    }

    /// <summary>The right frame: how many moves cleared 1, 2, 3 ... lines at once (<c>Pane_comboinfo</c>).</summary>
    private void PaintClearStatistics(PlayerSim player)
    {
        const int x = 428 + 24, y = 37 + 15, step = 20;
        for (int i = 0; i < 15; i++)
        {
            string name = _strings.Get(i < 14 ? LegacyText.FirstClearName + i : LegacyText.MoreClears);
            _text.Draw(Overlay, name, x, y + step * i, White);
            NumberField(x + 75, y + step * i, 50, player.ClearCounts[i]);
        }
        _text.Draw(Overlay, _strings.Get(LegacyText.Total), x, y + step * 15 + 10, White);
        NumberField(x + 75, y + step * 15 + 10, 50, player.Lines);
    }

    /// <summary>
    /// A framed box with a right-aligned number (<c>Zone_text_field</c>): a one pixel border in palette
    /// entry 210, a near-black inside (entry 0), the number in the number font.
    /// </summary>
    private void NumberField(int x, int y, int width, int value)
    {
        int height = _text.Height + 2;
        uint border = PixelBuffer.Pack(_palette![210]);
        uint inside = PixelBuffer.Pack(_palette[0]);
        Overlay.HLine(y, x, width, border);
        Overlay.HLine(y + height - 1, x, width, border);
        Overlay.VLine(x, y + 1, height - 2, border);
        Overlay.VLine(x + width - 1, y + 1, height - 2, border);
        for (int row = 1; row < height - 1; row++)
            Overlay.HLine(y + row, x + 1, width - 2, inside);

        string text = value.ToString();
        _numbers.Draw(Overlay, text, x + 2 + (width - _numbers.Measure(text) - 3), y + 2, White);
    }

    /// <summary>Centres one or two lines of text over the board (pause and game over).</summary>
    public void PaintMessage(string first, string? second = null)
    {
        int centre = BoardX + BoardWidth / 2;
        int y = BoardY + BoardHeight / 2 - _text.Height;
        _text.Draw(Overlay, first, centre - _text.Measure(first) / 2, y, White);
        if (second is not null)
            _text.Draw(Overlay, second, centre - _text.Measure(second) / 2, y + _text.Height + 6, new Rgb(200, 200, 200));
    }
}
