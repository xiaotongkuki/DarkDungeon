using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Play-mode tests for scoring: kills accumulate into a score, the total is
/// announced on the bus, a wounded enemy is not a kill, and a disabled tracker
/// scores nothing.
/// </summary>
public class ScoreTrackerTests
{
    private GameObject _managerObject;
    private GameObject _trackerObject;
    private readonly List<GameObject> _enemyObjects = new List<GameObject>();
    private int _announcementCount;
    private int _lastAnnouncedScore;

    [SetUp]
    public void SetUp()
    {
        // The runner reuses one fixture instance, so the counters must be cleared.
        _announcementCount = 0;
        _lastAnnouncedScore = 0;

        _managerObject = new GameObject("EnemyManager");
        _managerObject.AddComponent<EnemyManager>();

        EventBus.ScoreChanged += OnScoreChanged;
    }

    [TearDown]
    public void TearDown()
    {
        EventBus.ScoreChanged -= OnScoreChanged;

        for (int i = 0; i < _enemyObjects.Count; i++)
        {
            if (_enemyObjects[i] != null)
            {
                Object.DestroyImmediate(_enemyObjects[i]);
            }
        }
        _enemyObjects.Clear();

        if (_trackerObject != null)
        {
            Object.DestroyImmediate(_trackerObject);
        }
        Object.DestroyImmediate(_managerObject);
    }

    /// <summary>Records an announced score.</summary>
    /// <param name="score">Total announced by the tracker.</param>
    private void OnScoreChanged(int score)
    {
        _announcementCount++;
        _lastAnnouncedScore = score;
    }

    /// <summary>
    /// Creates an enemy. Built inactive so its data asset can be assigned before
    /// Awake runs, which the enemy requires.
    /// </summary>
    /// <param name="position">World position.</param>
    /// <param name="health">Hits it survives.</param>
    /// <returns>The created enemy.</returns>
    private Enemy SpawnEnemy(Vector2 position, int health)
    {
        var go = new GameObject("Enemy");
        go.SetActive(false);
        go.transform.position = position;
        Enemy enemy = go.AddComponent<Enemy>();
        TestData.SetObjectReference(enemy, "_data", TestData.CreateEnemyData(maxHealth: health));
        go.SetActive(true);

        _enemyObjects.Add(go);
        return enemy;
    }

    /// <summary>Creates the tracker under test.</summary>
    /// <returns>The created tracker.</returns>
    private ScoreTracker CreateTracker()
    {
        _trackerObject = new GameObject("ScoreTracker");
        return _trackerObject.AddComponent<ScoreTracker>();
    }

    [Test]
    public void KillingEnemies_AccumulatesAndAnnouncesTheScore()
    {
        ScoreTracker tracker = CreateTracker();

        Enemy first = SpawnEnemy(new Vector2(1f, 0f), health: 1);
        Enemy second = SpawnEnemy(new Vector2(2f, 0f), health: 1);
        Enemy wounded = SpawnEnemy(new Vector2(3f, 0f), health: 3);

        first.TakeDamage(1);
        second.TakeDamage(1);
        wounded.TakeDamage(1);

        Assert.IsTrue(wounded.IsAlive, "Sanity check: the third enemy must survive one hit.");
        Assert.AreEqual(2, tracker.Score, "Only kills may score.");
        Assert.AreEqual(2, _announcementCount, "Each kill must be announced exactly once.");
        Assert.AreEqual(2, _lastAnnouncedScore, "The announcement must carry the running total.");
    }

    [Test]
    public void DisabledTracker_DoesNotScore()
    {
        ScoreTracker tracker = CreateTracker();
        tracker.enabled = false;

        Enemy enemy = SpawnEnemy(new Vector2(1f, 0f), health: 1);
        enemy.TakeDamage(1);

        Assert.AreEqual(0, tracker.Score, "A disabled tracker must not react to kills.");
        Assert.AreEqual(0, _announcementCount, "A disabled tracker must stay silent.");
    }

    [Test]
    public void EnablingResetsTheScore()
    {
        ScoreTracker tracker = CreateTracker();

        Enemy enemy = SpawnEnemy(new Vector2(1f, 0f), health: 1);
        enemy.TakeDamage(1);
        Assert.AreEqual(1, tracker.Score, "Sanity check: the kill must have scored.");

        tracker.enabled = false;
        tracker.enabled = true;

        Assert.AreEqual(0, tracker.Score, "Re-enabling starts a fresh run.");
    }
}
