using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Builds the arena floor as three tilemap layers, and can rebuild it for every
/// wave of an endless run.
///
/// The layers exist because the three jobs want different rules:
/// <list type="bullet">
/// <item><c>ground</c> covers the whole walled area in one tile - it is the
/// canvas, and it must reach under the walls so no seam shows at the edge;</item>
/// <item><c>detail</c> replaces a share of the ground inside the terrain area
/// with variant tiles (cracks, rubble, stains). Variants are chosen by noise so
/// they clump into patches instead of sprinkling evenly, which is what makes a
/// floor read as a place rather than as static;</item>
/// <item><c>decor</c> overlays sparse transparent art (bones, grates) on top,
/// with a minimum spacing so two props never stack into a blob.</item>
/// </list>
///
/// The terrain area is authored through <see cref="ArenaArea"/>; the floor area
/// is read from the walls, which the designer owns. Nothing here writes to the
/// walls, the player or the props - it only fills tilemaps.
/// </summary>
public class TerrainGenerator : MonoBehaviour
{
    [Header("Areas")]
    [Tooltip("Rectangle terrain detail and decor are generated in (the combat area).")]
    [SerializeField] private ArenaArea _terrainArea;
    [Tooltip("Walls whose interior bounds the floor. Read-only: they are the designer's objects.")]
    [SerializeField] private Transform _wallTop;
    [SerializeField] private Transform _wallBottom;
    [SerializeField] private Transform _wallLeft;
    [SerializeField] private Transform _wallRight;
    [Tooltip("Extra units the floor extends past the wall interior on every side, so the floor tucks under the walls.")]
    [SerializeField] private float _floorOverlap = 1f;

    [Header("Tilemaps")]
    [Tooltip("Base floor layer, filled with the ground tile.")]
    [SerializeField] private Tilemap _ground;
    [Tooltip("Variant layer drawn over the ground inside the terrain area.")]
    [SerializeField] private Tilemap _detail;
    [Tooltip("Sparse overlay layer for transparent decorations.")]
    [SerializeField] private Tilemap _decor;

    [Header("Palette")]
    [Tooltip("The one tile the whole floor is laid with.")]
    [SerializeField] private TileBase _groundTile;
    [Tooltip("Variant tiles used for detail patches.")]
    [SerializeField] private TileBase[] _detailTiles;
    [Tooltip("Relative chance of each detail tile; a shorter list pads with ones.")]
    [SerializeField] private float[] _detailWeights;
    [Tooltip("Transparent overlay tiles used for decoration.")]
    [SerializeField] private TileBase[] _decorTiles;

    [Header("Density")]
    [Tooltip("Share of terrain-area tiles replaced by a detail variant (0..1).")]
    [Range(0f, 1f)][SerializeField] private float _detailDensity = 0.22f;
    [Tooltip("Noise frequency for detail patches. Smaller makes larger, fewer patches.")]
    [SerializeField] private float _detailNoiseScale = 0.18f;
    [Tooltip("Share of terrain-area tiles that receive a decoration (0..1).")]
    [Range(0f, 1f)][SerializeField] private float _decorDensity = 0.04f;
    [Tooltip("Minimum distance in tiles between two decorations.")]
    [SerializeField] private float _decorMinSpacing = 2.5f;

    /// <summary>Scratch lists reused across generations so a wave switch allocates almost nothing.</summary>
    private readonly List<Vector3Int> _positions = new List<Vector3Int>(4096);
    private readonly List<TileBase> _tiles = new List<TileBase>(4096);
    private readonly List<Vector2Int> _placedDecor = new List<Vector2Int>(256);

    /// <summary>Set once the terrain area is found to reach past the walls, so the warning stays single.</summary>
    private bool _clampWarned;

    /// <summary>
    /// Rejects a generator with a missing reference, the same way the project's
    /// other configurable components do: disabled on awake means inert and loud
    /// rather than a silently empty floor.
    /// </summary>
    private void Awake()
    {
        if (_terrainArea == null || _ground == null || _detail == null || _decor == null || _groundTile == null)
        {
            Debug.LogError($"TerrainGenerator on '{name}' is missing a reference and will not build a floor.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Rebuilds all three layers from scratch with the given seed, so the same
    /// wave number always produces the same floor and a new wave produces a new
    /// one.
    /// </summary>
    /// <param name="seed">Seed for the layout; the same seed reproduces the floor.</param>
    public void Generate(int seed)
    {
        if (!enabled)
        {
            return;
        }

        // All randomness here comes from System.Random seeded with the wave, so
        // the terrain never touches UnityEngine.Random: a layout rebuild cannot
        // shift where enemies roll or which upgrade is offered.
        Clear();
        PublishBounds();
        FillGround();
        FillDetail(seed);
        FillDecor(seed);
    }

    /// <summary>
    /// Republishes the arena geometry for the camera and the spawner: the walls
    /// bound the playable area, and the camera may roam out to the floor's edge
    /// so the walls are actually seen.
    /// </summary>
    private void PublishBounds()
    {
        Rect interior = ArenaGrid.SnapRect(WallInterior());
        Rect floor = FloorRect();
        StageBounds.SetSize(new Vector2(interior.width, interior.height));
        StageBounds.SetCameraSize(new Vector2(floor.width, floor.height));
    }

    /// <summary>
    /// Empties every layer. Called before each rebuild.
    /// </summary>
    public void Clear()
    {
        _ground.ClearAllTiles();
        _detail.ClearAllTiles();
        _decor.ClearAllTiles();
    }

    /// <summary>
    /// The rectangle the floor covers: the wall interior grown by the overlap,
    /// snapped outward to whole tiles so the edge is never a sliver.
    /// </summary>
    /// <returns>Floor rectangle in world space.</returns>
    public Rect FloorRect()
    {
        Rect interior = WallInterior();
        return ArenaGrid.SnapRect(new Rect(
            interior.xMin - _floorOverlap,
            interior.yMin - _floorOverlap,
            interior.width + _floorOverlap * 2f,
            interior.height + _floorOverlap * 2f));
    }

    /// <summary>
    /// The rectangle detail and decor are generated in, snapped to the lattice
    /// and clamped to the wall interior.
    ///
    /// The clamp is what keeps a hand-authored area honest: if the area reaches
    /// past the walls, props would be placed where the player cannot walk and
    /// where the camera never looks. Clamping means the walls always win, and a
    /// warning tells the designer the area is being cut rather than silently
    /// shrinking it.
    /// </summary>
    /// <returns>Terrain rectangle in world space.</returns>
    public Rect TerrainRect()
    {
        Rect area = _terrainArea.SnappedRect;
        Rect interior = ArenaGrid.SnapRect(WallInterior());

        float minX = Mathf.Max(area.xMin, interior.xMin);
        float minY = Mathf.Max(area.yMin, interior.yMin);
        float maxX = Mathf.Min(area.xMax, interior.xMax);
        float maxY = Mathf.Min(area.yMax, interior.yMax);

        if (maxX <= minX || maxY <= minY)
        {
            // No overlap at all: the authored area sits outside the walls. The
            // interior is still the only sensible place to build.
            WarnClampedOnce();
            return interior;
        }

        Rect clamped = Rect.MinMaxRect(minX, minY, maxX, maxY);
        if (!Mathf.Approximately(clamped.xMin, area.xMin) || !Mathf.Approximately(clamped.xMax, area.xMax) ||
            !Mathf.Approximately(clamped.yMin, area.yMin) || !Mathf.Approximately(clamped.yMax, area.yMax))
        {
            WarnClampedOnce();
        }

        return clamped;
    }

    /// <summary>
    /// Reports once that the authored terrain area reaches past the walls, so a
    /// too-large area is noticed without spamming the console every wave.
    /// </summary>
    private void WarnClampedOnce()
    {
        if (_clampWarned)
        {
            return;
        }

        _clampWarned = true;
        Debug.LogWarning(
            $"TerrainGenerator on '{name}': the terrain area reaches past the walls and was clamped to the wall " +
            "interior. Move the walls out, or shrink the ArenaArea, to use the full area.", this);
    }

    /// <summary>
    /// The wall interior in world space. Walls are read through their collider
    /// when they have one, and through their renderer otherwise; a missing wall
    /// falls back to the terrain area so the floor still appears.
    /// </summary>
    /// <returns>Interior rectangle in world space.</returns>
    private Rect WallInterior()
    {
        if (!TryWallBounds(_wallTop, out Bounds top) || !TryWallBounds(_wallBottom, out Bounds bottom) ||
            !TryWallBounds(_wallLeft, out Bounds left) || !TryWallBounds(_wallRight, out Bounds right))
        {
            Rect fallback = _terrainArea.SnappedRect;
            return fallback;
        }

        float minX = Mathf.Max(left.min.x, Mathf.Min(top.min.x, bottom.min.x));
        float maxX = Mathf.Min(right.max.x, Mathf.Max(top.max.x, bottom.max.x));
        float minY = Mathf.Max(bottom.min.y, Mathf.Min(left.min.y, right.min.y));
        float maxY = Mathf.Min(top.max.y, Mathf.Max(left.max.y, right.max.y));
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Reads one wall's world bounds, preferring its collider (the physics
    /// footprint the player actually hits) and falling back to its renderer.
    /// </summary>
    /// <param name="wall">Wall transform; may be null.</param>
    /// <param name="bounds">Wall bounds in world space.</param>
    /// <returns>Whether bounds were found.</returns>
    private static bool TryWallBounds(Transform wall, out Bounds bounds)
    {
        bounds = default;
        if (wall == null)
        {
            return false;
        }

        Collider2D collider = wall.GetComponent<Collider2D>();
        if (collider != null)
        {
            bounds = collider.bounds;
            return true;
        }

        Renderer renderer = wall.GetComponent<Renderer>();
        if (renderer != null)
        {
            bounds = renderer.bounds;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Lays the ground tile over the whole floor rectangle.
    /// </summary>
    private void FillGround()
    {
        Rect rect = FloorRect();
        int minX = ArenaGrid.TileIndex(rect.xMin);
        int minY = ArenaGrid.TileIndex(rect.yMin);
        int maxX = ArenaGrid.TileIndex(rect.xMax - 0.001f);
        int maxY = ArenaGrid.TileIndex(rect.yMax - 0.001f);

        _positions.Clear();
        _tiles.Clear();
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                _positions.Add(new Vector3Int(x, y, 0));
                _tiles.Add(_groundTile);
            }
        }
        _ground.SetTiles(_positions.ToArray(), _tiles.ToArray());
    }

    /// <summary>
    /// Replaces the noisiest share of the terrain area with detail variants.
    ///
    /// Picking the top slice of a noise field (rather than thresholding a random
    /// roll per tile) does two things at once: the density is exact - the share
    /// asked for is the share painted - and because noise is spatially
    /// correlated the picks arrive as patches, which is what a worn floor looks
    /// like.
    /// </summary>
    /// <param name="seed">Seed offset for the noise field.</param>
    private void FillDetail(int seed)
    {
        if (_detailTiles == null || _detailTiles.Length == 0 || _detailDensity <= 0f)
        {
            return;
        }

        Rect rect = TerrainRect();
        int minX = ArenaGrid.TileIndex(rect.xMin);
        int minY = ArenaGrid.TileIndex(rect.yMin);
        int maxX = ArenaGrid.TileIndex(rect.xMax - 0.001f);
        int maxY = ArenaGrid.TileIndex(rect.yMax - 0.001f);

        List<(Vector3Int cell, float noise)> samples = new List<(Vector3Int, float)>((maxX - minX + 1) * (maxY - minY + 1));
        System.Random random = new System.Random(seed);
        float offsetX = (float)(random.NextDouble() * 1000.0);
        float offsetY = (float)(random.NextDouble() * 1000.0);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float noise = Mathf.PerlinNoise(
                    (x + offsetX) * _detailNoiseScale,
                    (y + offsetY) * _detailNoiseScale);
                samples.Add((new Vector3Int(x, y, 0), noise));
            }
        }

        samples.Sort((a, b) => b.noise.CompareTo(a.noise));
        int wanted = Mathf.Clamp(Mathf.RoundToInt(samples.Count * _detailDensity), 0, samples.Count);

        _positions.Clear();
        _tiles.Clear();
        for (int i = 0; i < wanted; i++)
        {
            _positions.Add(samples[i].cell);
            _tiles.Add(PickDetailTile(random));
        }
        _detail.SetTiles(_positions.ToArray(), _tiles.ToArray());
    }

    /// <summary>
    /// Draws one detail variant from the weighted palette.
    /// </summary>
    /// <param name="random">Seeded source, so a wave reproduces its own floor.</param>
    /// <returns>The chosen variant tile.</returns>
    private TileBase PickDetailTile(System.Random random)
    {
        float total = 0f;
        for (int i = 0; i < _detailTiles.Length; i++)
        {
            total += WeightAt(i);
        }

        if (total <= 0f)
        {
            return _detailTiles[random.Next(_detailTiles.Length)];
        }

        double roll = random.NextDouble() * total;
        for (int i = 0; i < _detailTiles.Length; i++)
        {
            roll -= WeightAt(i);
            if (roll <= 0.0)
            {
                return _detailTiles[i];
            }
        }

        return _detailTiles[_detailTiles.Length - 1];
    }

    /// <summary>
    /// The weight of a palette entry, defaulting to one when the weights array
    /// is shorter than the palette.
    /// </summary>
    /// <param name="index">Palette index.</param>
    /// <returns>The entry's weight.</returns>
    private float WeightAt(int index)
    {
        if (_detailWeights == null || index >= _detailWeights.Length)
        {
            return 1f;
        }
        return Mathf.Max(0f, _detailWeights[index]);
    }

    /// <summary>
    /// Scatters decorations over the terrain area, refusing a spot that is too
    /// close to an already placed one so the overlay reads as scattered props
    /// rather than clumps.
    /// </summary>
    /// <param name="seed">Seed for the scatter.</param>
    private void FillDecor(int seed)
    {
        if (_decorTiles == null || _decorTiles.Length == 0 || _decorDensity <= 0f)
        {
            return;
        }

        Rect rect = TerrainRect();
        int minX = ArenaGrid.TileIndex(rect.xMin);
        int minY = ArenaGrid.TileIndex(rect.yMin);
        int maxX = ArenaGrid.TileIndex(rect.xMax - 0.001f);
        int maxY = ArenaGrid.TileIndex(rect.yMax - 0.001f);
        int total = (maxX - minX + 1) * (maxY - minY + 1);

        System.Random random = new System.Random(seed ^ 0x5f3759df);
        int wanted = Mathf.RoundToInt(total * _decorDensity);
        _placedDecor.Clear();

        _positions.Clear();
        _tiles.Clear();

        int attempts = wanted * 30 + 30;
        while (_placedDecor.Count < wanted && attempts-- > 0)
        {
            int x = random.Next(minX, maxX + 1);
            int y = random.Next(minY, maxY + 1);
            if (!IsDecorSpotFree(x, y))
            {
                continue;
            }

            _placedDecor.Add(new Vector2Int(x, y));
            _positions.Add(new Vector3Int(x, y, 0));
            _tiles.Add(_decorTiles[random.Next(_decorTiles.Length)]);
        }

        _decor.SetTiles(_positions.ToArray(), _tiles.ToArray());
    }

    /// <summary>
    /// True when a tile is far enough from every decoration placed so far.
    /// </summary>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <returns>Whether the spot may host a decoration.</returns>
    private bool IsDecorSpotFree(int x, int y)
    {
        float minSq = _decorMinSpacing * _decorMinSpacing;
        for (int i = 0; i < _placedDecor.Count; i++)
        {
            Vector2Int other = _placedDecor[i];
            float dx = other.x - x;
            float dy = other.y - y;
            if (dx * dx + dy * dy < minSq)
            {
                return false;
            }
        }
        return true;
    }
}
