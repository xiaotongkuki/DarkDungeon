using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the chase behaviour: closing on the player, turning when
/// the player moves, settling on the stopping ring, keeping the spatial grid up to
/// date while moving, surviving a missing player, and stopping on death.
/// </summary>
public class EnemyAITests
{
    /// <summary>Cell size for these tests: small cells make boundary crossings easy to trigger.</summary>
    private const float CellSize = 1f;

    private GameObject _managerObject;
    private GameObject _playerObject;
    private EnemyManager _manager;
    private readonly List<GameObject> _enemyObjects = new List<GameObject>();

    [SetUp]
    public void SetUp()
    {
        _managerObject = new GameObject("EnemyManager");
        _manager = _managerObject.AddComponent<EnemyManager>();
        SetFloat(_manager, "_cellSize", CellSize);

        _playerObject = new GameObject("Player");
        _playerObject.AddComponent<PlayerLocator>();
        _playerObject.transform.position = Vector3.zero;
    }

    [TearDown]
    public void TearDown()
    {
        // Immediate teardown keeps the singletons from leaking into the next test.
        for (int i = 0; i < _enemyObjects.Count; i++)
        {
            if (_enemyObjects[i] != null)
            {
                Object.DestroyImmediate(_enemyObjects[i]);
            }
        }
        _enemyObjects.Clear();

        if (_playerObject != null)
        {
            Object.DestroyImmediate(_playerObject);
        }
        Object.DestroyImmediate(_managerObject);
    }

    /// <summary>
    /// Creates a chasing enemy. The rigidbody mirrors the prefab (kinematic, no
    /// gravity) because a freshly added Rigidbody2D would otherwise be dynamic and
    /// fall under gravity.
    ///
    /// The object is built inactive and activated last: the components reject a
    /// missing data asset, and AddComponent on a live object runs Awake before the
    /// asset can be assigned, which would log that error and fail the test.
    /// </summary>
    /// <param name="position">World position to spawn at.</param>
    /// <param name="speed">Chase speed in units per second.</param>
    /// <param name="stopDistance">Stopping ring radius in world units.</param>
    /// <returns>The created enemy.</returns>
    private Enemy SpawnEnemy(Vector2 position, float speed, float stopDistance)
    {
        var go = new GameObject("Enemy");
        go.SetActive(false);
        go.transform.position = position;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        Enemy enemy = go.AddComponent<Enemy>();
        EnemyAI ai = go.AddComponent<EnemyAI>();

        // Zero variance: every assertion on travel distance would otherwise be at
        // the mercy of a random multiplier.
        TestData.SetObjectReference(enemy, "_data", TestData.CreateEnemyData(moveSpeed: speed, speedVariance: 0f));
        SetFloat(ai, "_stopDistance", stopDistance);

        go.SetActive(true);

        _enemyObjects.Add(go);
        return enemy;
    }

    /// <summary>
    /// Creates an interpolated enemy at the origin the way the spawner does, with
    /// interpolation on and no chase speed, so any divergence between the rendered
    /// transform and the physics body comes from the interpolator alone.
    /// </summary>
    /// <returns>The created enemy.</returns>
    private Enemy SpawnInterpolatedEnemyAtOrigin()
    {
        var go = new GameObject("Enemy");
        go.SetActive(false);
        go.transform.position = Vector2.zero;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        Enemy enemy = go.AddComponent<Enemy>();
        go.AddComponent<EnemyAI>();

        TestData.SetObjectReference(enemy, "_data", TestData.CreateEnemyData(moveSpeed: 0f, speedVariance: 0f));

        go.SetActive(true);

        _enemyObjects.Add(go);
        return enemy;
    }

    /// <summary>
    /// Distance between an enemy's rendered transform and its physics body, which is
    /// the quantity the interpolator is responsible for keeping small.
    /// </summary>
    /// <param name="enemy">Enemy to measure.</param>
    /// <returns>Distance in world units.</returns>
    private static float RenderDivergence(Enemy enemy)
    {
        return Vector2.Distance(enemy.transform.position, enemy.GetComponent<Rigidbody2D>().position);
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
    public IEnumerator Chase_ClosesInOnThePlayer()
    {
        Enemy enemy = SpawnEnemy(new Vector2(5f, 0f), speed: 2f, stopDistance: 0.2f);

        yield return WaitForGameTime(0.5f);

        float distance = Vector2.Distance(enemy.transform.position, _playerObject.transform.position);
        Assert.Less(distance, 5f, "The enemy must close in on the player.");
        Assert.Greater(distance, 0.2f, "It must not walk past the stopping ring.");
    }

    [UnityTest]
    public IEnumerator Chase_FollowsThePlayerAfterItMoves()
    {
        Enemy enemy = SpawnEnemy(new Vector2(5f, 0f), speed: 1f, stopDistance: 0.2f);

        yield return WaitForGameTime(0.2f);

        // Send the player off in a different direction: the enemy has to turn.
        _playerObject.transform.position = new Vector3(0f, 5f, 0f);
        float heightBefore = enemy.transform.position.y;

        yield return WaitForGameTime(0.6f);

        Assert.Greater(enemy.transform.position.y, heightBefore,
            "The enemy must follow the player to its new position.");
    }

    [UnityTest]
    public IEnumerator Chase_SettlesOnTheStoppingRing()
    {
        Enemy enemy = SpawnEnemy(new Vector2(3f, 0f), speed: 5f, stopDistance: 0.5f);

        yield return WaitForGameTime(2f);

        float distance = Vector2.Distance(enemy.transform.position, _playerObject.transform.position);
        Assert.AreEqual(0.5f, distance, 0.1f, "The enemy must settle on the ring, not on the player.");
    }

    [UnityTest]
    public IEnumerator Chase_KeepsTheSpatialGridUpToDate()
    {
        // Starting six units out with a cell size of one guarantees the enemy
        // crosses several cell boundaries while it closes in.
        Enemy enemy = SpawnEnemy(new Vector2(6f, 0.5f), speed: 4f, stopDistance: 0.2f);
        Vector2 start = enemy.transform.position;

        yield return WaitForGameTime(0.6f);

        Vector2 now = enemy.transform.position;
        Assert.Less((now - start).magnitude, 5f, "Sanity check: the enemy must have moved but not arrived.");

        // The grid must report the enemy where it is now...
        Assert.IsTrue(_manager.TryGetNearest(now, out Enemy found, maxRadius: 0.5f),
            "The enemy must be queryable at its new position.");
        Assert.AreSame(enemy, found);

        // ...and no longer where it started, otherwise the stale cell leaked.
        Assert.IsFalse(_manager.TryGetNearest(start, out Enemy _, maxRadius: 0.5f),
            "The vacated cell must no longer report the enemy.");
    }

    [UnityTest]
    public IEnumerator Chase_WithoutPlayerLocator_WaitsAndThenRecovers()
    {
        // Simulate a scene with no player at all.
        Object.DestroyImmediate(_playerObject);
        _playerObject = null;

        Enemy enemy = SpawnEnemy(new Vector2(4f, 0f), speed: 2f, stopDistance: 0.2f);
        Vector2 start = enemy.transform.position;

        // The component logs a single warning here; warnings do not fail play-mode
        // tests, only errors and exceptions do.
        yield return WaitForGameTime(0.3f);

        Assert.AreEqual(start, (Vector2)enemy.transform.position,
            "Without a player the enemy must stay put.");

        // Resolution is lazy, so a player that appears later must be picked up.
        _playerObject = new GameObject("Player");
        _playerObject.AddComponent<PlayerLocator>();

        yield return WaitForGameTime(0.5f);

        float distance = Vector2.Distance(enemy.transform.position, _playerObject.transform.position);
        Assert.Less(distance, start.magnitude, "The enemy must start chasing once a player appears.");
    }

    [UnityTest]
    public IEnumerator Chase_DeadEnemy_StopsAndLeavesTheGrid()
    {
        Enemy enemy = SpawnEnemy(new Vector2(4f, 0f), speed: 2f, stopDistance: 0.2f);

        yield return WaitForGameTime(0.2f);

        enemy.TakeDamage(999);

        Assert.IsFalse(enemy.IsAlive);
        Assert.IsFalse(enemy.gameObject.activeSelf,
            "Death deactivates the enemy, which is what stops its physics step.");
        Assert.IsFalse(_manager.TryGetNearest(Vector2.zero, out Enemy _, maxRadius: 20f),
            "A dead enemy must never be returned as a target.");

        yield return null;

        Assert.AreEqual(0, _manager.ActiveCount, "The corpse must leave the grid by the end of the frame.");
        Assert.IsTrue(enemy == null, "The corpse object must be destroyed by the end of the frame.");
    }

    [UnityTest]
    public IEnumerator PlaceAt_KeepsTheRenderedSpriteOnTheBody()
    {
        Vector2 spawnPoint = new Vector2(12f, 0f);

        // The smear only exists during the first physics step after the teleport, so
        // widen that step: otherwise a slow frame can step straight past the window
        // and the test would pass or fail by luck.
        float previousFixedDelta = Time.fixedDeltaTime;
        Time.fixedDeltaTime = 0.2f;

        try
        {
            // Two identical enemies born at the origin, the way the spawner creates
            // them: one placed through the supported path, one teleported with a bare
            // position write, which is what leaves the interpolator's stale sample.
            Enemy placed = SpawnInterpolatedEnemyAtOrigin();
            Enemy teleported = SpawnInterpolatedEnemyAtOrigin();

            placed.GetComponent<EnemyAI>().PlaceAt(spawnPoint);
            teleported.GetComponent<Rigidbody2D>().position = spawnPoint;

            float placedWorst = 0f;
            float teleportedWorst = 0f;
            for (int frame = 0; frame < 10; frame++)
            {
                yield return null;
                placedWorst = Mathf.Max(placedWorst, RenderDivergence(placed));
                teleportedWorst = Mathf.Max(teleportedWorst, RenderDivergence(teleported));
            }

            Assert.Less(placedWorst, 0.2f,
                "PlaceAt must keep the rendered sprite on its body from the very first frame.");

            // Documents the engine bug this works around (Unity Case 1367721): a bare
            // position write leaves the interpolator's previous pose at the world
            // origin, so the sprite slides in from there on the next physics step. If
            // this ever starts failing, the engine has fixed it and PlaceAt can go.
            Assert.Greater(teleportedWorst, 1f,
                "Expected a bare teleport to smear the sprite from the origin; if this fails, " +
                "the engine bug is gone and the PlaceAt workaround can be simplified.");
        }
        finally
        {
            Time.fixedDeltaTime = previousFixedDelta;
        }
    }

    [UnityTest]
    public IEnumerator Chase_ThreeHundredEnemies_DoesNotStall()
    {
        const int count = 300;
        for (int i = 0; i < count; i++)
        {
            SpawnEnemy(new Vector2(Random.Range(-40f, 40f), Random.Range(-40f, 40f)), speed: 2f, stopDistance: 0.2f);
        }
        Assert.AreEqual(count, _manager.ActiveCount);

        float startedAt = Time.realtimeSinceStartup;
        yield return WaitForGameTime(0.5f);
        float elapsedMs = (Time.realtimeSinceStartup - startedAt) * 1000f;

        // A baseline for regression spotting, not a tight budget: the bound only
        // catches a catastrophic stall, which keeps the test from flaking.
        Debug.Log($"[EnemyAI] {count} chasing enemies: {elapsedMs:F1} ms of wall clock for 0.5 s of game time.");
        Assert.Less(elapsedMs, 5000f, "300 chasing enemies must not stall the frame loop.");
    }
}
