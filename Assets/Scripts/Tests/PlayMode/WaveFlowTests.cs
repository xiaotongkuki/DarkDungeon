using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

/// <summary>
/// Integration test for the endless wave loop on a synthetic arena: a wave
/// builds terrain and props, hands out a fixed number of enemies, stops, opens
/// the portal once the field is clear, and the next wave is one step harder.
///
/// The rig is built in code rather than loaded from the scene so the test owns
/// every number it asserts on; the scene's own wiring is covered by the asset
/// tests and by play-mode smoke runs.
/// </summary>
public class WaveFlowTests
{
    private GameObject _cameraObject;
    private GameObject _managerObject;
    private GameObject _spawnerObject;
    private GameObject _prefabSource;
    private GameObject _terrainObject;
    private GameObject _areaObject;
    private GameObject _placerObject;
    private GameObject _waveObject;
    private GameObject _playerObject;
    private GameObject _portalObject;
    private GameObject _fadeObject;
    private GameObject _gemObject;
    private GameObject[] _walls;
    private WaveController _wave;
    private EnemySpawner _spawner;
    private TerrainGenerator _terrain;
    private PropPlacer _placer;
    private StagePortal _portal;
    private Tilemap _groundTilemap;

    /// <summary>
    /// Builds the rig: camera, enemy registry, spawner with a one-prefab table,
    /// a walled terrain area with real tilemaps, the prop placer, a portal, a
    /// fast fade, a player stand-in and a wave controller with two one-tier
    /// rules. Objects that validate their references are created inactive and
    /// woken after wiring.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _cameraObject = new GameObject("TestCamera");
        _cameraObject.tag = "MainCamera";
        Camera camera = _cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        _cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        _prefabSource = new GameObject("EnemyPrefab");
        _prefabSource.SetActive(false);
        Enemy prefabEnemy = _prefabSource.AddComponent<Enemy>();
        TestData.SetObjectReference(prefabEnemy, "_data", TestData.CreateEnemyData());

        _spawnerObject = new GameObject("EnemySpawner");
        _spawner = _spawnerObject.AddComponent<EnemySpawner>();

        // The registry must exist before the prefab stand-in wakes: the stand-in
        // is an active object with an Enemy component and would otherwise
        // register itself on enable, inflating every live-count assertion.
        _managerObject = new GameObject("EnemyManager");
        EnemyManager manager = _managerObject.AddComponent<EnemyManager>();

        _prefabSource.SetActive(true);
        manager.Unregister(prefabEnemy);
        manager.FlushRemovals();

        // Walls: a 40x40 interior the terrain generator reads for the floor.
        _walls = new GameObject[4];
        _walls[0] = MakeWall("WallTop", new Vector2(0f, 20.5f), new Vector2(42f, 1f));
        _walls[1] = MakeWall("WallBottom", new Vector2(0f, -20.5f), new Vector2(42f, 1f));
        _walls[2] = MakeWall("WallLeft", new Vector2(-20.5f, 0f), new Vector2(1f, 42f));
        _walls[3] = MakeWall("WallRight", new Vector2(20.5f, 0f), new Vector2(1f, 42f));

        _areaObject = new GameObject("ArenaArea");
        BoxCollider2D areaBox = _areaObject.AddComponent<BoxCollider2D>();
        areaBox.isTrigger = true;
        areaBox.size = new Vector2(30f, 30f);
        ArenaArea area = _areaObject.AddComponent<ArenaArea>();

        _terrainObject = new GameObject("TerrainGenerator");
        _terrainObject.SetActive(false);
        _terrain = _terrainObject.AddComponent<TerrainGenerator>();
        GameObject terrainRoot = new GameObject("Terrain");
        terrainRoot.AddComponent<Grid>();
        Tilemap ground = MakeLayer(terrainRoot, "Ground");
        Tilemap detail = MakeLayer(terrainRoot, "Detail");
        Tilemap decor = MakeLayer(terrainRoot, "Decor");
        _groundTilemap = ground;
        WireTerrain(_terrain, area, ground, detail, decor);

        _placerObject = new GameObject("PropPlacer");
        _placerObject.SetActive(false);
        _placer = _placerObject.AddComponent<PropPlacer>();
        WirePlacer(_placer, _terrain);

        _portalObject = new GameObject("Portal");
        _portal = _portalObject.AddComponent<StagePortal>();

        _fadeObject = new GameObject("StageFade");
        StageFade fade = _fadeObject.AddComponent<StageFade>();
        SetFloat(fade, "_halfDuration", 0.01f);

        _playerObject = new GameObject("Player");
        // The portal finds the player through the locator, exactly as in the
        // scene, so the rig must publish one.
        _playerObject.AddComponent<PlayerLocator>();

        _gemObject = new GameObject("GemSpawner");
        _gemObject.SetActive(false);
        GemSpawner gems = _gemObject.AddComponent<GemSpawner>();
        GameObject gemPrefab = MakeGemPrefab();
        SetRef(gems, "_gemPrefab", gemPrefab);
        _gemObject.SetActive(true);

        _waveObject = new GameObject("WaveSystem");
        _waveObject.SetActive(false);
        _wave = _waveObject.AddComponent<WaveController>();
        WireWave(_wave, _terrain, _placer, _spawner, gems, _portal, fade, _playerObject.transform, prefabEnemy);

        // Wake everything once its references are in place.
        _terrainObject.SetActive(true);
        _placerObject.SetActive(true);
        _waveObject.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        StageBounds.Clear();

        Object.DestroyImmediate(_waveObject);
        Object.DestroyImmediate(_gemObject);
        Object.DestroyImmediate(_playerObject);
        Object.DestroyImmediate(_fadeObject);
        Object.DestroyImmediate(_portalObject);
        Object.DestroyImmediate(_placerObject);
        Object.DestroyImmediate(_terrainObject);
        Object.DestroyImmediate(_areaObject);
        foreach (GameObject wall in _walls)
        {
            Object.DestroyImmediate(wall);
        }
        Object.DestroyImmediate(_spawnerObject);
        Object.DestroyImmediate(_prefabSource);
        Object.DestroyImmediate(_cameraObject);
        Object.DestroyImmediate(_managerObject);

        foreach (GameObject stray in Object.FindObjectsOfType<GameObject>())
        {
            if (stray.transform.parent == null && (stray.name == "Terrain" || stray.name == "Props"))
            {
                Object.DestroyImmediate(stray);
            }
        }
    }

    [UnityTest]
    public IEnumerator WaveLoop_BuildsStopsAndAdvances()
    {
        yield return null; // the controller's Start builds wave 1

        Assert.AreEqual(1, _wave.Wave, "The run must open on wave one.");
        Assert.GreaterOrEqual(_placer.ObstacleCount, 1, "Wave one must place cover.");
        Assert.GreaterOrEqual(_placer.HazardCount, 1, "Wave one must place traps.");

        yield return WaitUntil(() => _spawner.HasFinishedSpawning, 5f);
        Assert.IsTrue(_spawner.HasFinishedSpawning, "The spawner must stop at the wave budget.");
        Assert.AreEqual(0, _spawner.SpawnBudgetRemaining, "The whole wave budget must be handed out.");

        // Nothing kills enemies in this rig, so the field holds exactly the wave's
        // ten enemies - which is what proves the budget really was ten.
        EnemyManager manager = Object.FindObjectOfType<EnemyManager>();
        Assert.AreEqual(10, manager.ActiveCount, "Wave one spawns exactly its ten enemies.");

        yield return KillEverything();
        yield return WaitUntil(() => _portal.gameObject.activeSelf, 5f);

        Assert.IsTrue(_portal.gameObject.activeSelf, "Clearing the field must open the portal.");
        Assert.AreEqual(Vector3.zero, new Vector3(_portal.transform.position.x, _portal.transform.position.y, 0f),
            "The portal opens on the map centre.");

        // The portal must not fire from under the player's feet: step out, then
        // back in.
        _playerObject.transform.position = new Vector3(6f, 6f, 0f);
        yield return null;
        _playerObject.transform.position = Vector3.zero;

        yield return WaitUntil(() => _wave.Wave == 2, 5f);
        Assert.AreEqual(2, _wave.Wave, "Walking into the portal must start the next wave.");
        Assert.AreEqual(15, _spawner.SpawnBudgetRemaining, "Wave two spawns five more than wave one.");
        Assert.GreaterOrEqual(_placer.ObstacleCount, 1, "Wave two must place its own cover.");
    }

    [UnityTest]
    public IEnumerator WaveLoop_TerrainFollowsTheWaves()
    {
        yield return null;

        Assert.IsNotNull(_groundTilemap, "The terrain rig needs a tilemap to check.");
        Assert.Greater(_groundTilemap.GetUsedTilesCount(), 0, "The floor must be laid for wave one.");

        // Clearing the wave and stepping through the portal must leave a floor
        // again, not an empty arena.
        yield return WaitUntil(() => _spawner.HasFinishedSpawning, 5f);
        yield return KillEverything();
        yield return WaitUntil(() => _portal.gameObject.activeSelf, 5f);

        _playerObject.transform.position = new Vector3(6f, 6f, 0f);
        yield return null;
        _playerObject.transform.position = Vector3.zero;
        yield return WaitUntil(() => _wave.Wave == 2, 5f);

        Assert.Greater(_groundTilemap.GetUsedTilesCount(), 0, "The next wave must lay its own floor.");
    }

    /// <summary>Yields until the predicate holds or the game-time budget ends.</summary>
    /// <param name="done">Condition to wait for.</param>
    /// <param name="seconds">Game-time budget.</param>
    private static IEnumerator WaitUntil(System.Func<bool> done, float seconds)
    {
        float deadline = Time.time + seconds;
        while (Time.time < deadline && !done())
        {
            yield return null;
        }
    }

    /// <summary>Kills every live enemy, as the player eventually would.</summary>
    private static IEnumerator KillEverything()
    {
        for (int frame = 0; frame < 120; frame++)
        {
            Enemy[] alive = Object.FindObjectsOfType<Enemy>();
            bool any = false;
            foreach (Enemy enemy in alive)
            {
                if (enemy != null && enemy.IsAlive)
                {
                    enemy.Die();
                    any = true;
                }
            }

            if (!any && frame > 2)
            {
                yield break;
            }
            yield return null;
        }
    }

    /// <summary>Creates a wall with a collider that bounds the arena.</summary>
    /// <param name="name">Object name.</param>
    /// <param name="position">Centre position.</param>
    /// <param name="size">Collider size.</param>
    /// <returns>The wall object.</returns>
    private static GameObject MakeWall(string name, Vector2 position, Vector2 size)
    {
        GameObject wall = new GameObject(name);
        wall.transform.position = position;
        BoxCollider2D box = wall.AddComponent<BoxCollider2D>();
        box.size = size;
        return wall;
    }

    /// <summary>Creates one tilemap layer under the terrain root.</summary>
    /// <param name="root">Terrain root carrying the Grid.</param>
    /// <param name="name">Layer name.</param>
    /// <returns>The layer's tilemap.</returns>
    private static Tilemap MakeLayer(GameObject root, string name)
    {
        GameObject layer = new GameObject(name);
        layer.transform.SetParent(root.transform, false);
        return layer.AddComponent<Tilemap>();
    }

    /// <summary>
    /// Creates a gem prefab stand-in for the drop spawner. It stays inactive:
    /// an active gem sitting at the origin would pull itself into the player and
    /// destroy the very prefab the pool clones from.
    /// </summary>
    /// <returns>The gem prefab object.</returns>
    private static GameObject MakeGemPrefab()
    {
        GameObject gem = new GameObject("GemPrefab");
        gem.SetActive(false);
        gem.AddComponent<Gem>();
        return gem;
    }

    /// <summary>Wires the terrain generator's references.</summary>
    /// <param name="terrain">Generator to wire.</param>
    /// <param name="area">Terrain rectangle.</param>
    /// <param name="ground">Ground layer.</param>
    /// <param name="detail">Detail layer.</param>
    /// <param name="decor">Decor layer.</param>
    private static void WireTerrain(TerrainGenerator terrain, ArenaArea area, Tilemap ground, Tilemap detail, Tilemap decor)
    {
        SerializedObject so = new SerializedObject(terrain);
        so.FindProperty("_terrainArea").objectReferenceValue = area;
        so.FindProperty("_ground").objectReferenceValue = ground;
        so.FindProperty("_detail").objectReferenceValue = detail;
        so.FindProperty("_decor").objectReferenceValue = decor;
        so.FindProperty("_groundTile").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Data/Tiles/Tile_floor_0.asset");
        so.ApplyModifiedProperties();
    }

    /// <summary>Wires the placer's terrain reference and its prop tables.</summary>
    /// <param name="placer">Placer to wire.</param>
    /// <param name="terrain">Terrain source.</param>
    private static void WirePlacer(PropPlacer placer, TerrainGenerator terrain)
    {
        SerializedObject so = new SerializedObject(placer);
        so.FindProperty("_terrain").objectReferenceValue = terrain;

        string[] obstacles = { "Prop_Pillar", "Prop_Rubble", "Prop_Wall_H2", "Prop_Corner_L" };
        SerializedProperty obstacleTable = so.FindProperty("_obstacles");
        obstacleTable.arraySize = obstacles.Length;
        for (int i = 0; i < obstacles.Length; i++)
        {
            SerializedProperty entry = obstacleTable.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("_prefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Props/{obstacles[i]}.prefab");
            entry.FindPropertyRelative("_count").intValue = 1;
        }

        SerializedProperty hazardTable = so.FindProperty("_hazards");
        hazardTable.arraySize = 1;
        SerializedProperty hazard = hazardTable.GetArrayElementAtIndex(0);
        hazard.FindPropertyRelative("_prefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SpikeTrap.prefab");
        hazard.FindPropertyRelative("_count").intValue = 1;
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Wires the wave controller: references plus two in-memory tiers that
    /// spawn the test prefab, so the numbers under test are the test's own.
    /// </summary>
    /// <param name="wave">Controller to wire.</param>
    /// <param name="terrain">Terrain generator.</param>
    /// <param name="placer">Prop placer.</param>
    /// <param name="spawner">Enemy spawner.</param>
    /// <param name="gems">Gem spawner.</param>
    /// <param name="portal">Exit portal.</param>
    /// <param name="fade">Screen fade.</param>
    /// <param name="player">Player transform.</param>
    /// <param name="enemyPrefab">Enemy component the tiers spawn.</param>
    private static void WireWave(WaveController wave, TerrainGenerator terrain, PropPlacer placer,
        EnemySpawner spawner, GemSpawner gems, StagePortal portal, StageFade fade, Transform player, Enemy enemyPrefab)
    {
        SerializedObject so = new SerializedObject(wave);
        so.FindProperty("_terrain").objectReferenceValue = terrain;
        so.FindProperty("_props").objectReferenceValue = placer;
        so.FindProperty("_spawner").objectReferenceValue = spawner;
        so.FindProperty("_gemSpawner").objectReferenceValue = gems;
        so.FindProperty("_portal").objectReferenceValue = portal;
        so.FindProperty("_fade").objectReferenceValue = fade;
        so.FindProperty("_player").objectReferenceValue = player;
        so.FindProperty("_firstWaveEnemies").intValue = 10;
        so.FindProperty("_enemiesPerWave").intValue = 5;
        so.FindProperty("_wavesPerTier").intValue = 2;

        SerializedProperty tiers = so.FindProperty("_tiers");
        tiers.arraySize = 2;
        tiers.GetArrayElementAtIndex(0).objectReferenceValue = MakeTier("T1", enemyPrefab, 3, 2);
        tiers.GetArrayElementAtIndex(1).objectReferenceValue = MakeTier("T2", enemyPrefab, 1, 3);
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Builds an in-memory tier asset pointing at the test enemy prefab.
    /// </summary>
    /// <param name="name">Tier name.</param>
    /// <param name="enemyPrefab">Enemy component to spawn.</param>
    /// <param name="obstacles">Cover pieces per wave.</param>
    /// <param name="hazards">Traps per wave.</param>
    /// <returns>The tier asset.</returns>
    private static WaveTierData MakeTier(string name, Enemy enemyPrefab, int obstacles, int hazards)
    {
        WaveTierData tier = ScriptableObject.CreateInstance<WaveTierData>();
        SerializedObject so = new SerializedObject(tier);
        so.FindProperty("_tierName").stringValue = name;
        so.FindProperty("_spawnInterval").floatValue = 0.02f;
        so.FindProperty("_maxAlive").intValue = 60;
        so.FindProperty("_obstacleCount").intValue = obstacles;
        so.FindProperty("_hazardCount").intValue = hazards;

        SerializedProperty entries = so.FindProperty("_spawnEntries");
        entries.arraySize = 1;
        SerializedProperty entry = entries.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("_prefab").objectReferenceValue = enemyPrefab;
        entry.FindPropertyRelative("_weight").floatValue = 1f;
        so.ApplyModifiedProperties();
        return tier;
    }

    /// <summary>Writes a serialized object reference.</summary>
    /// <param name="target">Object owning the field.</param>
    /// <param name="fieldName">Serialized field name.</param>
    /// <param name="value">Reference to assign.</param>
    private static void SetRef(Object target, string fieldName, Object value)
    {
        SerializedObject so = new SerializedObject(target);
        so.FindProperty(fieldName).objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }

    /// <summary>Writes a serialized float field.</summary>
    /// <param name="target">Object owning the field.</param>
    /// <param name="fieldName">Serialized field name.</param>
    /// <param name="value">Value to assign.</param>
    private static void SetFloat(Object target, string fieldName, float value)
    {
        SerializedObject so = new SerializedObject(target);
        so.FindProperty(fieldName).floatValue = value;
        so.ApplyModifiedProperties();
    }
}
