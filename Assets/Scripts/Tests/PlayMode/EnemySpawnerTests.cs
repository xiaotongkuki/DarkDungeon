using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the spawner: cadence, the live cap and — most importantly
/// — that every enemy appears outside the camera's visible rectangle instead of
/// popping in on screen.
/// </summary>
public class EnemySpawnerTests
{
    private GameObject _managerObject;
    private GameObject _spawnerObject;
    private GameObject _cameraObject;
    private GameObject _prefabSource;
    private EnemyManager _manager;
    private EnemySpawner _spawner;
    private Camera _camera;

    [SetUp]
    public void SetUp()
    {
        // The spawner caches Camera.main in Awake, so the camera must exist first.
        _cameraObject = new GameObject("TestCamera");
        _cameraObject.tag = "MainCamera";
        _camera = _cameraObject.AddComponent<Camera>();
        _camera.orthographic = true;
        _camera.orthographicSize = 5f;
        _cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        // Deliberately created BEFORE the manager: AddComponent runs Awake
        // immediately, so this reproduces the real scene where both components sit
        // on one GameObject and Unity does not guarantee their Awake order.
        _spawnerObject = new GameObject("EnemySpawner");
        _spawner = _spawnerObject.AddComponent<EnemySpawner>();

        _managerObject = new GameObject("EnemyManager");
        _manager = _managerObject.AddComponent<EnemyManager>();
        SetFloat(_manager, "_cellSize", 4f);

        // A runtime stand-in for the enemy prefab: the spawner only needs the
        // component to instantiate. It must stay active so its clones are active
        // too, but it must not count as a live enemy itself.
        //
        // Built inactive and given a data asset, because Enemy rejects a missing
        // asset and AddComponent on a live object would run Awake first.
        _prefabSource = new GameObject("EnemyPrefab");
        _prefabSource.SetActive(false);
        Enemy prefabEnemy = _prefabSource.AddComponent<Enemy>();
        TestData.SetObjectReference(prefabEnemy, "_data", TestData.CreateEnemyData());
        _prefabSource.SetActive(true);

        _manager.Unregister(prefabEnemy);
        _manager.FlushRemovals();

        SetSpawnEntries(_spawner, prefabEnemy);
        SetFloat(_spawner, "_spawnInterval", 0.05f);
        SetInt(_spawner, "_maxAlive", 300);
    }

    [TearDown]
    public void TearDown()
    {
        // Immediate teardown: deferred destroys would not have run before the next
        // test's SetUp and the manager singleton would leak across tests.
        foreach (Enemy enemy in Object.FindObjectsOfType<Enemy>())
        {
            if (enemy != null)
            {
                Object.DestroyImmediate(enemy.gameObject);
            }
        }
        Object.DestroyImmediate(_spawnerObject);
        Object.DestroyImmediate(_prefabSource);
        Object.DestroyImmediate(_cameraObject);
        Object.DestroyImmediate(_managerObject);
    }

    /// <summary>Writes a private float field exactly as the Inspector would.</summary>
    /// <param name="target">Object owning the field.</param>
    /// <param name="fieldName">Serialized field name.</param>
    /// <param name="value">Value to assign.</param>
    private static void SetFloat(Object target, string fieldName, float value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).floatValue = value;
        so.ApplyModifiedProperties();
    }

    /// <summary>Writes a private int field exactly as the Inspector would.</summary>
    /// <param name="target">Object owning the field.</param>
    /// <param name="fieldName">Serialized field name.</param>
    /// <param name="value">Value to assign.</param>
    private static void SetInt(Object target, string fieldName, int value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).intValue = value;
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Replaces the spawner's spawn table with one entry per prefab, all at equal
    /// weight. Passing no prefabs leaves the table empty, which is how the
    /// unconfigured-spawner case is set up.
    /// </summary>
    /// <param name="spawner">Spawner to configure.</param>
    /// <param name="prefabs">Enemy prefabs to spawn.</param>
    private static void SetSpawnEntries(EnemySpawner spawner, params Enemy[] prefabs)
    {
        var so = new SerializedObject(spawner);
        SerializedProperty entries = so.FindProperty("_spawnEntries");
        entries.arraySize = prefabs.Length;

        for (int i = 0; i < prefabs.Length; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("_prefab").objectReferenceValue = prefabs[i];
            entry.FindPropertyRelative("_weight").floatValue = 1f;
        }

        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Yields until the given amount of game time has passed, regardless of how
    /// many frames that takes on the current machine and editor focus state.
    /// </summary>
    /// <param name="seconds">Game-time duration to wait for.</param>
    private static IEnumerator WaitForGameTime(float seconds)
    {
        float deadline = Time.time + seconds;
        while (Time.time < deadline)
        {
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator Spawner_ResolvesManagerWhenItsAwakeRanFirst()
    {
        // SetUp creates the spawner before the manager on purpose, so this is the
        // regression guard for a manager cached in Awake: it would stay null and
        // the scene would silently produce no enemies at all.
        yield return WaitForGameTime(0.2f);

        Assert.GreaterOrEqual(_manager.ActiveCount, 1,
            "The spawner must find the manager even when its own Awake ran first.");
    }

    [UnityTest]
    public IEnumerator Spawner_KeepsProducingEnemies()
    {
        yield return WaitForGameTime(0.5f);

        // ~10 spawns are expected in half a second; the bounds stay loose so a
        // slow frame cannot flake the assertion.
        Assert.GreaterOrEqual(_manager.ActiveCount, 4, "The spawner must keep producing enemies over time.");
        Assert.LessOrEqual(_manager.ActiveCount, 30, "The spawner must respect its interval, not flood the scene.");
    }

    [UnityTest]
    public IEnumerator Spawner_PlacesEveryEnemyOutsideTheCameraView()
    {
        yield return WaitForGameTime(0.4f);

        ViewRect view = SpawnArea.GetViewRect(_camera, 0f);
        Assert.GreaterOrEqual(_manager.ActiveCount, 3, "Not enough enemies spawned to judge placement.");

        foreach (Enemy enemy in Object.FindObjectsOfType<Enemy>())
        {
            if (enemy == _prefabSource.GetComponent<Enemy>())
            {
                continue; // the instantiate source is not a spawn
            }

            Vector3 position = enemy.transform.position;
            bool outsideX = Mathf.Abs(position.x - view.Center.x) > view.HalfExtents.x;
            bool outsideY = Mathf.Abs(position.y - view.Center.y) > view.HalfExtents.y;

            Assert.IsTrue(outsideX || outsideY,
                $"Enemy spawned inside the camera view at {position}; view is {view.HalfExtents * 2f} around {view.Center}.");
        }
    }

    [UnityTest]
    public IEnumerator Spawner_StopsAtMaxAlive()
    {
        SetInt(_spawner, "_maxAlive", 5);
        SetFloat(_spawner, "_spawnInterval", 0.01f);

        yield return WaitForGameTime(0.5f);

        Assert.AreEqual(5, _manager.ActiveCount, "Spawning must stop once the live cap is reached.");
    }

    [UnityTest]
    public IEnumerator Spawner_WithoutPrefab_DoesNotSpawnOrThrow()
    {
        SetSpawnEntries(_spawner);

        yield return WaitForGameTime(0.2f);

        Assert.AreEqual(0, _manager.ActiveCount, "An empty spawn table must be a no-op, not an error.");
    }

    [UnityTest]
    public IEnumerator Spawner_ReusesADeadEnemyInsteadOfCreatingANewOne()
    {
        // Manual control: the spawner's own cadence would keep adding enemies and the
        // instance identity under test would stop being unambiguous.
        SetInt(_spawner, "_maxAlive", 0);

        Enemy first = _spawner.SpawnOne();
        Assert.IsNotNull(first, "The spawner must produce an enemy to test reuse with.");
        int originalId = first.GetInstanceID();

        first.Die();

        // Reused in the same frame, before the grid removal queued by the death is
        // flushed - which is the round trip that has to survive, because re-registering
        // is what cancels that removal.
        Enemy second = _spawner.SpawnOne();

        Assert.IsNotNull(second, "The spawner must keep producing enemies after a death.");
        Assert.AreEqual(originalId, second.GetInstanceID(),
            "A dead enemy must come back from the pool instead of a fresh instance being made.");
        Assert.IsTrue(second.gameObject.activeSelf, "A reused enemy must be active.");
        Assert.IsTrue(second.IsAlive, "A reused enemy must come back with its health refilled.");

        // Let the end-of-frame flush run: if the removal queued on death were still
        // pending, this is where it would evict the enemy that had just come back.
        yield return null;

        Assert.IsTrue(_manager.TryGetNearest(second.transform.position, out Enemy found, 5f),
            "The reused enemy must still be registered after the frame's removals are flushed.");
        Assert.AreSame(second, found, "The registry must hold the reused enemy itself.");
    }
}
