using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Uniform spatial hash grid over items that expose a world position, offering
/// O(1) add/remove/move and a nearest-neighbour query that scans only the cells
/// around the query point instead of the whole population.
///
/// Why not a plain List&lt;T&gt;? A list answers "closest to X" in O(N) and
/// removes in O(N) (every later element shifts down). Once auto-attack, piercing
/// shots or area damage exist those two operations run constantly, so the
/// population is bucketed per cell and only the cells actually touched are paid
/// for.
///
/// Removal is swap-and-pop in both the flat list and the cell bucket, driven by
/// a per-item slot that records where the item currently lives. Swapping moves
/// the previous last element into the freed index, so both lists stay packed and
/// nothing ever shifts. The price is unstable iteration order: callers must not
/// rely on the order of <see cref="Items"/> or of a bucket.
/// </summary>
/// <typeparam name="T">Reference type stored in the grid.</typeparam>
public sealed class SpatialGrid<T> where T : class
{
    /// <summary>
    /// Safety bound on how many cell rings a nearest-neighbour query will
    /// expand through, so a sparse grid can never scan unbounded.
    /// </summary>
    private const int MaxSearchRings = 32;

    /// <summary>Initial size of a freshly pooled cell bucket.</summary>
    private const int DefaultBucketCapacity = 16;

    /// <summary>
    /// Where one item currently lives: its cell key plus its index inside both
    /// the flat list and that cell's bucket. Keeping these indices is what turns
    /// removal into a swap-and-pop instead of a linear search for the item.
    /// </summary>
    private struct Slot
    {
        public Vector2Int Cell;
        public int IndexInBucket;
        public int IndexInItems;
    }

    private readonly float _cellSize;
    private readonly Func<T, Vector2> _positionOf;
    private readonly List<T> _items;
    private readonly Dictionary<T, Slot> _slots;
    private readonly Dictionary<Vector2Int, List<T>> _buckets;
    private readonly Stack<List<T>> _bucketPool = new Stack<List<T>>();

    /// <summary>
    /// Creates a grid with the given cell size and position accessor.
    /// </summary>
    /// <param name="cellSize">
    /// World units per cell. Smaller cells sharpen nearest-neighbour queries at
    /// the cost of touching more buckets; roughly the typical item spacing works
    /// well.
    /// </param>
    /// <param name="positionProvider">
    /// Reads an item's world position. Called on add, on update and for every
    /// candidate during a query, so it must be cheap (a transform read is fine).
    /// </param>
    /// <param name="initialCapacity">Capacity hint so early inserts do not resize.</param>
    public SpatialGrid(float cellSize, Func<T, Vector2> positionProvider, int initialCapacity = 256)
    {
        if (cellSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive.");
        }
        if (positionProvider == null)
        {
            throw new ArgumentNullException(nameof(positionProvider));
        }

        _cellSize = cellSize;
        _positionOf = positionProvider;
        int capacity = Mathf.Max(4, initialCapacity);
        _items = new List<T>(capacity);
        _slots = new Dictionary<T, Slot>(capacity);
        _buckets = new Dictionary<Vector2Int, List<T>>(capacity / 4 + 4);
    }

    /// <summary>Number of items currently registered.</summary>
    public int Count => _items.Count;

    /// <summary>
    /// Flat view of the registered items. Iteration order is unstable (swap-and-pop
    /// reorders it); never mutate the grid while iterating this.
    /// </summary>
    public IReadOnlyList<T> Items => _items;

    /// <summary>
    /// Maps a world position to its cell key. Exposed for debug gizmos and tests.
    /// </summary>
    /// <param name="position">World position.</param>
    /// <returns>The cell containing that position.</returns>
    public Vector2Int CellOf(Vector2 position)
    {
        return new Vector2Int(
            Mathf.FloorToInt(position.x / _cellSize),
            Mathf.FloorToInt(position.y / _cellSize));
    }

    /// <summary>
    /// True when the item is registered.
    /// </summary>
    /// <param name="item">Item to look up.</param>
    /// <returns>Whether the item is present.</returns>
    public bool Contains(T item)
    {
        return item != null && _slots.ContainsKey(item);
    }

    /// <summary>
    /// Registers an item in the cell holding its current position.
    /// </summary>
    /// <param name="item">Item to add; duplicates and null are rejected.</param>
    /// <returns>True when the item was added.</returns>
    public bool Add(T item)
    {
        if (item == null || _slots.ContainsKey(item))
        {
            return false;
        }

        Vector2Int cell = CellOf(_positionOf(item));
        List<T> bucket = GetOrCreateBucket(cell);

        Slot slot;
        slot.Cell = cell;
        slot.IndexInBucket = bucket.Count;
        slot.IndexInItems = _items.Count;

        bucket.Add(item);
        _items.Add(item);
        _slots.Add(item, slot);
        return true;
    }

    /// <summary>
    /// Unregisters an item, leaving the remaining ones packed. Both the bucket
    /// and the flat list drop the item with a swap-and-pop, so the cost does not
    /// depend on how many items are registered.
    /// </summary>
    /// <param name="item">Item to remove.</param>
    /// <returns>True when the item was present and removed.</returns>
    public bool Remove(T item)
    {
        if (item == null || !_slots.TryGetValue(item, out Slot slot))
        {
            return false;
        }

        // Detach from the bucket first: if it empties out it returns to the pool,
        // and the flat list must not be touched before that bookkeeping is done.
        List<T> bucket = _buckets[slot.Cell];
        SwapPopBucket(bucket, slot.IndexInBucket);
        if (bucket.Count == 0)
        {
            _buckets.Remove(slot.Cell);
            _bucketPool.Push(bucket);
        }

        SwapPopItems(slot.IndexInItems);
        _slots.Remove(item);
        return true;
    }

    /// <summary>
    /// Re-buckets an item after it moved. Cheap enough to call every frame: it
    /// exits immediately unless the item crossed a cell boundary.
    /// </summary>
    /// <param name="item">Registered item whose position may have changed.</param>
    /// <returns>True when the item actually moved to another cell.</returns>
    public bool Update(T item)
    {
        if (item == null || !_slots.TryGetValue(item, out Slot slot))
        {
            return false;
        }

        Vector2Int cell = CellOf(_positionOf(item));
        if (cell == slot.Cell)
        {
            return false;
        }

        List<T> oldBucket = _buckets[slot.Cell];
        SwapPopBucket(oldBucket, slot.IndexInBucket);
        if (oldBucket.Count == 0)
        {
            _buckets.Remove(slot.Cell);
            _bucketPool.Push(oldBucket);
        }

        List<T> newBucket = GetOrCreateBucket(cell);
        Slot updated;
        updated.Cell = cell;
        updated.IndexInBucket = newBucket.Count;
        updated.IndexInItems = slot.IndexInItems;
        newBucket.Add(item);
        _slots[item] = updated;
        return true;
    }

    /// <summary>
    /// Finds the item closest to a world point by expanding ring by ring outwards
    /// from the point's own cell. The search stops early once the best candidate
    /// found so far is closer than the nearest point any outer ring could offer,
    /// which keeps a query close to O(1) in a uniformly dense grid instead of
    /// O(N) over the whole population.
    /// </summary>
    /// <param name="point">Query position in world space.</param>
    /// <param name="nearest">Closest item, or null when nothing matched.</param>
    /// <param name="maxRadius">
    /// Maximum distance in world units; 0 means unlimited. Candidates beyond the
    /// radius are ignored.
    /// </param>
    /// <param name="maxRings">Ring limit override; 0 uses the built-in bound.</param>
    /// <param name="filter">
    /// Optional per-candidate predicate; items it rejects are treated as absent.
    /// Lets a caller hide items that are still registered but no longer valid
    /// (e.g. enemies killed earlier in the frame whose removal is queued). Pass a
    /// cached delegate, not a fresh lambda, to keep the query allocation-free.
    /// Rejected candidates do not stop the ring expansion, so the nearest
    /// accepted item is still found — the early-exit bound below is a property of
    /// cell geometry and is unaffected by filtering.
    /// </param>
    /// <returns>True when an item was found.</returns>
    public bool TryGetNearest(Vector2 point, out T nearest, float maxRadius = 0f, int maxRings = 0,
        Func<T, bool> filter = null)
    {
        nearest = null;

        // Fast path: an empty grid would otherwise walk every ring before giving
        // up, which is the most expensive case precisely when there is nothing to
        // find (e.g. a cleared screen).
        if (_items.Count == 0)
        {
            return false;
        }

        Vector2Int origin = CellOf(point);

        // Seeding the best distance with the squared radius makes the radius
        // filter fall out of the candidate comparison for free.
        float bestSq = maxRadius > 0f ? maxRadius * maxRadius : float.PositiveInfinity;
        int ringLimit = maxRings > 0 ? maxRings : MaxSearchRings;
        bool found = false;

        for (int ring = 0; ring <= ringLimit; ring++)
        {
            ScanRing(origin, ring, point, filter, ref bestSq, ref nearest, ref found);

            // Cells in ring r + 1 are at least r * cellSize away from the query
            // point, so a best candidate already closer than that cannot be
            // beaten and the search is done.
            if (found)
            {
                float guaranteed = ring * _cellSize;
                if (bestSq <= guaranteed * guaranteed)
                {
                    break;
                }
            }

            // Past this point every remaining ring lies entirely outside the
            // search radius, so there is nothing left to find.
            if (maxRadius > 0f && ring * _cellSize > maxRadius)
            {
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// Drops every item and returns all buckets to the pool.
    /// </summary>
    public void Clear()
    {
        foreach (KeyValuePair<Vector2Int, List<T>> pair in _buckets)
        {
            pair.Value.Clear();
            _bucketPool.Push(pair.Value);
        }
        _buckets.Clear();
        _items.Clear();
        _slots.Clear();
    }

    /// <summary>
    /// Visits the buckets forming one Chebyshev ring around the origin cell,
    /// without allocating: ring 0 is the origin itself, ring r is its 8r-cell
    /// perimeter.
    /// </summary>
    /// <param name="origin">Cell containing the query point.</param>
    /// <param name="ring">Ring index; 0 is the origin cell.</param>
    /// <param name="point">Query point used for distance comparisons.</param>
    /// <param name="filter">Optional per-candidate predicate; null accepts everything.</param>
    /// <param name="bestSq">Best squared distance so far; updated in place.</param>
    /// <param name="nearest">Best candidate so far; updated in place.</param>
    /// <param name="found">Whether a candidate has been accepted; updated in place.</param>
    private void ScanRing(Vector2Int origin, int ring, Vector2 point, Func<T, bool> filter,
        ref float bestSq, ref T nearest, ref bool found)
    {
        if (ring == 0)
        {
            ScanBucket(origin, point, filter, ref bestSq, ref nearest, ref found);
            return;
        }

        int minX = origin.x - ring;
        int maxX = origin.x + ring;
        int minY = origin.y - ring;
        int maxY = origin.y + ring;

        for (int x = minX; x <= maxX; x++)
        {
            ScanBucket(new Vector2Int(x, minY), point, filter, ref bestSq, ref nearest, ref found);
            ScanBucket(new Vector2Int(x, maxY), point, filter, ref bestSq, ref nearest, ref found);
        }
        for (int y = minY + 1; y <= maxY - 1; y++)
        {
            ScanBucket(new Vector2Int(minX, y), point, filter, ref bestSq, ref nearest, ref found);
            ScanBucket(new Vector2Int(maxX, y), point, filter, ref bestSq, ref nearest, ref found);
        }
    }

    /// <summary>
    /// Compares every accepted item of one bucket against the running best.
    /// Missing cells are simply skipped, which is what makes sparse grids cheap to
    /// query.
    /// </summary>
    /// <param name="cell">Bucket key to visit.</param>
    /// <param name="point">Query point used for distance comparisons.</param>
    /// <param name="filter">Optional per-candidate predicate; null accepts everything.</param>
    /// <param name="bestSq">Best squared distance so far; updated in place.</param>
    /// <param name="nearest">Best candidate so far; updated in place.</param>
    /// <param name="found">Whether a candidate has been accepted; updated in place.</param>
    private void ScanBucket(Vector2Int cell, Vector2 point, Func<T, bool> filter,
        ref float bestSq, ref T nearest, ref bool found)
    {
        if (!_buckets.TryGetValue(cell, out List<T> bucket))
        {
            return;
        }

        for (int i = 0; i < bucket.Count; i++)
        {
            T candidate = bucket[i];

            // Rejected candidates are skipped before the distance maths, so a
            // hidden item costs one delegate call and nothing else.
            if (filter != null && !filter(candidate))
            {
                continue;
            }

            float sq = (_positionOf(candidate) - point).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                nearest = candidate;
                found = true;
            }
        }
    }

    /// <summary>
    /// Removes the element at the given index from a cell bucket by moving the
    /// last element into the freed slot, then patches the moved element's slot so
    /// its recorded index stays truthful. O(1), no shifting.
    /// </summary>
    /// <param name="bucket">Bucket to mutate.</param>
    /// <param name="index">Index of the element to drop.</param>
    private void SwapPopBucket(List<T> bucket, int index)
    {
        int last = bucket.Count - 1;
        if (index != last)
        {
            T moved = bucket[last];
            bucket[index] = moved;
            Slot movedSlot = _slots[moved];
            movedSlot.IndexInBucket = index;
            _slots[moved] = movedSlot;
        }
        bucket.RemoveAt(last);
    }

    /// <summary>
    /// Removes the element at the given index from the flat list with the same
    /// swap-and-pop trick, keeping the moved element's recorded index valid.
    /// </summary>
    /// <param name="index">Index of the element to drop.</param>
    private void SwapPopItems(int index)
    {
        int last = _items.Count - 1;
        if (index != last)
        {
            T moved = _items[last];
            _items[index] = moved;
            Slot movedSlot = _slots[moved];
            movedSlot.IndexInItems = index;
            _slots[moved] = movedSlot;
        }
        _items.RemoveAt(last);
    }

    /// <summary>
    /// Returns the bucket for a cell, taking one from the pool when available so
    /// repeated spawn/kill churn does not allocate fresh lists.
    /// </summary>
    /// <param name="cell">Bucket key.</param>
    /// <returns>The bucket, guaranteed empty on return.</returns>
    private List<T> GetOrCreateBucket(Vector2Int cell)
    {
        if (_buckets.TryGetValue(cell, out List<T> existing))
        {
            return existing;
        }

        List<T> bucket = _bucketPool.Count > 0
            ? _bucketPool.Pop()
            : new List<T>(DefaultBucketCapacity);
        bucket.Clear();
        _buckets.Add(cell, bucket);
        return bucket;
    }
}
