using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pure-logic tests for the spatial grid: bucketing, swap-and-pop integrity,
/// bucket recycling and the expanding-ring nearest-neighbour search.
///
/// The grid is generic over a position provider, so these run as fast edit-mode
/// tests against plain objects and never touch the scene.
/// </summary>
public class SpatialGridTests
{
    /// <summary>Minimal item with a mutable position and liveness, standing in for an enemy.</summary>
    private sealed class Node
    {
        public Vector2 Position;

        /// <summary>
        /// Stands in for "still a valid target"; the filter tests flip it to mimic
        /// an enemy that died but has not left the grid yet.
        /// </summary>
        public bool Alive = true;

        /// <summary>Stores a node at the given world position.</summary>
        /// <param name="position">Initial world position.</param>
        public Node(Vector2 position)
        {
            Position = position;
        }
    }

    /// <summary>
    /// Cached filter delegate, mirroring how the manager passes its own: a fresh
    /// lambda per query would allocate on every call.
    /// </summary>
    private static readonly Func<Node, bool> AliveOnly = n => n.Alive;

    /// <summary>Cell size used throughout: one world unit per cell keeps the maths readable.</summary>
    private const float CellSize = 1f;

    private SpatialGrid<Node> _grid;

    [SetUp]
    public void SetUp()
    {
        _grid = new SpatialGrid<Node>(CellSize, n => n.Position);
    }

    /// <summary>
    /// Adds a node at the given position and returns it, so tests can address it
    /// as a reference instead of an index.
    /// </summary>
    /// <param name="x">World x.</param>
    /// <param name="y">World y.</param>
    /// <returns>The registered node.</returns>
    private Node AddNode(float x, float y)
    {
        var node = new Node(new Vector2(x, y));
        Assert.IsTrue(_grid.Add(node), "Node should register.");
        return node;
    }

    [Test]
    public void Constructor_NonPositiveCellSize_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new SpatialGrid<Node>(0f, n => n.Position));
    }

    [Test]
    public void Constructor_NullPositionProvider_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => new SpatialGrid<Node>(CellSize, null));
    }

    [Test]
    public void Add_IncreasesCount_AndRegistersItem()
    {
        Node node = AddNode(0.5f, 0.5f);

        Assert.AreEqual(1, _grid.Count);
        Assert.IsTrue(_grid.Contains(node));
    }

    [Test]
    public void Add_SameItemTwice_IsRejected()
    {
        Node node = AddNode(0.5f, 0.5f);

        Assert.IsFalse(_grid.Add(node), "Duplicate registration must be rejected.");
        Assert.AreEqual(1, _grid.Count);
    }

    [Test]
    public void Add_Null_IsRejected()
    {
        Assert.IsFalse(_grid.Add(null));
        Assert.AreEqual(0, _grid.Count);
    }

    [Test]
    public void Remove_UnknownItem_ReturnsFalse()
    {
        Assert.IsFalse(_grid.Remove(new Node(Vector2.zero)));
        Assert.AreEqual(0, _grid.Count);
    }

    [Test]
    public void Remove_MiddleOfBucket_LeavesOthersQueryable()
    {
        Node first = AddNode(0.1f, 0.1f);
        Node second = AddNode(0.2f, 0.1f);
        Node third = AddNode(0.3f, 0.1f);

        Assert.IsTrue(_grid.Remove(first));
        Assert.AreEqual(2, _grid.Count);
        Assert.IsFalse(_grid.Contains(first));

        // The swap moved the bucket's last element into the freed index; both
        // survivors must still be reachable.
        Assert.IsTrue(_grid.Contains(second));
        Assert.IsTrue(_grid.Contains(third));
        Assert.IsTrue(_grid.TryGetNearest(second.Position, out Node nearest));
        Assert.AreSame(second, nearest);
    }

    [Test]
    public void Remove_SwapMovedElement_RemainsRemovable()
    {
        // Removing the first node moves the last one into its index; removing the
        // moved node afterwards catches a stale recorded index.
        Node first = AddNode(0.1f, 0.1f);
        Node second = AddNode(0.2f, 0.1f);
        Node third = AddNode(0.3f, 0.1f);

        Assert.IsTrue(_grid.Remove(first));
        Assert.IsTrue(_grid.Remove(third), "The element moved by the swap must still be removable.");
        Assert.AreEqual(1, _grid.Count);
        Assert.IsTrue(_grid.Contains(second));
    }

    [Test]
    public void TryGetNearest_EmptyGrid_ReturnsFalse()
    {
        Assert.IsFalse(_grid.TryGetNearest(Vector2.zero, out Node nearest));
        Assert.IsNull(nearest);
    }

    [Test]
    public void TryGetNearest_PicksClosestWithinSameCell()
    {
        AddNode(0.1f, 0.1f);
        Node closest = AddNode(0.4f, 0.4f);
        AddNode(0.9f, 0.9f);

        Assert.IsTrue(_grid.TryGetNearest(new Vector2(0.45f, 0.45f), out Node nearest));
        Assert.AreSame(closest, nearest);
    }

    [Test]
    public void TryGetNearest_FindsClosestAcrossNeighbourBuckets()
    {
        AddNode(0.9f, 0.5f);   // cell (0, 0)
        Node closest = AddNode(1.1f, 0.5f); // cell (1, 0)

        // The query point sits in the neighbour cell, so only a cross-bucket scan
        // can find it.
        Assert.IsTrue(_grid.TryGetNearest(new Vector2(1.05f, 0.5f), out Node nearest));
        Assert.AreSame(closest, nearest);
    }

    [Test]
    public void TryGetNearest_ExpandsPastEmptyRings()
    {
        // Cell size is 1, so this lands three rings out while rings 0..2 are empty.
        Node distant = AddNode(3.5f, 0.5f);

        Assert.IsTrue(_grid.TryGetNearest(new Vector2(0.5f, 0.5f), out Node nearest),
            "The search must keep expanding while the inner rings are empty.");
        Assert.AreSame(distant, nearest);
    }

    [Test]
    public void TryGetNearest_RespectsMaxRadius()
    {
        AddNode(5f, 0.5f);
        Vector2 query = new Vector2(0.5f, 0.5f);

        Assert.IsFalse(_grid.TryGetNearest(query, out Node _ , maxRadius: 2f),
            "A node beyond the search radius must not be returned.");
        Assert.IsTrue(_grid.TryGetNearest(query, out Node found, maxRadius: 10f));
        Assert.IsNotNull(found);
    }

    [Test]
    public void TryGetNearest_TieOnDistance_ReturnsOneOfThem()
    {
        Node left = AddNode(0.4f, 0.5f);
        Node right = AddNode(0.6f, 0.5f);

        Assert.IsTrue(_grid.TryGetNearest(new Vector2(0.5f, 0.5f), out Node nearest));
        Assert.IsTrue(nearest == left || nearest == right);
    }

    [Test]
    public void TryGetNearest_Filter_SkipsRejectedCandidate()
    {
        Node rejected = AddNode(0.2f, 0.2f);
        Node accepted = AddNode(0.6f, 0.6f);
        rejected.Alive = false;

        Assert.IsTrue(_grid.TryGetNearest(Vector2.zero, out Node nearest, filter: AliveOnly),
            "A rejected candidate must not end the search.");
        Assert.AreSame(accepted, nearest, "The nearest accepted candidate must win.");
    }

    [Test]
    public void TryGetNearest_Filter_RejectingEverything_ReturnsFalse()
    {
        AddNode(0.2f, 0.2f).Alive = false;
        AddNode(0.6f, 0.6f).Alive = false;

        Assert.IsFalse(_grid.TryGetNearest(Vector2.zero, out Node nearest, filter: AliveOnly));
        Assert.IsNull(nearest);
    }

    [Test]
    public void TryGetNearest_Filter_KeepsExpandingPastRejectedCandidates()
    {
        // The rejected node is the closest one and sits in the query's own cell, so
        // only continued ring expansion can reach the accepted node three cells out.
        Node rejected = AddNode(0.6f, 0.5f);
        Node accepted = AddNode(3.5f, 0.5f);
        rejected.Alive = false;

        Assert.IsTrue(_grid.TryGetNearest(new Vector2(0.5f, 0.5f), out Node nearest, filter: AliveOnly),
            "Filtering must not stop the search from expanding outward.");
        Assert.AreSame(accepted, nearest);
    }

    [Test]
    public void TryGetNearest_NullFilter_AcceptsEveryCandidate()
    {
        Node rejected = AddNode(0.2f, 0.2f);
        rejected.Alive = false;

        Assert.IsTrue(_grid.TryGetNearest(Vector2.zero, out Node nearest, filter: null));
        Assert.AreSame(rejected, nearest, "Without a filter liveness is irrelevant.");
    }

    [Test]
    public void Update_AfterCrossingCellBoundary_IsFoundAtNewPosition()
    {
        Node node = AddNode(0.5f, 0.5f);

        node.Position = new Vector2(20.5f, 0.5f);
        Assert.IsTrue(_grid.Update(node), "Crossing a cell boundary must re-bucket the node.");

        Assert.IsTrue(_grid.TryGetNearest(new Vector2(20.6f, 0.5f), out Node nearest));
        Assert.AreSame(node, nearest);

        // The old cell must be empty, otherwise the node is registered twice. A
        // radius bound is required here: an unlimited query legitimately keeps
        // expanding until it finds the node, no matter how far away it moved.
        Assert.IsFalse(_grid.TryGetNearest(new Vector2(0.5f, 0.5f), out Node _, maxRadius: 2f),
            "The vacated cell must no longer report the node.");
    }

    [Test]
    public void Update_WithoutCellChange_ReportsNoMove()
    {
        Node node = AddNode(0.1f, 0.1f);

        node.Position = new Vector2(0.2f, 0.2f); // same cell

        Assert.IsFalse(_grid.Update(node), "Staying inside the cell must be a cheap no-op.");
        Assert.AreEqual(1, _grid.Count);
    }

    [Test]
    public void Update_UnregisteredItem_ReturnsFalse()
    {
        Assert.IsFalse(_grid.Update(new Node(Vector2.zero)));
    }

    [Test]
    public void Remove_EmptiesBucket_BucketIsRecycledAndReusable()
    {
        Node first = AddNode(0.1f, 0.1f);
        Node second = AddNode(0.2f, 0.1f);

        Assert.IsTrue(_grid.Remove(first));
        Assert.IsTrue(_grid.Remove(second));
        Assert.AreEqual(0, _grid.Count);

        // Re-adding into the same cell exercises the pooled bucket.
        Node recycled = AddNode(0.3f, 0.3f);
        Assert.AreEqual(1, _grid.Count);
        Assert.IsTrue(_grid.TryGetNearest(new Vector2(0.35f, 0.3f), out Node nearest));
        Assert.AreSame(recycled, nearest);
    }

    [Test]
    public void RemoveAll_InScrambledOrder_LeavesGridEmptyAndConsistent()
    {
        const int total = 60;
        var nodes = new List<Node>(total);
        for (int i = 0; i < total; i++)
        {
            // Spread across cells so both the flat list and buckets get reshuffled.
            nodes.Add(AddNode(i % 10 + 0.5f, i / 10 + 0.5f));
        }
        Assert.AreEqual(total, _grid.Count);

        for (int i = 0; i < total; i++)
        {
            Node victim = nodes[(i * 7) % total];
            Assert.IsTrue(_grid.Remove(victim), $"Removal {i} failed for a registered node.");
        }

        Assert.AreEqual(0, _grid.Count);
        Assert.IsFalse(_grid.TryGetNearest(Vector2.zero, out Node _));
        Assert.IsFalse(_grid.Contains(nodes[0]));
    }

    [Test]
    public void Items_ReflectAddsAndRemoves()
    {
        Node first = AddNode(0.1f, 0.1f);
        Node second = AddNode(5.1f, 5.1f);

        Assert.AreEqual(2, _grid.Items.Count);

        _grid.Remove(first);

        Assert.AreEqual(1, _grid.Items.Count);
        Assert.AreSame(second, _grid.Items[0]);
    }

    [Test]
    public void Clear_DropsEverything()
    {
        AddNode(0.1f, 0.1f);
        AddNode(3.1f, 3.1f);

        _grid.Clear();

        Assert.AreEqual(0, _grid.Count);
        Assert.IsFalse(_grid.TryGetNearest(Vector2.zero, out Node _));
    }
}
