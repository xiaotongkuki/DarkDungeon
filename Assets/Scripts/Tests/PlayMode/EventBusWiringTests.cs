using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Play-mode tests for the producers. A bus is only worth having if the game
/// actually announces on it, so these kill a real enemy and a real player and
/// listen: they are the regression guard against an event being defined but never
/// raised.
/// </summary>
public class EventBusWiringTests
{
    private GameObject _managerObject;
    private GameObject _enemyObject;
    private GameObject _playerObject;
    private int _enemyKilledCount;
    private Vector2 _lastKilledPosition;
    private EnemyData _lastKilledData;
    private int _playerDiedCount;

    [SetUp]
    public void SetUp()
    {
        // The runner reuses one fixture instance for every test in the class, so the
        // counters keep their value from the previous test and must be cleared here.
        _enemyKilledCount = 0;
        _lastKilledPosition = default(Vector2);
        _lastKilledData = null;
        _playerDiedCount = 0;

        _managerObject = new GameObject("EnemyManager");
        _managerObject.AddComponent<EnemyManager>();

        EventBus.EnemyKilled += OnEnemyKilled;
        EventBus.PlayerDied += OnPlayerDied;
    }

    [TearDown]
    public void TearDown()
    {
        // Unsubscribing is mandatory: the bus is static and would otherwise carry
        // these handlers into every later test.
        EventBus.EnemyKilled -= OnEnemyKilled;
        EventBus.PlayerDied -= OnPlayerDied;

        if (_enemyObject != null)
        {
            Object.DestroyImmediate(_enemyObject);
        }
        if (_playerObject != null)
        {
            Object.DestroyImmediate(_playerObject);
        }
        Object.DestroyImmediate(_managerObject);
    }

    /// <summary>Records an enemy kill announced by the game.</summary>
    /// <param name="position">Death position carried by the event.</param>
    /// <param name="data">Balance asset carried by the event.</param>
    private void OnEnemyKilled(Vector2 position, EnemyData data)
    {
        _enemyKilledCount++;
        _lastKilledPosition = position;
        _lastKilledData = data;
    }

    /// <summary>Records a player death announced by the game.</summary>
    private void OnPlayerDied()
    {
        _playerDiedCount++;
    }

    [Test]
    public void KillingAnEnemy_AnnouncesTheKillWithItsPositionAndData()
    {
        var go = new GameObject("Enemy");
        go.SetActive(false);
        go.transform.position = new Vector3(4f, -2f, 0f);
        Enemy enemy = go.AddComponent<Enemy>();
        EnemyData data = TestData.CreateEnemyData(maxHealth: 1);
        TestData.SetObjectReference(enemy, "_data", data);
        go.SetActive(true);
        _enemyObject = go;

        enemy.TakeDamage(1);

        Assert.AreEqual(1, _enemyKilledCount, "Enemy.Die must announce the kill on the bus.");
        Assert.AreEqual(new Vector2(4f, -2f), _lastKilledPosition,
            "The announcement must carry the position the enemy died at.");
        Assert.AreSame(data, _lastKilledData,
            "The announcement must carry the dead enemy's data, which is what a drop reads.");
    }

    [Test]
    public void KillingThePlayer_AnnouncesTheDeath()
    {
        var go = new GameObject("Player");
        go.SetActive(false);
        PlayerHealth health = go.AddComponent<PlayerHealth>();
        TestData.SetObjectReference(health, "_data", TestData.CreatePlayerData(maxHealth: 1));
        go.SetActive(true);
        _playerObject = go;

        health.TakeDamage(1);

        Assert.AreEqual(1, _playerDiedCount, "PlayerHealth.Die must announce the death on the bus.");
    }

    [Test]
    public void NonLethalDamage_AnnouncesNothing()
    {
        var go = new GameObject("Enemy");
        go.SetActive(false);
        Enemy enemy = go.AddComponent<Enemy>();
        TestData.SetObjectReference(enemy, "_data", TestData.CreateEnemyData(maxHealth: 3));
        go.SetActive(true);
        _enemyObject = go;

        enemy.TakeDamage(1);

        Assert.IsTrue(enemy.IsAlive, "Sanity check: the enemy must survive a single hit.");
        Assert.AreEqual(0, _enemyKilledCount, "A wounded enemy is not a kill and must stay silent.");
    }
}
