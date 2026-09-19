using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scatters the wave's cover and traps inside the terrain area.
///
/// Placement is grid work, not free-form scattering: every prop is snapped to
/// the tile lattice, its whole footprint must be free, and it must clear the
/// reserved zone around the map centre where the player spawns and the portal
/// appears. After the cover is down the placer walks the free cells from the
/// centre and, if the cover has cut the arena into pockets the player could be
/// trapped in, it takes the offending pieces back out - a wall that seals a
/// corner is worse than no wall.
///
/// Props are plain instantiations destroyed on the next wave rather than pooled:
/// they are few, they live exactly one wave, and their state is the prefab's.
/// </summary>
public class PropPlacer : MonoBehaviour
{
    /// <summary>One prop type in a scatter table: the prefab and how many to place.</summary>
    [Serializable]
    public class PropEntry
    {
        [Tooltip("Prop prefab to place. Its PropFootprint declares the tiles it needs.")]
        [SerializeField] private GameObject _prefab;
        [Tooltip("How many of this prop to place this wave. Zero or less places none.")]
        [SerializeField] private int _count = 1;

        /// <summary>Prop prefab to place.</summary>
        public GameObject Prefab => _prefab;

        /// <summary>How many to place.</summary>
        public int Count => _count;

        /// <summary>Builds an entry in code, for tests and generated waves.</summary>
        /// <param name="prefab">Prop prefab.</param>
        /// <param name="count">How many to place.</param>
        public PropEntry(GameObject prefab, int count)
        {
            _prefab = prefab;
            _count = count;
        }
    }

    /// <summary>A placed prop, kept so a failed connectivity check can take it back.</summary>
    private struct Placement
    {
        public GameObject Instance;
        public Vector2Int Min;
        public Vector2Int Max;
        public bool IsObstacle;
    }

    [Header("Source")]
    [Tooltip("Terrain generator whose effective rectangle bounds the scatter.")]
    [SerializeField] private TerrainGenerator _terrain;

    [Header("Tables")]
    [Tooltip("Cover pieces placed this wave (pillars, wall segments, L pieces, rubble, stairs).")]
    [SerializeField] private PropEntry[] _obstacles;
    [Tooltip("Traps placed this wave (spikes).")]
    [SerializeField] private PropEntry[] _hazards;

    [Header("Rules")]
    [Tooltip("Units around the map centre (player spawn and portal) kept clear of every prop.")]
    [SerializeField] private float _centerClearRadius = 3.5f;
    [Tooltip("Minimum distance in tiles between two traps.")]
    [SerializeField] private float _hazardSpacing = 2.5f;
    [Tooltip("Tries per prop before it is skipped for this wave.")]
    [SerializeField] private int _attemptsPerProp = 40;
    [Tooltip("Share of free tiles that must stay reachable from the centre, or the cover is rolled back.")]
    [Range(0f, 1f)][SerializeField] private float _minReachableFraction = 0.6f;

    /// <summary>Occupancy of the current wave's scatter.</summary>
    private readonly TileOccupancy _occupancy = new TileOccupancy();

    /// <summary>Everything placed this wave, in placement order.</summary>
    private readonly List<Placement> _placements = new List<Placement>(64);

    /// <summary>Tile centres of traps already placed, for spacing checks.</summary>
    private readonly List<Vector2Int> _hazardTiles = new List<Vector2Int>(16);

    /// <summary>Container the props live under, so a wave switch reclaims them in one step.</summary>
    private Transform _container;

    /// <summary>Cover pieces actually standing after the connectivity rollback.</summary>
    public int ObstacleCount { get; private set; }

    /// <summary>Traps actually placed.</summary>
    public int HazardCount { get; private set; }

    /// <summary>
    /// Rejects a placer with no terrain source, like the project's other
    /// configurable components: inert and loud beats an arena with silent holes.
    /// </summary>
    private void Awake()
    {
        if (_terrain == null)
        {
            Debug.LogError($"PropPlacer on '{name}' has no terrain generator and will place nothing.", this);
            enabled = false;
            return;
        }

        _container = new GameObject("Props").transform;
        _container.SetParent(transform, worldPositionStays: false);
    }

    /// <summary>
    /// Clears the previous wave and scatters a new one from the given seed. The
    /// same seed reproduces the same layout.
    ///
    /// The tier supplies the totals and the tables supply the mix: an entry's
    /// count is its share of the target, so retuning how many pieces a wave
    /// carries is a tier edit while retuning which kinds appear is a table edit.
    /// </summary>
    /// <param name="seed">Layout seed for this wave.</param>
    /// <param name="obstacleTarget">Cover pieces to place this wave.</param>
    /// <param name="hazardTarget">Traps to place this wave.</param>
    public void Place(int seed, int obstacleTarget, int hazardTarget)
    {
        if (!enabled)
        {
            return;
        }

        Clear();

        Rect terrain = _terrain.TerrainRect();
        RectInt bounds = new RectInt(
            ArenaGrid.TileIndex(terrain.xMin),
            ArenaGrid.TileIndex(terrain.yMin),
            Mathf.RoundToInt(terrain.width / ArenaGrid.TileSize),
            Mathf.RoundToInt(terrain.height / ArenaGrid.TileSize));
        _occupancy.SetBounds(bounds);

        System.Random random = new System.Random(seed);
        PlaceTable(_obstacles, obstacleTarget, true, random);
        ResolveConnectivity(bounds);
        PlaceTable(_hazards, hazardTarget, false, random);
    }

    /// <summary>
    /// Destroys every prop of the previous wave.
    /// </summary>
    public void Clear()
    {
        if (_container != null)
        {
            for (int i = _container.childCount - 1; i >= 0; i--)
            {
                Destroy(_container.GetChild(i).gameObject);
            }
        }

        _placements.Clear();
        _hazardTiles.Clear();
        _occupancy.Clear();
        ObstacleCount = 0;
        HazardCount = 0;
    }

    /// <summary>
    /// Places one table of props, honouring footprints, the centre clear zone
    /// and, for traps, the spacing rule.
    /// </summary>
    /// <param name="entries">Table to place; empty or missing entries place nothing.</param>
    /// <param name="target">How many pieces the wave wants from this table.</param>
    /// <param name="isObstacle">True for cover, false for traps (affects bookkeeping).</param>
    /// <param name="random">Seeded source so a wave reproduces its layout.</param>
    private void PlaceTable(PropEntry[] entries, int target, bool isObstacle, System.Random random)
    {
        if (entries == null || target <= 0)
        {
            return;
        }

        float tableTotal = 0f;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] != null && entries[i].Prefab != null)
            {
                tableTotal += Mathf.Max(0, entries[i].Count);
            }
        }

        if (tableTotal <= 0f)
        {
            return;
        }

        for (int e = 0; e < entries.Length; e++)
        {
            PropEntry entry = entries[e];
            if (entry == null || entry.Prefab == null || entry.Count <= 0)
            {
                continue;
            }

            // The entry's count is its share of the wave's target, so a table
            // authored as 3:2:2:1:1:1 places those proportions of the total.
            int wanted = Mathf.Max(1, Mathf.RoundToInt(target * entry.Count / tableTotal));

            PropFootprint footprint = entry.Prefab.GetComponent<PropFootprint>();
            bool rotatable = footprint != null && footprint.Rotatable;
            if (footprint == null)
            {
                Debug.LogWarning($"Prop '{entry.Prefab.name}' has no PropFootprint; treating it as one tile.", entry.Prefab);
            }

            int placed = 0;
            for (int attempt = 0; attempt < wanted * _attemptsPerProp && placed < wanted; attempt++)
            {
                int quarterTurns = rotatable ? random.Next(4) : 0;
                Vector2Int size = footprint != null ? footprint.SizeAfter(quarterTurns) : Vector2Int.one;

                if (!TryFindSpot(size, isObstacle, random, out Vector2 center, out Vector2Int min, out Vector2Int max))
                {
                    continue;
                }

                Spawn(entry.Prefab, center, quarterTurns, min, max, isObstacle);
                placed++;
            }
        }
    }

    /// <summary>
    /// Finds one free, aligned spot for a footprint, or reports failure.
    /// </summary>
    /// <param name="size">Footprint in tiles after rotation.</param>
    /// <param name="isObstacle">True for cover, false for traps.</param>
    /// <param name="random">Seeded source.</param>
    /// <param name="center">Aligned world centre when a spot was found.</param>
    /// <param name="min">Minimum tile index covered.</param>
    /// <param name="max">One past the maximum tile index covered.</param>
    /// <returns>Whether a spot was found.</returns>
    private bool TryFindSpot(Vector2Int size, bool isObstacle, System.Random random,
        out Vector2 center, out Vector2Int min, out Vector2Int max)
    {
        center = Vector2.zero;
        min = Vector2Int.zero;
        max = Vector2Int.zero;

        RectInt bounds = _occupancy.Bounds;
        for (int attempt = 0; attempt < _attemptsPerProp; attempt++)
        {
            int x = random.Next(bounds.xMin, bounds.xMax);
            int y = random.Next(bounds.yMin, bounds.yMax);
            Vector2 candidate = ArenaGrid.SnapCenter(
                new Vector2(x + 0.5f, y + 0.5f),
                new Vector2(size.x * ArenaGrid.TileSize, size.y * ArenaGrid.TileSize));
            ArenaGrid.TilesCovered(candidate, new Vector2(size.x * ArenaGrid.TileSize, size.y * ArenaGrid.TileSize),
                out Vector2Int cellMin, out Vector2Int cellMax);

            if (!_occupancy.IsRectFree(cellMin, cellMax))
            {
                continue;
            }

            if (!ClearsCentre(candidate, size))
            {
                continue;
            }

            if (!isObstacle && !ClearsOtherHazards(candidate))
            {
                continue;
            }

            center = candidate;
            min = cellMin;
            max = cellMax;
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when a footprint keeps clear of the reserved zone around the map
    /// centre, where the player spawns and the portal later appears.
    /// </summary>
    /// <param name="center">Footprint centre.</param>
    /// <param name="size">Footprint in tiles.</param>
    /// <returns>Whether the centre zone is respected.</returns>
    private bool ClearsCentre(Vector2 center, Vector2Int size)
    {
        Vector2 half = new Vector2(size.x, size.y) * (ArenaGrid.TileSize * 0.5f);
        // Nearest point of the footprint rectangle to the origin.
        float nearestX = Mathf.Clamp(0f, center.x - half.x, center.x + half.x);
        float nearestY = Mathf.Clamp(0f, center.y - half.y, center.y + half.y);
        return nearestX * nearestX + nearestY * nearestY >= _centerClearRadius * _centerClearRadius;
    }

    /// <summary>
    /// True when a trap is far enough from every trap already placed.
    /// </summary>
    /// <param name="center">Candidate centre.</param>
    /// <returns>Whether the spacing rule is met.</returns>
    private bool ClearsOtherHazards(Vector2 center)
    {
        float minSq = _hazardSpacing * _hazardSpacing;
        for (int i = 0; i < _hazardTiles.Count; i++)
        {
            Vector2 other = ArenaGrid.TileCenter(_hazardTiles[i].x, _hazardTiles[i].y);
            if ((other - center).sqrMagnitude < minSq)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Instantiates a prop, books its tiles and records it for a possible rollback.
    /// </summary>
    /// <param name="prefab">Prop prefab.</param>
    /// <param name="center">Aligned world centre.</param>
    /// <param name="quarterTurns">Rotation applied.</param>
    /// <param name="min">Minimum tile covered.</param>
    /// <param name="max">One past the maximum tile covered.</param>
    /// <param name="isObstacle">True for cover, false for traps.</param>
    private void Spawn(GameObject prefab, Vector2 center, int quarterTurns, Vector2Int min, Vector2Int max, bool isObstacle)
    {
        GameObject instance = Instantiate(prefab, center, Quaternion.Euler(0f, 0f, quarterTurns * 90f));
        instance.transform.SetParent(_container, worldPositionStays: true);

        _occupancy.SetRectOccupied(min, max, true);
        Placement placement;
        placement.Instance = instance;
        placement.Min = min;
        placement.Max = max;
        placement.IsObstacle = isObstacle;
        _placements.Add(placement);

        if (isObstacle)
        {
            ObstacleCount++;
        }
        else
        {
            HazardCount++;
            _hazardTiles.Add(new Vector2Int(min.x, min.y));
        }
    }

    /// <summary>
    /// Walks the free cells from the map centre and takes cover back out while
    /// too little of the arena is reachable, so a placement can never seal the
    /// player into a pocket.
    /// </summary>
    /// <param name="bounds">Tile bounds of the terrain area.</param>
    private void ResolveConnectivity(RectInt bounds)
    {
        Vector2Int start = new Vector2Int(
            ArenaGrid.TileIndex(0f), ArenaGrid.TileIndex(0f));

        int guard = _placements.Count + 1;
        while (guard-- > 0)
        {
            int reachable = _occupancy.ReachableFrom(start);
            if (_occupancy.FreeCount <= 0 || reachable >= Mathf.CeilToInt(_occupancy.FreeCount * _minReachableFraction))
            {
                return;
            }

            int lastObstacle = LastObstacleIndex();
            if (lastObstacle < 0)
            {
                return;
            }

            Placement placement = _placements[lastObstacle];
            _occupancy.SetRectOccupied(placement.Min, placement.Max, false);
            if (placement.Instance != null)
            {
                Destroy(placement.Instance);
            }
            _placements.RemoveAt(lastObstacle);
            ObstacleCount = Mathf.Max(0, ObstacleCount - 1);
        }
    }

    /// <summary>
    /// Index of the most recently placed cover piece, or -1 when none is left.
    /// </summary>
    /// <returns>Index into the placement list.</returns>
    private int LastObstacleIndex()
    {
        for (int i = _placements.Count - 1; i >= 0; i--)
        {
            if (_placements[i].IsObstacle)
            {
                return i;
            }
        }
        return -1;
    }
}
