using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the enemy registry: self-registration, nearest-enemy
/// queries through the spatial grid, deferred removal on death and bookkeeping
/// integrity under spawn/kill churn.
/// </summary>
public class EnemyManagerTests
{
    private GameObject _managerObject;
    private EnemyManager _manager;
    private readonly List<GameObject> _enemyObjects = new List<GameObject>();

    [SetUp]
    public void SetUp()
    {
        _managerObject = new GameObject("EnemyManager");
        _manager = _managerObject.AddComponent<EnemyManager>();

        // Inspector-equivalent wiring for the private serialized field.
        var so = new SerializedObject(_manager);
        so.FindProperty("_cellSize").floatValue = 2f;
        so.ApplyModifiedProperties();
    }

    [TearDown]
    public void TearDown()
    {
        // Immediate teardown keeps the singleton from leaking into the next test:
        // a deferred Destroy would not have run before the following SetUp, and the
        // next manager would then destroy itself as a duplicate.
        for (int i = 0; i < _enemyObjects.Count; i++)
        {
            if (_enemyObjects[i] != null)
            {
                Object.DestroyImmediate(_enemyObjects[i]);
            }
        }
        _enemyObjects.Clear();
        Object.DestroyImmediate(_managerObject);
    }

    /// <summary>
    /// Creates an enemy at the given position. The transform is placed before the
    /// component is enabled, because OnEnable registers using the current position.
    ///
    /// The object is built inactive and activated last so the data asset can be
    /// assigned before Awake runs: an enemy with no data asset refuses to act.
    /// </summary>
    /// <param name="position">World position for the enemy.</param>
    /// <returns>The created enemy.</returns>
    private Enemy SpawnEnemy(Vector2 position)
    {
        var go = new GameObject("Enemy");
        go.SetActive(false);
        go.transform.position = position;
        Enemy enemy = go.AddComponent<Enemy>();
        TestData.SetObjectReference(enemy, "_data", TestData.CreateEnemyData());
        go.SetActive(true);

        _enemyObjects.Add(go);
        return enemy;
    }

    [Test]
    public void Enemy_RegistersItselfOnEnable()
    {
        Enemy enemy = SpawnEnemy(new Vector2(1f, 1f));

        Assert.AreEqual(1, _manager.ActiveCount);
        Assert.IsTrue(_manager.TryGetNearest(new Vector2(1.1f, 1f), out Enemy nearest));
        Assert.AreSame(enemy, nearest);
    }

    [Test]
    public void TryGetNearest_NoEnemies_ReturnsFalse()
    {
        Assert.IsFalse(_manager.TryGetNearest(Vector2.zero, out Enemy nearest));
        Assert.IsNull(nearest);
    }

    [Test]
    public void TryGetNearest_ReturnsClosestEnemy()
    {
        SpawnEnemy(new Vector2(20f, 0f));
        Enemy closest = SpawnEnemy(new Vector2(3f, 0f));
        SpawnEnemy(new Vector2(-15f, 0f));

        Assert.IsTrue(_manager.TryGetNearest(Vector2.zero, out Enemy nearest));
        Assert.AreSame(closest, nearest);
    }

    [Test]
    public void TryGetNearest_RespectsMaxRadius()
    {
        SpawnEnemy(new Vector2(30f, 0f));

        Assert.IsFalse(_manager.TryGetNearest(Vector2.zero, out Enemy _, maxRadius: 5f));
        Assert.IsTrue(_manager.TryGetNearest(Vector2.zero, out Enemy _, maxRadius: 40f));
    }

    [Test]
    public void TryGetNearest_NeverReturnsACorpseFromTheSameFrame()
    {
        // The corpse is the closer of the two, so an unfiltered query would pick it.
        Enemy corpse = SpawnEnemy(new Vector2(0.5f, 0.5f));
        Enemy survivor = SpawnEnemy(new Vector2(6f, 0.5f));

        corpse.TakeDamage(999);

        // Removal is queued until LateUpdate, so the corpse really is still in the
        // grid right now. Asserting that is what makes this test meaningful: the
        // filter, not the absence of the corpse, must be doing the work.
        Assert.AreEqual(2, _manager.ActiveCount, "Deferred removal leaves the corpse registered this frame.");
        Assert.IsFalse(corpse.IsAlive);

        Assert.IsTrue(_manager.TryGetNearest(Vector2.zero, out Enemy nearest),
            "The surviving enemy must still be found.");
        Assert.AreSame(survivor, nearest, "A corpse must never be returned as a target.");
    }

    [Test]
    public void Register_CancelsARemovalQueuedByADisableEnableCycle()
    {
        Enemy enemy = SpawnEnemy(new Vector2(1f, 1f));

        // Disabling and re-enabling is exactly what object pooling does: the disable
        // queues a removal, the enable registers the enemy again. The stale queue
        // entry must not evict it at the end of the frame.
        enemy.enabled = false;
        enemy.enabled = true;

        _manager.FlushRemovals();

        Assert.AreEqual(1, _manager.ActiveCount, "A re-registered enemy must survive the flush.");
        Assert.IsTrue(_manager.TryGetNearest(new Vector2(1f, 1f), out Enemy found));
        Assert.AreSame(enemy, found, "The enemy must still be the one queries return.");
    }

    [Test]
    public void Unregister_ThenFlush_RemovesEnemyFromGrid()
    {
        Enemy enemy = SpawnEnemy(new Vector2(2f, 2f));

        _manager.Unregister(enemy);
        _manager.FlushRemovals();

        Assert.AreEqual(0, _manager.ActiveCount);
        Assert.IsFalse(_manager.TryGetNearest(new Vector2(2f, 2f), out Enemy _));
    }

    [Test]
    public void RefreshPosition_RebucketsMovedEnemy()
    {
        Enemy enemy = SpawnEnemy(new Vector2(0.5f, 0.5f));

        enemy.transform.position = new Vector3(50.5f, 0.5f, 0f);
        _manager.RefreshPosition(enemy);

        Assert.IsTrue(_manager.TryGetNearest(new Vector2(50.6f, 0.5f), out Enemy nearest));
        Assert.AreSame(enemy, nearest);

        // Radius-bounded: an unlimited query would keep expanding rings and find
        // the enemy at its new, far-away position.
        Assert.IsFalse(_manager.TryGetNearest(new Vector2(0.5f, 0.5f), out Enemy _, maxRadius: 4f),
            "The vacated cell must no longer report the enemy.");
    }

    [UnityTest]
    public IEnumerator Enemy_Die_LeavesTheRegistryWithinTheFrame()
    {
        Enemy enemy = SpawnEnemy(new Vector2(4f, 4f));
        Assert.AreEqual(1, _manager.ActiveCount);

        enemy.TakeDamage(999);
        Assert.IsFalse(enemy.IsAlive, "Lethal damage must kill the enemy.");

        // Health alone is a weak signal: it drops below zero before Die runs, so
        // assert the death side effects too, otherwise a no-op Die would pass.
        Assert.IsFalse(enemy.gameObject.activeSelf, "Death must deactivate the enemy immediately.");

        // Removal is queued and flushed in LateUpdate, so one frame is enough.
        yield return null;

        Assert.AreEqual(0, _manager.ActiveCount);
        Assert.IsFalse(_manager.TryGetNearest(new Vector2(4f, 4f), out Enemy _));
        Assert.IsTrue(enemy == null, "The dead enemy object must be destroyed by the end of the frame.");
    }

    [UnityTest]
    public IEnumerator TakeDamage_NonLethal_KeepsEnemyRegistered()
    {
        Enemy enemy = SpawnEnemy(new Vector2(4f, 4f));

        enemy.TakeDamage(1);
        yield return null;

        Assert.IsTrue(enemy.IsAlive);
        Assert.AreEqual(1, _manager.ActiveCount);
    }

    [Test]
    public void Stress_SpawnAndKill_KeepsRegistryConsistent()
    {
        const int total = 60;
        var enemies = new List<Enemy>(total);
        for (int i = 0; i < total; i++)
        {
            enemies.Add(SpawnEnemy(new Vector2(i % 10 * 3f, i / 10 * 3f)));
        }
        Assert.AreEqual(total, _manager.ActiveCount);

        // Kill every other enemy, then let the queued removals apply.
        for (int i = 0; i < total; i += 2)
        {
            enemies[i].TakeDamage(999);
        }
        _manager.FlushRemovals();

        Assert.AreEqual(total / 2, _manager.ActiveCount);
        for (int i = 1; i < total; i += 2)
        {
            Assert.IsTrue(_manager.TryGetNearest(enemies[i].transform.position, out Enemy nearest));
            Assert.AreSame(enemies[i], nearest, "A surviving enemy must still be the closest to itself.");
        }
    }

    [UnityTest]
    public IEnumerator DuplicateManager_DefersToTheFirstInstance()
    {
        var duplicateObject = new GameObject("EnemyManagerDuplicate");
        EnemyManager duplicate = duplicateObject.AddComponent<EnemyManager>();

        Assert.AreSame(_manager, EnemyManager.Instance, "The first manager must keep the singleton slot.");

        yield return null;

        Assert.IsTrue(duplicate == null, "The duplicate component must remove itself.");

        Object.DestroyImmediate(duplicateObject);
    }
}
