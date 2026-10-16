using System;

namespace ClusterWidget;

/// <summary>A rectangle measured in desktop-grid cells.</summary>
public readonly record struct CellRect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
}

/// <summary>Pure grid math: how big the box is, how big the expanded view is and where it lands. No Win32 here.</summary>
public static class Layout
{
    /// <summary>Items visible while collapsed (a 2x2 block).</summary>
    public const int VisibleSlots = 4;

    /// <summary>
    /// Collapsed footprint in cells: <paramref name="rows"/> tall, and as wide as it takes to look roughly square
    /// given that desktop cells are usually taller than they are wide.
    /// </summary>
    public static (int W, int H) CollapsedCells(int rows, int cellW, int cellH)
    {
        rows = Math.Max(2, rows);
        int cols = Math.Max(2, (int)Math.Round(rows * (double)cellH / Math.Max(1, cellW)));
        return (cols, rows);
    }

    /// <summary>
    /// Columns and rows of one-cell tiles for <paramref name="itemCount"/> items: the grid whose pixel shape is
    /// closest to square with the fewest empty cells, capped to what fits on screen.
    /// </summary>
    public static (int Cols, int Rows) TileGrid(int itemCount, int maxCols, int maxRows, int cellW, int cellH)
    {
        maxCols = Math.Max(2, maxCols);
        maxRows = Math.Max(2, maxRows);
        int n = Math.Max(itemCount, VisibleSlots);
        int bestCols = -1, bestRows = -1;
        double best = double.MaxValue;
        for (int rows = 1; rows <= maxRows; rows++)
        {
            int cols = (n + rows - 1) / rows;
            if (cols > maxCols) continue;
            double score = Math.Abs(cols * (double)cellW - rows * (double)cellH) + (cols * rows - n) * cellW / 2.0;
            if (score < best) { best = score; bestCols = cols; bestRows = rows; }
        }
        return bestCols < 0 ? (maxCols, maxRows) : (bestCols, bestRows);
    }

    /// <summary>Keeps <paramref name="r"/> fully inside <paramref name="bounds"/>.</summary>
    public static CellRect Clamp(CellRect r, CellRect bounds) => r with
    {
        X = Math.Clamp(r.X, bounds.X, Math.Max(bounds.X, bounds.Right - r.W)),
        Y = Math.Clamp(r.Y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - r.H)),
    };

    /// <summary>
    /// Expanded footprint: grows only as much as <paramref name="cols"/> x <paramref name="rows"/> cells need,
    /// split evenly on both sides of each axis, then shifted back inside <paramref name="bounds"/> (a widget on the
    /// right edge grows left, one in the top-left corner grows right and down).
    /// </summary>
    public static CellRect Expand(CellRect collapsed, int cols, int rows, CellRect bounds)
    {
        int w = Math.Min(Math.Max(cols, collapsed.W), bounds.W);
        int h = Math.Min(Math.Max(rows, collapsed.H), bounds.H);
        return new CellRect(
            Place(collapsed.X, collapsed.W, w, bounds.X, bounds.Right),
            Place(collapsed.Y, collapsed.H, h, bounds.Y, bounds.Bottom),
            w, h);
    }

    private static int Place(int start, int oldSize, int newSize, int lo, int hi)
    {
        int s = start - (newSize - oldSize) / 2;
        if (s + newSize > hi) s = hi - newSize;
        if (s < lo) s = lo;
        return s;
    }
}
