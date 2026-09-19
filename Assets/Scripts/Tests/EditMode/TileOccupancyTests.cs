using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Covers the placement bookkeeping: what counts as free, and whether a scatter
/// left the arena walkable. The reachability half is the guard against sealing
/// the player into a pocket, so it is pinned here rather than trusted to a
/// screenshot.
/// </summary>
public class TileOccupancyTests
{
    /// <summary>A 10x10 arena anchored at the origin.</summary>
    private static TileOccupancy MakeGrid()
    {
        TileOccupancy grid = new TileOccupancy();
        grid.SetBounds(new RectInt(-5, -5, 10, 10));
        return grid;
    }

    [Test]
    public void FreshGrid_IsAllFree()
    {
        TileOccupancy grid = MakeGrid();
        Assert.AreEqual(100, grid.FreeCount);
        Assert.IsTrue(grid.IsFree(Vector2Int.zero));
        Assert.IsTrue(grid.IsFree(new Vector2Int(4, 4)));
    }

    [Test]
    public void CellsOutsideTheBounds_AreNeverFree()
    {
        TileOccupancy grid = MakeGrid();
        Assert.IsFalse(grid.IsFree(new Vector2Int(5, 0)), "The far edge is outside a 10-wide grid.");
        Assert.IsFalse(grid.IsFree(new Vector2Int(0, -6)));
    }

    [Test]
    public void OccupyingAndReleasing_UpdatesTheFreeCount()
    {
        TileOccupancy grid = MakeGrid();
        grid.SetOccupied(Vector2Int.zero, true);
        Assert.AreEqual(99, grid.FreeCount);
        Assert.IsFalse(grid.IsFree(Vector2Int.zero));

        grid.SetOccupied(Vector2Int.zero, true);
        Assert.AreEqual(99, grid.FreeCount, "Marking the same cell twice must not double count.");

        grid.SetOccupied(Vector2Int.zero, false);
        Assert.AreEqual(100, grid.FreeCount);
    }

    [Test]
    public void SetRectOccupied_BooksAWholeFootprint()
    {
        TileOccupancy grid = MakeGrid();
        grid.SetRectOccupied(new Vector2Int(0, 0), new Vector2Int(2, 2), true);
        Assert.AreEqual(96, grid.FreeCount, "A two-by-two footprint takes four cells.");
        Assert.IsFalse(grid.IsRectFree(new Vector2Int(0, 0), new Vector2Int(1, 1)));
        Assert.IsTrue(grid.IsRectFree(new Vector2Int(2, 2), new Vector2Int(3, 3)));
    }

    [Test]
    public void ReachableFrom_CountsTheWholeOpenGrid()
    {
        TileOccupancy grid = MakeGrid();
        Assert.AreEqual(100, grid.ReachableFrom(Vector2Int.zero));
    }

    [Test]
    public void ReachableFrom_StopsAtAWall()
    {
        TileOccupancy grid = MakeGrid();
        // A full vertical wall down the middle (ten cells) splits the grid: the
        // left five columns stay reachable from the left, the right four do not.
        grid.SetRectOccupied(new Vector2Int(0, -5), new Vector2Int(1, 5), true);

        int reachable = grid.ReachableFrom(new Vector2Int(-4, 0));
        Assert.AreEqual(50, reachable, "Only the left half is reachable.");
        Assert.AreEqual(40, grid.FreeCount - 50, "The right half is cut off.");
    }

    [Test]
    public void ReachableFrom_AnOccupiedCell_IsZero()
    {
        TileOccupancy grid = MakeGrid();
        grid.SetOccupied(Vector2Int.zero, true);
        Assert.AreEqual(0, grid.ReachableFrom(Vector2Int.zero));
    }

    [Test]
    public void ReachableFrom_WalksAroundAPartialWall()
    {
        TileOccupancy grid = MakeGrid();
        // A wall with a one-tile gap at the top: everything stays reachable.
        grid.SetRectOccupied(new Vector2Int(0, -5), new Vector2Int(1, 4), true);

        Assert.AreEqual(grid.FreeCount, grid.ReachableFrom(new Vector2Int(-4, 0)),
            "A wall with a gap must not cut the arena.");
    }

    [Test]
    public void Clear_KeepsTheBoundsAndFreesEverything()
    {
        TileOccupancy grid = MakeGrid();
        grid.SetRectOccupied(new Vector2Int(-2, -2), new Vector2Int(2, 2), true);
        grid.Clear();

        Assert.IsTrue(grid.HasBounds, "Clearing occupancy must keep the arena bounds.");
        Assert.AreEqual(100, grid.FreeCount);
    }
}
