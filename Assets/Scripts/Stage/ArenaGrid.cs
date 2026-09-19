using UnityEngine;

/// <summary>
/// Pure grid maths for the arena terrain.
///
/// The arena uses a one-unit lattice anchored at the world origin: tile (i, j)
/// covers [i, i+1) x [j, j+1), so tile boundaries sit on integers and tile
/// centres on half-integers. That anchor is what makes the fixed points of the
/// map - the player spawn and the portal, both at (0, 0) - land on a tile
/// boundary, so a two-unit portal covers exactly four whole tiles.
///
/// Everything here is a pure function so the alignment rules can be tested
/// without a scene, and so terrain generation, prop placement and the editor
/// preview all share one definition of "aligned" instead of three drifting
/// copies.
/// </summary>
public static class ArenaGrid
{
    /// <summary>Edge length of one tile in world units (16 px at 16 pixels per unit).</summary>
    public const float TileSize = 1f;

    /// <summary>
    /// Rounds a world coordinate down to the tile boundary that starts at or
    /// before it.
    /// </summary>
    /// <param name="world">World coordinate.</param>
    /// <returns>The boundary coordinate of the tile containing it.</returns>
    public static float TileMin(float world)
    {
        return Mathf.Floor(world / TileSize) * TileSize;
    }

    /// <summary>
    /// The tile index containing a world coordinate.
    /// </summary>
    /// <param name="world">World coordinate.</param>
    /// <returns>Tile index along that axis.</returns>
    public static int TileIndex(float world)
    {
        return Mathf.FloorToInt(world / TileSize);
    }

    /// <summary>
    /// Centre of a tile, in world space.
    /// </summary>
    /// <param name="x">Tile index along x.</param>
    /// <param name="y">Tile index along y.</param>
    /// <returns>World-space centre of that tile.</returns>
    public static Vector2 TileCenter(int x, int y)
    {
        return new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);
    }

    /// <summary>
    /// Centre of the tile containing a world point.
    /// </summary>
    /// <param name="world">World point.</param>
    /// <returns>World-space centre of its tile.</returns>
    public static Vector2 CenterOf(Vector2 world)
    {
        return TileCenter(TileIndex(world.x), TileIndex(world.y));
    }

    /// <summary>
    /// Snaps a footprint's centre so the footprint's minimum edge lands on a
    /// tile boundary. This is the one alignment rule the whole terrain follows:
    /// a one-unit prop ends up centred on a tile, a two-unit prop ends up
    /// straddling exactly four.
    /// </summary>
    /// <param name="candidate">Desired centre in world space.</param>
    /// <param name="footprint">Footprint size in world units (usually 1 or 2).</param>
    /// <returns>The nearest aligned centre.</returns>
    public static Vector2 SnapCenter(Vector2 candidate, Vector2 footprint)
    {
        Vector2 half = new Vector2(Mathf.Abs(footprint.x), Mathf.Abs(footprint.y)) * 0.5f;
        Vector2 min = new Vector2(
            Mathf.Round((candidate.x - half.x) / TileSize) * TileSize,
            Mathf.Round((candidate.y - half.y) / TileSize) * TileSize);
        return min + half;
    }

    /// <summary>
    /// Snaps a rectangle outward to the tile lattice: the minimum corner rounds
    /// down and the maximum corner rounds up, so the result always covers the
    /// original rectangle in whole tiles.
    /// </summary>
    /// <param name="rect">Rectangle in world space.</param>
    /// <returns>The covering aligned rectangle.</returns>
    public static Rect SnapRect(Rect rect)
    {
        float minX = TileMin(rect.xMin);
        float minY = TileMin(rect.yMin);
        float maxX = Mathf.Ceil(rect.xMax / TileSize) * TileSize;
        float maxY = Mathf.Ceil(rect.yMax / TileSize) * TileSize;
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// True when a footprint centre already sits on the lattice, i.e. its
    /// minimum edge is a tile boundary. Used by tests and by the runtime probe
    /// that checks a generated arena.
    /// </summary>
    /// <param name="center">Footprint centre in world space.</param>
    /// <param name="footprint">Footprint size in world units.</param>
    /// <returns>Whether the footprint is grid aligned.</returns>
    public static bool IsAligned(Vector2 center, Vector2 footprint)
    {
        Vector2 aligned = SnapCenter(center, footprint);
        return Mathf.Approximately(aligned.x, center.x) && Mathf.Approximately(aligned.y, center.y);
    }

    /// <summary>
    /// The tile indices a footprint covers, as a half-open range: tiles from
    /// <paramref name="min"/> inclusive to <paramref name="max"/> exclusive on
    /// each axis.
    /// </summary>
    /// <param name="center">Footprint centre, expected to be aligned.</param>
    /// <param name="footprint">Footprint size in world units.</param>
    /// <param name="min">Minimum tile index covered.</param>
    /// <param name="max">One past the maximum tile index covered.</param>
    public static void TilesCovered(Vector2 center, Vector2 footprint, out Vector2Int min, out Vector2Int max)
    {
        Vector2 half = new Vector2(Mathf.Abs(footprint.x), Mathf.Abs(footprint.y)) * 0.5f;
        min = new Vector2Int(TileIndex(center.x - half.x), TileIndex(center.y - half.y));
        // A footprint that ends exactly on a boundary does not reach into the
        // next tile, hence the small pull-back before flooring the far edge.
        max = new Vector2Int(
            TileIndex(center.x + half.x - 0.001f) + 1,
            TileIndex(center.y + half.y - 0.001f) + 1);
    }
}
