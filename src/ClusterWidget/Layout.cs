using System;

namespace ClusterWidget;

/// <summary>A rectangle measured in desktop-grid cells.</summary>
public readonly record struct CellRect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
}

/// <summary>Pure grid math: how big the expanded widget is and where it lands. No Win32 here.</summary>
public static class Layout
{
    /// <summary>Items visible while collapsed (a 2x2 block of tiles).</summary>
    public const int VisibleSlots = 4;

    /// <summary>Tile columns/rows needed for <paramref name="itemCount"/> items, capped to what fits on screen.</summary>
    public static (int Cols, int Rows) TileGrid(int itemCount, int maxCols, int maxRows)
    {
        maxCols = Math.Max(2, maxCols);
        maxRows = Math.Max(2, maxRows);
        int n = Math.Max(itemCount, VisibleSlots);
        int cols = Math.Min((int)Math.Ceiling(Math.Sqrt(n)), maxCols);
        int rows = Math.Min((n + cols - 1) / cols, maxRows);
        return (cols, rows);
    }

    /// <summary>Keeps <paramref name="r"/> fully inside <paramref name="bounds"/>.</summary>
    public static CellRect Clamp(CellRect r, CellRect bounds) => r with
    {
        X = Math.Clamp(r.X, bounds.X, Math.Max(bounds.X, bounds.Right - r.W)),
        Y = Math.Clamp(r.Y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - r.H)),
    };

    /// <summary>
    /// Expanded footprint: grows only as much as the tiles need, split evenly on both sides of
    /// each axis, then shifted back inside <paramref name="bounds"/> (so a widget on the right
    /// edge grows left, one in the top-left corner grows right and down).
    /// </summary>
    public static CellRect Expand(CellRect collapsed, int cols, int rows, int tileCells, CellRect bounds)
    {
        int w = Math.Min(Math.Max(cols * tileCells, collapsed.W), bounds.W);
        int h = Math.Min(Math.Max(rows * tileCells, collapsed.H), bounds.H);
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
