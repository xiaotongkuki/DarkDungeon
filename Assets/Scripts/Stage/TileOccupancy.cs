using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A tile bookkeeping grid for prop placement: which cells are taken, and which
/// cells a walker can still reach from a starting point.
///
/// Placement needs both answers. The occupancy half stops two props sharing a
/// cell; the reachability half stops a ring of cover from walling the player
/// into a pocket, which is the failure mode a purely random scatter produces
/// sooner or later. Keeping them in one plain class means the rules can be
/// tested without a scene, and the placer stays free of loop bookkeeping.
///
/// Plain C# on purpose: no Unity objects, no transforms, just tiles.
/// </summary>
public class TileOccupancy
{
    private readonly HashSet<Vector2Int> _occupied = new HashSet<Vector2Int>();
    private RectInt _bounds;
    private readonly Queue<Vector2Int> _frontier = new Queue<Vector2Int>();
    private readonly HashSet<Vector2Int> _visited = new HashSet<Vector2Int>();

    /// <summary>Whether the grid has been given bounds yet.</summary>
    public bool HasBounds { get; private set; }

    /// <summary>Tile rectangle the arena covers.</summary>
    public RectInt Bounds => _bounds;

    /// <summary>Cells inside the bounds that no prop occupies.</summary>
    public int FreeCount { get; private set; }

    /// <summary>
    /// Sets the playable bounds. Occupancy outside them is meaningless, and the
    /// reachability walk never leaves them.
    /// </summary>
    /// <param name="bounds">Tile rectangle the arena covers.</param>
    public void SetBounds(RectInt bounds)
    {
        _bounds = bounds;
        HasBounds = true;
        _occupied.Clear();
        RecountFree();
    }

    /// <summary>Empties the occupancy, keeping the bounds.</summary>
    public void Clear()
    {
        _occupied.Clear();
        RecountFree();
    }

    /// <summary>
    /// True when a cell lies inside the bounds and holds no prop.
    /// </summary>
    /// <param name="tile">Tile to test.</param>
    /// <returns>Whether a prop could be placed there.</returns>
    public bool IsFree(Vector2Int tile)
    {
        return HasBounds && _bounds.Contains(tile) && !_occupied.Contains(tile);
    }

    /// <summary>
    /// Marks a cell as taken (or releases it).
    /// </summary>
    /// <param name="tile">Tile to mark.</param>
    /// <param name="occupied">Whether the cell is taken.</param>
    public void SetOccupied(Vector2Int tile, bool occupied)
    {
        if (!HasBounds || !_bounds.Contains(tile))
        {
            return;
        }

        bool changed = occupied ? _occupied.Add(tile) : _occupied.Remove(tile);
        if (changed)
        {
            FreeCount += occupied ? -1 : 1;
        }
    }

    /// <summary>
    /// Marks a whole footprint, or releases it. The caller supplies the tile
    /// range, which is what <see cref="ArenaGrid.TilesCovered"/> produces.
    /// </summary>
    /// <param name="min">Minimum tile index, inclusive.</param>
    /// <param name="max">Maximum tile index, exclusive.</param>
    /// <param name="occupied">Whether the cells are taken.</param>
    public void SetRectOccupied(Vector2Int min, Vector2Int max, bool occupied)
    {
        for (int y = min.y; y < max.y; y++)
        {
            for (int x = min.x; x < max.x; x++)
            {
                SetOccupied(new Vector2Int(x, y), occupied);
            }
        }
    }

    /// <summary>
    /// True when every cell of a footprint is free, which is the test a
    /// candidate placement must pass before it is committed.
    /// </summary>
    /// <param name="min">Minimum tile index, inclusive.</param>
    /// <param name="max">Maximum tile index, exclusive.</param>
    /// <returns>Whether the whole footprint is free.</returns>
    public bool IsRectFree(Vector2Int min, Vector2Int max)
    {
        for (int y = min.y; y < max.y; y++)
        {
            for (int x = min.x; x < max.x; x++)
            {
                if (!IsFree(new Vector2Int(x, y)))
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Counts the free cells reachable from a starting tile by four-way steps
    /// through free cells. Comparing this with <see cref="FreeCount"/> is how a
    /// placement checks it did not cut the arena into disconnected pockets.
    /// </summary>
    /// <param name="start">Starting tile; an occupied start yields zero.</param>
    /// <returns>Number of free cells reachable from the start.</returns>
    public int ReachableFrom(Vector2Int start)
    {
        if (!IsFree(start))
        {
            return 0;
        }

        _frontier.Clear();
        _visited.Clear();
        _frontier.Enqueue(start);
        _visited.Add(start);

        while (_frontier.Count > 0)
        {
            Vector2Int cell = _frontier.Dequeue();
            TryVisit(cell + Vector2Int.right);
            TryVisit(cell + Vector2Int.left);
            TryVisit(cell + Vector2Int.up);
            TryVisit(cell + Vector2Int.down);
        }

        return _visited.Count;
    }

    /// <summary>
    /// Queues a neighbouring cell when it is free and not yet seen.
    /// </summary>
    /// <param name="cell">Neighbour to consider.</param>
    private void TryVisit(Vector2Int cell)
    {
        if (IsFree(cell) && _visited.Add(cell))
        {
            _frontier.Enqueue(cell);
        }
    }

    /// <summary>
    /// Recomputes the free-cell count from the bounds and the occupancy set.
    /// </summary>
    private void RecountFree()
    {
        FreeCount = HasBounds
            ? Mathf.Max(0, _bounds.width * _bounds.height - _occupied.Count)
            : 0;
    }
}
