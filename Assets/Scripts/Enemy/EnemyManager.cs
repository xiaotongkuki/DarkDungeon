using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-wide registry of live enemies, backed by a spatial grid.
///
/// It exists so gameplay systems can ask "which enemy is closest?" without
/// FindGameObjectsWithTag-style allocation or a full-population scan every
/// frame, and so spawn/kill bookkeeping lives in exactly one place.
/// </summary>
public class EnemyManager : MonoBehaviour
{
    /// <summary>The active manager, claimed in Awake and released on destroy.</summary>
    public static EnemyManager Instance { get; private set; }

    [Header("Spatial Grid")]
    [Tooltip("World units per grid cell. Smaller cells sharpen nearest-enemy queries but touch more " +
             "cells per query; roughly the typical gap between enemies is a good starting point.")]
    [SerializeField] private float _cellSize = 4f;

    /// <summary>Capacity hint so the first waves do not resize the backing collections.</summary>
    private const int InitialCapacity = 256;

    /// <summary>
    /// Query filter that hides enemies which are dead but not yet out of the grid.
    ///
    /// Removal is deferred to the end of the frame (see <see cref="Unregister"/>),
    /// so for the rest of the frame a corpse is still registered: it counts toward
    /// <see cref="ActiveCount"/> and would otherwise be returned as a target.
    /// Filtering at the query layer means callers get only live enemies without
    /// having to remember any convention. Held in a static field so the query
    /// stays allocation-free.
    /// </summary>
    private static readonly Func<Enemy, bool> AliveOnlyFilter = e => e.IsAlive;

    private SpatialGrid<Enemy> _grid;

    /// <summary>
    /// Enemies waiting to leave the grid. Removal is deferred because callers may
    /// be iterating buckets when an enemy dies (area damage, piercing shots), and
    /// mutating a bucket mid-iteration would corrupt the loop.
    /// </summary>
    private readonly List<Enemy> _pendingRemovals = new List<Enemy>();

    /// <summary>Live enemy count, including any queued for removal this frame.</summary>
    public int ActiveCount => _grid != null ? _grid.Count : 0;

    /// <summary>
    /// Builds the grid and claims the singleton slot. A second manager in the
    /// scene disables itself so every query hits the same registry.
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"Duplicate EnemyManager on '{name}' removed; keeping '{Instance.name}'.", this);
            Destroy(this);
            return;
        }

        Instance = this;
        _grid = new SpatialGrid<Enemy>(_cellSize, e => e.transform.position, InitialCapacity);
    }

    /// <summary>
    /// Releases the singleton slot so a later manager (after a scene reload or in
    /// a test teardown) can take over, and drops the registry.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            _grid?.Clear();
        }
        _pendingRemovals.Clear();
    }

    /// <summary>
    /// Adds an enemy to the registry. Called by <see cref="Enemy.OnEnable"/>;
    /// duplicate registrations are ignored.
    ///
    /// Any removal still queued for this enemy is dropped first. A disable/enable
    /// cycle (which is exactly what object pooling does) queues a removal in
    /// OnDisable and registers again in OnEnable; without this the queued removal
    /// would run at the end of the frame and silently evict the enemy that just
    /// came back.
    /// </summary>
    /// <param name="enemy">Enemy entering play.</param>
    public void Register(Enemy enemy)
    {
        if (_grid == null)
        {
            return;
        }

        _pendingRemovals.Remove(enemy);
        _grid.Add(enemy);
    }

    /// <summary>
    /// Queues an enemy to leave the registry at the end of the frame. Queued
    /// duplicates are harmless: the second removal finds nothing and is skipped.
    ///
    /// The null test deliberately uses <see cref="object.ReferenceEquals"/> rather
    /// than Unity's overloaded operator: during OnDisable the object is already
    /// flagged destroyed, so <c>enemy == null</c> would be true and the removal
    /// would be silently dropped, leaking the entry forever.
    /// </summary>
    /// <param name="enemy">Enemy leaving play.</param>
    public void Unregister(Enemy enemy)
    {
        if (ReferenceEquals(enemy, null) || _grid == null)
        {
            return;
        }
        _pendingRemovals.Add(enemy);
    }

    /// <summary>
    /// Re-buckets an enemy after it moved. Cheap and safe to call every frame: the
    /// grid only touches its dictionaries when the enemy actually crossed a cell.
    /// </summary>
    /// <param name="enemy">Registered enemy whose position may have changed.</param>
    public void RefreshPosition(Enemy enemy)
    {
        if (ReferenceEquals(enemy, null))
        {
            return;
        }
        _grid?.Update(enemy);
    }

    /// <summary>
    /// Applies the queued removals. Public so tests and teardown paths can force a
    /// flush instead of waiting for the next frame.
    /// </summary>
    public void FlushRemovals()
    {
        if (_grid == null || _pendingRemovals.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _pendingRemovals.Count; i++)
        {
            _grid.Remove(_pendingRemovals[i]);
        }
        _pendingRemovals.Clear();
    }

    /// <summary>
    /// Takes every live enemy off the field without calling it a kill. The stage
    /// switch uses this: the stage is over, the wave is not dying, so the
    /// despawn must not raise score, drops or experience. Removals are flushed
    /// right away so a caller that clears and immediately rebuilds sees an empty
    /// registry, not one full of corpses scheduled to leave.
    /// </summary>
    public void DespawnAll()
    {
        if (_grid == null)
        {
            return;
        }

        IReadOnlyList<Enemy> items = _grid.Items;
        // Descending is a habit rather than a need here: grid mutation is
        // deferred, so the flat list is stable across the loop in either order.
        for (int i = items.Count - 1; i >= 0; i--)
        {
            items[i]?.ForceDespawn();
        }
        FlushRemovals();
    }

    /// <summary>
    /// Flushes queued removals once per frame, after every Update-phase system has
    /// finished reading the grid.
    /// </summary>
    private void LateUpdate()
    {
        FlushRemovals();
    }

    /// <summary>
    /// Finds the enemy closest to a world point, scanning only the grid cells
    /// around it instead of the whole population.
    ///
    /// Dead enemies are filtered out even before their deferred removal runs, so
    /// the result is always a targetable enemy — a corpse is never returned.
    /// </summary>
    /// <param name="point">Query position in world space.</param>
    /// <param name="nearest">Closest live enemy, or null when none was found.</param>
    /// <param name="maxRadius">Search radius in world units; 0 means unlimited.</param>
    /// <returns>True when a live enemy was found.</returns>
    public bool TryGetNearest(Vector2 point, out Enemy nearest, float maxRadius = 0f)
    {
        if (_grid == null)
        {
            nearest = null;
            return false;
        }
        return _grid.TryGetNearest(point, out nearest, maxRadius, 0, AliveOnlyFilter);
    }
}
