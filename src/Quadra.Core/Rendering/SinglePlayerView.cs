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
    private BlockColors? _colors;
    private Rgb[]? _palette;

    public SinglePlayerView(LegacyFont text, LegacyFont numbers)
    {
        _text = text;
        _numbers = numbers;
    }

    public PixelBuffer Overlay { get; } = new(ScreenWidth, ScreenHeight);

    /// <summary>The "level up" banner sprite, if loaded.</summary>
    public PixelBuffer? LevelUpBanner { get; set; }

    /// <summary>Sets the palette of the current level's background (it holds the block colours).</summary>
    public void SetPalette(Rgb[] palette)
    {
        _palette = palette;
        _colors = new BlockColors(palette);
    }

    public void Paint(SinglePlayerGame game)
    {
        if (_colors is null || _palette is null)
            throw new InvalidOperationException("Call SetPalette first.");

        Overlay.Clear();
        _board.Clear();

        var player = game.Player;
        var animation = game.Animation;
        IBoardView board = animation?.Board ?? player.Board;
        PaintCells(board);

        if (game.Phase == GamePhase.Falling)
        {
            var piece = player.Falling;
            // The landing shadow first, then the piece (which slides smoothly in both directions).
            PaintPiece(piece.Piece, piece.Rotation, (piece.Column - Board.FirstColumn) * BlockPainter.CellSize,
                (piece.RestingRow() - Board.FirstClearableRow) * BlockPainter.CellSize, BlockColors.ShadowColor);
            PaintPiece(piece.Piece, piece.Rotation, piece.X >> 4, piece.Y >> 4, piece.Piece);
        }

        if (animation is { FlashColor: not 0 } flash)
        {
            uint color = PixelBuffer.Pack(_palette[flash.FlashColor]);
            foreach (int row in flash.FlashRows)
                for (int i = 0; i < BlockPainter.CellSize; i++)
                    _board.HLine((row - Board.FirstClearableRow) * BlockPainter.CellSize + i, 0, BoardWidth - 1, color);
        }

        if (game.LevelUpFrames > 0 && LevelUpBanner is { } banner)
            _board.Blit(banner, 10, game.LevelUpFrames - 30);

        Overlay.Blit(_board, BoardX, BoardY);
        PaintNext(player);
        PaintScores(player);
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

    private void PaintScores(PlayerSim player)
    {
        var white = new Rgb(255, 255, 255);
        int x = BoardX - 5;
        _text.Draw(Overlay, "Score", x, 403, white);
        _numbers.Draw(Overlay, player.Score.ToString(), BoardX + 55, 403, white);
        _text.Draw(Overlay, "Lines", x, 422, white);
        _numbers.Draw(Overlay, player.LinesThisRun.ToString(), BoardX + 55, 422, white);
        _text.Draw(Overlay, "Level", x, 460, white);
        _numbers.Draw(Overlay, player.Level.ToString(), BoardX + 55, 460, white);
    }

    /// <summary>Centres one or two lines of text over the board (pause and game over).</summary>
    public void PaintMessage(string first, string? second = null)
    {
        var white = new Rgb(255, 255, 255);
        int centre = BoardX + BoardWidth / 2;
        int y = BoardY + BoardHeight / 2 - _text.Height;
        _text.Draw(Overlay, first, centre - _text.Measure(first) / 2, y, white);
        if (second is not null)
            _text.Draw(Overlay, second, centre - _text.Measure(second) / 2, y + _text.Height + 6, new Rgb(200, 200, 200));
    }
}
