namespace Quadra.Core.Rendering;

/// <summary>
/// Paints the blocks the way the original does: no sprites, just 18 x 18 cells with a bevel made of
/// shade lines on the sides that are not joined to a neighbour (<c>raw_draw_bloc</c>,
/// <c>raw_draw_bloc_corner</c> and <c>raw_small_draw_bloc</c> in <c>legacy/source/quadra.cc</c>).
/// The four low bits of <c>side</c> say which sides are edges: 1 left, 2 top, 4 right, 8 bottom.
/// </summary>
public static class BlockPainter
{
    public const int CellSize = 18;
    public const int SmallCellSize = 6;

    /// <summary>One cell including the rounded inner corners that depend on the neighbours.</summary>
    /// <param name="neighbours">Side bits of the cells to the left, above, to the right and below.</param>
    public static void DrawCell(PixelBuffer b, int x, int y, int side, int color, BlockColors c, ReadOnlySpan<byte> neighbours)
    {
        DrawPlain(b, x, y, side, color, c);

        if ((side & 1) == 0 && (side & 2) == 0 && (neighbours[0] & 2) != 0 && (neighbours[1] & 1) != 0)
        {
            b.Put(x, y, c.Shade(color, 7));
            b.Put(x + 1, y, c.Shade(color, 6));
            b.Put(x, y + 1, c.Shade(color, 6));
            b.Put(x + 2, y, c.Shade(color, 5));
            b.Put(x + 1, y + 1, c.Shade(color, 5));
            b.Put(x, y + 2, c.Shade(color, 5));
        }
        if ((side & 4) == 0 && (side & 2) == 0 && (neighbours[2] & 2) != 0 && (neighbours[1] & 4) != 0)
        {
            b.Put(x + 17, y, c.Shade(color, 3));
            b.Put(x + 16, y, c.Shade(color, 3));
            b.Put(x + 17, y + 1, c.Shade(color, 4));
            b.Put(x + 15, y, c.Shade(color, 3));
            b.Put(x + 16, y + 1, c.Shade(color, 4));
            b.Put(x + 17, y + 2, c.Shade(color, 5));
        }
        if ((side & 1) == 0 && (side & 8) == 0 && (neighbours[0] & 8) != 0 && (neighbours[3] & 1) != 0)
        {
            b.Put(x, y + 17, c.Shade(color, 7));
            b.Put(x + 1, y + 17, c.Shade(color, 6));
            b.Put(x, y + 16, c.Shade(color, 6));
            b.Put(x + 2, y + 17, c.Shade(color, 5));
            b.Put(x + 1, y + 16, c.Shade(color, 5));
            b.Put(x, y + 15, c.Shade(color, 5));
        }
        if ((side & 4) == 0 && (side & 8) == 0 && (neighbours[2] & 8) != 0 && (neighbours[3] & 4) != 0)
        {
            b.Put(x + 17, y + 17, c.Shade(color, 1));
            b.Put(x + 16, y + 17, c.Shade(color, 2));
            b.Put(x + 17, y + 16, c.Shade(color, 2));
            b.Put(x + 15, y + 17, c.Shade(color, 3));
            b.Put(x + 16, y + 16, c.Shade(color, 3));
            b.Put(x + 17, y + 15, c.Shade(color, 3));
        }
    }

    /// <summary>One cell without the neighbour-dependent corners (<c>raw_draw_bloc</c>).</summary>
    public static void DrawPlain(PixelBuffer b, int x, int y, int side, int color, BlockColors c)
    {
        int rx = 0, ry = 0, rw = 18, rh = 18;
        if ((side & 1) != 0)
        {
            b.VLine(x, y, 18, c.Shade(color, 7));
            b.VLine(x + 1, y, 18, c.Shade(color, 6));
            b.VLine(x + 2, y, 18, c.Shade(color, 5));
            rx = 3;
            rw -= 3;
        }
        if ((side & 2) != 0)
        {
            b.HLine(y, x, 18, c.Shade(color, 7));
            int tx = (side & 1) != 0 ? x + 1 : x;
            b.HLine(y + 1, tx, x - tx + 18, c.Shade(color, 6));
            tx = (side & 1) != 0 ? x + 2 : x;
            b.HLine(y + 2, tx, x - tx + 18, c.Shade(color, 5));
            ry = 3;
            rh -= 3;
        }
        if ((side & 4) != 0)
        {
            b.VLine(x + 17, y, 18, c.Shade(color, 1));
            int ty = (side & 2) != 0 ? y + 1 : y;
            b.VLine(x + 16, ty, y - ty + 18, c.Shade(color, 2));
            ty = (side & 2) != 0 ? y + 2 : y;
            b.VLine(x + 15, ty, y - ty + 18, c.Shade(color, 3));
            rw -= 3;
        }
        if ((side & 8) != 0)
        {
            b.HLine(y + 17, x, 18, c.Shade(color, 1));
            int tx = (side & 1) != 0 ? x + 1 : x;
            int tl = x - tx + 18;
            if ((side & 4) != 0)
                tl--;
            b.HLine(y + 16, tx, tl, c.Shade(color, 2));
            tx = (side & 1) != 0 ? x + 2 : x;
            tl = x - tx + 18;
            if ((side & 4) != 0)
                tl -= 2;
            b.HLine(y + 15, tx, tl, c.Shade(color, 3));
            rh -= 3;
        }
        uint main = c.Shade(color, 4);
        for (int i = 0; i < rh; i++)
            b.HLine(y + ry + i, x + rx, rw, main);
    }

    /// <summary>A 6 x 6 cell as used in the next-piece preview (<c>raw_small_draw_bloc</c>).</summary>
    public static void DrawSmall(PixelBuffer b, int x, int y, int side, int color, BlockColors c)
    {
        int rx = 0, ry = 0, rw = 6, rh = 6;
        if ((side & 1) != 0)
        {
            b.VLine(x, y, 6, c.Shade(color, 7));
            rx++;
            rw--;
        }
        if ((side & 2) != 0)
        {
            b.HLine(y, x, 6, c.Shade(color, 7));
            ry++;
            rh--;
        }
        if ((side & 4) != 0)
        {
            b.VLine(x + 5, y, 6, c.Shade(color, 1));
            rw--;
        }
        if ((side & 8) != 0)
        {
            b.HLine(y + 5, x, 6, c.Shade(color, 1));
            rh--;
        }
        for (int i = 0; i < rh; i++)
            b.HLine(y + ry + i, x + rx, rw, c.Shade(color, 4));
    }
}
