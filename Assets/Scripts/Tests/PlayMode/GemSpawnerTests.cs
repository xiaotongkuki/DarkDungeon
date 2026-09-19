using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the drop: every kill leaves a gem worth that enemy type's
/// experience, at the place the enemy died, and collected gems are recycled.
/// </summary>
public class GemSpawnerTests
{
    private GameObject _managerObject;
    private GameObject _spawnerObject;
    private GameObject _gemPrefabObject;
    private GameObject _enemyObject;
    private GameObject _playerObject;
    private Gem _gemPrefab;

    [SetUp]
    public void SetUp()
    {
        _managerObject = new GameObject("EnemyManager");
        _managerObject.AddComponent<EnemyManager>();

        // A runtime stand-in for the gem prefab, parked far away so it neither
        // collects itself nor pollutes the drop assertions.
        _gemPrefabObject = new GameObject("GemPrefab");
        _gemPrefabObject.transform.position = new Vector3(500f, 500f, 0f);
        _gemPrefab = _gemPrefabObject.AddComponent<Gem>();

        _spawnerObject = new GameObject("GemSpawner");
        _spawnerObject.SetActive(false);
        GemSpawner spawner = _spawnerObject.AddComponent<GemSpawner>();
        TestData.SetObjectReference(spawner, "_gemPrefab", _gemPrefab);
        _spawnerObject.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Gem gem in Object.FindObjectsOfType<Gem>())
        {
            if (gem != null)
            {
                Object.DestroyImmediate(gem.gameObject);
            }
        }

        if (_enemyObject != null)
        {
            Object.DestroyImmediate(_enemyObject);
        }
        if (_playerObject != null)
        {
            Object.DestroyImmediate(_playerObject);
        }
        Object.DestroyImmediate(_spawnerObject);
        Object.DestroyImmediate(_gemPrefabObject);
        Object.DestroyImmediate(_managerObject);
    }

    /// <summary>
    /// Creates an enemy worth the given experience. Built inactive so its data asset
    /// can be assigned before Awake runs, which the enemy requires.
    /// </summary>
    /// <param name="position">World position.</param>
    /// <param name="xpValue">Experience its gem should be worth.</param>
    /// <returns>The created enemy.</returns>
    private Enemy SpawnEnemy(Vector2 position, int xpValue)
    {
        var go = new GameObject("Enemy");
        go.SetActive(false);
        go.transform.position = position;
        Enemy enemy = go.AddComponent<Enemy>();
        TestData.SetObjectReference(enemy, "_data",
            TestData.CreateEnemyData(maxHealth: 1, xpValue: xpValue));
        go.SetActive(true);

        _enemyObject = go;
        return enemy;
    }

    /// <summary>Collects the dropped gems, excluding the instantiate source.</summary>
    /// <returns>Gems that were actually dropped.</returns>
    private List<Gem> DroppedGems()
    {
        var dropped = new List<Gem>();
        foreach (Gem gem in Object.FindObjectsOfType<Gem>())
        {
            if (gem != null && gem != _gemPrefab)
            {
                dropped.Add(gem);
            }
        }
        return dropped;
    }

    [Test]
    public void KillingAnEnemy_DropsOneGemWorthItsXpValue()
    {
        Vector2 deathPosition = new Vector2(2f, 3f);
        Enemy enemy = SpawnEnemy(deathPosition, xpValue: 4);

        enemy.TakeDamage(1);

        List<Gem> dropped = DroppedGems();
        Assert.AreEqual(1, dropped.Count, "Every kill must leave exactly one gem.");
        Assert.AreEqual(4, dropped[0].XpValue, "The gem must be worth the dead enemy's experience.");
        Assert.LessOrEqual(Vector2.Distance(dropped[0].transform.position, deathPosition), 0.5f,
            "The gem must land at the death position, allowing for the scatter offset.");
    }

    [Test]
    public void NonLethalDamage_DropsNothing()
    {
        var go = new GameObject("Enemy");
        go.SetActive(false);
        go.transform.position = Vector2.zero;
        Enemy enemy = go.AddComponent<Enemy>();
        TestData.SetObjectReference(enemy, "_data", TestData.CreateEnemyData(maxHealth: 3, xpValue: 1));
        go.SetActive(true);
        _enemyObject = go;

        enemy.TakeDamage(1);

        Assert.IsTrue(enemy.IsAlive, "Sanity check: the enemy must survive one hit.");
        Assert.AreEqual(0, DroppedGems().Count, "A wounded enemy drops nothing.");
    }

    [UnityTest]
    public IEnumerator CollectedGems_AreRecycledInsteadOfCreatedPerKill()
    {
        // The player stands on the drop point, so each gem is collected on its first
        // update and retires straight back into the pool.
        _playerObject = new GameObject("Player");
        _playerObject.AddComponent<PlayerLocator>();
        _playerObject.transform.position = Vector2.zero;

        Enemy first = SpawnEnemy(Vector2.zero, xpValue: 1);
        first.TakeDamage(1);

        List<Gem> firstDrop = DroppedGems();
        Assert.AreEqual(1, firstDrop.Count, "The first kill must drop a gem to test recycling with.");
        int firstId = firstDrop[0].GetInstanceID();

        // One frame is enough: the gem sits inside the collect radius and the player is
        // alive, so its own update collects it.
        yield return null;

        Assert.AreEqual(0, DroppedGems().Count, "A gem under a living player must be collected.");

        Enemy second = SpawnEnemy(Vector2.zero, xpValue: 1);
        second.TakeDamage(1);

        List<Gem> secondDrop = DroppedGems();
        Assert.AreEqual(1, secondDrop.Count, "The second kill must drop a gem too.");
        Assert.AreEqual(firstId, secondDrop[0].GetInstanceID(),
            "A collected gem must come back from the pool instead of a new one being created.");
    }
}
