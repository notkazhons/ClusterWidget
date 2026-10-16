using ClusterWidget;
using Xunit;

namespace ClusterWidget.Tests;

public class LayoutTests
{
    // A 20x12-cell work area starting at the origin.
    private static readonly CellRect Bounds = new(0, 0, 20, 12);

    [Theory]
    [InlineData(0, 2, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 3, 2)]
    [InlineData(9, 3, 3)]
    [InlineData(10, 4, 3)]
    public void TileGrid_GrowsOnlyAsNeeded(int items, int cols, int rows)
        => Assert.Equal((cols, rows), Layout.TileGrid(items, 10, 10));

    [Fact]
    public void TileGrid_IsCappedToWhatFitsOnScreen()
    {
        var (cols, rows) = Layout.TileGrid(100, 3, 2);
        Assert.Equal((3, 2), (cols, rows));
    }

    [Fact]
    public void Expand_InTheMiddle_SplitsGrowthEvenly()
    {
        var r = Layout.Expand(new CellRect(8, 5, 4, 4), 3, 3, 2, Bounds);
        Assert.Equal(new CellRect(7, 4, 6, 6), r);
    }

    [Fact]
    public void Expand_InTopLeftCorner_GrowsRightAndDown()
    {
        var r = Layout.Expand(new CellRect(0, 0, 4, 4), 3, 3, 2, Bounds);
        Assert.Equal(new CellRect(0, 0, 6, 6), r);
    }

    [Fact]
    public void Expand_AgainstRightEdge_GrowsLeftAndBothVerticalWays()
    {
        var r = Layout.Expand(new CellRect(16, 4, 4, 4), 3, 3, 2, Bounds);
        Assert.Equal(new CellRect(14, 3, 6, 6), r);
    }

    [Fact]
    public void Expand_AgainstBottomRightCorner_GrowsLeftAndUp()
    {
        var r = Layout.Expand(new CellRect(16, 8, 4, 4), 3, 3, 2, Bounds);
        Assert.Equal(new CellRect(14, 6, 6, 6), r);
    }

    [Fact]
    public void Expand_OddGrowth_GivesTheExtraCellToTheFarSide()
    {
        // 3-cell tiles: collapsed 6 wide, expanded 9 wide -> 3 extra: 1 left, 2 right.
        var r = Layout.Expand(new CellRect(8, 4, 6, 6), 3, 3, 3, Bounds);
        Assert.Equal(new CellRect(7, 3, 9, 9), r);
    }

    [Fact]
    public void Expand_NeverShrinksBelowTheCollapsedFootprint()
    {
        var r = Layout.Expand(new CellRect(8, 5, 4, 4), 2, 2, 2, Bounds);
        Assert.Equal(new CellRect(8, 5, 4, 4), r);
    }

    [Fact]
    public void Expand_NeverLeavesTheWorkArea()
    {
        var tiny = new CellRect(2, 1, 6, 5);
        var r = Layout.Expand(new CellRect(2, 1, 4, 4), 5, 5, 2, tiny);
        Assert.Equal(new CellRect(2, 1, 6, 5), r);
    }

    [Fact]
    public void Clamp_PullsAWidgetBackInsideTheWorkArea()
    {
        Assert.Equal(new CellRect(16, 8, 4, 4), Layout.Clamp(new CellRect(30, 20, 4, 4), Bounds));
        Assert.Equal(new CellRect(0, 0, 4, 4), Layout.Clamp(new CellRect(-3, -2, 4, 4), Bounds));
    }
}
