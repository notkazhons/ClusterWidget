using ClusterWidget;
using Xunit;

namespace ClusterWidget.Tests;

public class LayoutTests
{
    // A 20x12-cell work area starting at the origin.
    private static readonly CellRect Bounds = new(0, 0, 20, 12);

    [Theory]
    [InlineData(2, 95, 121, 3, 2)]   // tall desktop cells: the box gets wider than it is tall
    [InlineData(2, 100, 100, 2, 2)]
    [InlineData(3, 100, 100, 3, 3)]
    [InlineData(1, 100, 100, 2, 2)]  // never below a 2x2 box
    public void CollapsedCells_LooksRoughlySquare(int rows, int cw, int ch, int w, int h)
        => Assert.Equal((w, h), Layout.CollapsedCells(rows, cw, ch));

    [Theory]
    [InlineData(0, 2, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 3, 2)]
    [InlineData(9, 3, 3)]
    [InlineData(10, 4, 3)]
    public void TileGrid_GrowsOnlyAsNeeded(int items, int cols, int rows)
        => Assert.Equal((cols, rows), Layout.TileGrid(items, 20, 12, 100, 100));

    [Fact]
    public void TileGrid_FollowsTheCellShape()
    {
        // tall cells (95x121): 7 items fit a 3x3 better than a wide 4x2
        Assert.Equal((3, 3), Layout.TileGrid(7, 20, 12, 95, 121));
    }

    [Fact]
    public void TileGrid_IsCappedToWhatFitsOnScreen()
        => Assert.Equal((3, 2), Layout.TileGrid(100, 3, 2, 100, 100));

    [Fact]
    public void Expand_InTheMiddle_SplitsGrowthEvenly()
        => Assert.Equal(new CellRect(7, 4, 6, 6), Layout.Expand(new CellRect(8, 5, 4, 4), 6, 6, Bounds));

    [Fact]
    public void Expand_InTopLeftCorner_GrowsRightAndDown()
        => Assert.Equal(new CellRect(0, 0, 6, 6), Layout.Expand(new CellRect(0, 0, 4, 4), 6, 6, Bounds));

    [Fact]
    public void Expand_AgainstRightEdge_GrowsLeftAndBothVerticalWays()
        => Assert.Equal(new CellRect(14, 3, 6, 6), Layout.Expand(new CellRect(16, 4, 4, 4), 6, 6, Bounds));

    [Fact]
    public void Expand_AgainstBottomRightCorner_GrowsLeftAndUp()
        => Assert.Equal(new CellRect(14, 6, 6, 6), Layout.Expand(new CellRect(16, 8, 4, 4), 6, 6, Bounds));

    [Fact]
    public void Expand_OddGrowth_GivesTheExtraCellToTheFarSide()
        // collapsed 3 wide, expanded 6 wide -> 3 extra: 1 left, 2 right
        => Assert.Equal(new CellRect(7, 3, 6, 5), Layout.Expand(new CellRect(8, 4, 3, 3), 6, 5, Bounds));

    [Fact]
    public void Expand_NeverShrinksBelowTheCollapsedFootprint()
        => Assert.Equal(new CellRect(8, 5, 4, 4), Layout.Expand(new CellRect(8, 5, 4, 4), 2, 2, Bounds));

    [Fact]
    public void Expand_NeverLeavesTheWorkArea()
        => Assert.Equal(new CellRect(2, 1, 6, 5), Layout.Expand(new CellRect(2, 1, 4, 4), 10, 10, new CellRect(2, 1, 6, 5)));

    [Fact]
    public void Clamp_PullsAWidgetBackInsideTheWorkArea()
    {
        Assert.Equal(new CellRect(16, 8, 4, 4), Layout.Clamp(new CellRect(30, 20, 4, 4), Bounds));
        Assert.Equal(new CellRect(0, 0, 4, 4), Layout.Clamp(new CellRect(-3, -2, 4, 4), Bounds));
    }
}
