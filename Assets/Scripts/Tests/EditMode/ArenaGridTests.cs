using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Covers the terrain lattice: where tile boundaries fall, how footprints snap
/// onto them and which tiles a footprint covers. These rules are what keep
/// props from straddling tiles, so they are pinned here rather than discovered
/// by eye in a screenshot.
/// </summary>
public class ArenaGridTests
{
    /// <summary>One-unit footprint, the common case (pillars, rubble, spikes).</summary>
    private static readonly Vector2 OneTile = new Vector2(1f, 1f);

    /// <summary>Two-unit footprint, the portal and the L-shaped cover.</summary>
    private static readonly Vector2 TwoTiles = new Vector2(2f, 2f);

    [Test]
    public void TileCenter_SitsOnAHalfInteger()
    {
        Assert.AreEqual(new Vector2(0.5f, 0.5f), ArenaGrid.TileCenter(0, 0));
        Assert.AreEqual(new Vector2(-0.5f, -0.5f), ArenaGrid.TileCenter(-1, -1));
        Assert.AreEqual(new Vector2(3.5f, -2.5f), ArenaGrid.TileCenter(3, -3));
    }

    [Test]
    public void TileIndex_CoversTheHalfOpenCell()
    {
        Assert.AreEqual(0, ArenaGrid.TileIndex(0f), "A boundary belongs to the tile it starts.");
        Assert.AreEqual(0, ArenaGrid.TileIndex(0.99f));
        Assert.AreEqual(1, ArenaGrid.TileIndex(1f));
        Assert.AreEqual(-1, ArenaGrid.TileIndex(-0.01f));
    }

    [Test]
    public void SnapCenter_OneTileFootprint_LandsOnATileCentre()
    {
        Assert.AreEqual(new Vector2(0.5f, 0.5f), ArenaGrid.SnapCenter(new Vector2(0.2f, 0.2f), OneTile));
        Assert.AreEqual(new Vector2(-1.5f, 2.5f), ArenaGrid.SnapCenter(new Vector2(-1.7f, 2.1f), OneTile));
        Assert.IsTrue(ArenaGrid.IsAligned(new Vector2(4.5f, -7.5f), OneTile));
        Assert.IsFalse(ArenaGrid.IsAligned(new Vector2(4.2f, -7.5f), OneTile));
    }

    [Test]
    public void SnapCenter_TwoTileFootprint_LandsOnATileBoundary()
    {
        // A two-unit footprint aligned to the lattice has its edges on
        // boundaries, which puts its centre on an integer - the case the portal
        // at (0, 0) relies on.
        Assert.AreEqual(new Vector2(0f, 0f), ArenaGrid.SnapCenter(new Vector2(0.3f, -0.4f), TwoTiles));
        Assert.AreEqual(new Vector2(2f, -4f), ArenaGrid.SnapCenter(new Vector2(2.4f, -3.6f), TwoTiles));
        Assert.IsTrue(ArenaGrid.IsAligned(Vector2.zero, TwoTiles));
    }

    [Test]
    public void SnapCenter_OddFootprint_UsesItsOwnSizeNotAConstant()
    {
        // A three-tile wall segment must centre on a half-integer to keep whole
        // tiles, which a hard-coded "1 means centre" rule would get wrong.
        Vector2 centre = ArenaGrid.SnapCenter(new Vector2(1.9f, 0f), new Vector2(3f, 1f));
        Assert.AreEqual(1.5f, centre.x, "A three-tile footprint centres half a tile past the boundary.");
        Assert.AreEqual(0.5f, centre.y);
    }

    [Test]
    public void SnapRect_RoundsOutwardToWholeTiles()
    {
        Rect snapped = ArenaGrid.SnapRect(Rect.MinMaxRect(-9.6f, -9.4f, 9.7f, 9.2f));
        Assert.AreEqual(-10f, snapped.xMin);
        Assert.AreEqual(-10f, snapped.yMin);
        Assert.AreEqual(10f, snapped.xMax);
        Assert.AreEqual(10f, snapped.yMax);
        Assert.AreEqual(20f, snapped.width);
        Assert.AreEqual(20f, snapped.height);
    }

    [Test]
    public void SnapRect_AlreadyAlignedRect_IsUnchanged()
    {
        Rect rect = Rect.MinMaxRect(-10f, -10f, 10f, 10f);
        Assert.AreEqual(rect, ArenaGrid.SnapRect(rect));
    }

    [Test]
    public void TilesCovered_OneTileFootprint_CoversExactlyOneCell()
    {
        ArenaGrid.TilesCovered(new Vector2(0.5f, 0.5f), OneTile, out Vector2Int min, out Vector2Int max);
        Assert.AreEqual(new Vector2Int(0, 0), min);
        Assert.AreEqual(new Vector2Int(1, 1), max, "The max is exclusive.");
    }

    [Test]
    public void TilesCovered_TwoTileFootprint_CoversFourCells()
    {
        ArenaGrid.TilesCovered(Vector2.zero, TwoTiles, out Vector2Int min, out Vector2Int max);
        Assert.AreEqual(new Vector2Int(-1, -1), min);
        Assert.AreEqual(new Vector2Int(1, 1), max);
        Assert.AreEqual(4, (max.x - min.x) * (max.y - min.y));
    }

    [Test]
    public void TilesCovered_NegativeSideFootprint_CoversWholeTiles()
    {
        // A wall segment laid out as (3, 1) centred at 1.5 covers tiles 0..2 on
        // x: the far edge lands exactly on a boundary and must not claim a
        // fourth cell.
        ArenaGrid.TilesCovered(new Vector2(1.5f, 0.5f), new Vector2(3f, 1f), out Vector2Int min, out Vector2Int max);
        Assert.AreEqual(new Vector2Int(0, 0), min);
        Assert.AreEqual(new Vector2Int(3, 1), max);
    }
}
