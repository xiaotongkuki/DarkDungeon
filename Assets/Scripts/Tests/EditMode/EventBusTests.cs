using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Unit tests for the bus itself: delivery to every subscriber, exact arguments,
/// silence after unsubscribing, and safety when nobody is listening.
///
/// The bus is static, so a subscription made in one test would survive into the
/// next. Every test therefore unsubscribes in TearDown, and the handlers are kept
/// as fields precisely so they can be unsubscribed by identity.
/// </summary>
public class EventBusTests
{
    private int _enemyKilledCount;
    private Vector2 _lastKilledPosition;
    private EnemyData _lastKilledData;
    private int _playerDiedCount;
    private int _gemCollectedTotal;
    private int _lastXpInLevel;
    private int _lastXpRequired;
    private int _levelUpCount;
    private int _lastLevel;
    private int _statsChangedCount;

    /// <summary>Records an enemy kill; subscribed by the tests that need it.</summary>
    /// <param name="position">Death position carried by the event.</param>
    /// <param name="data">Balance asset carried by the event.</param>
    private void OnEnemyKilled(Vector2 position, EnemyData data)
    {
        _enemyKilledCount++;
        _lastKilledPosition = position;
        _lastKilledData = data;
    }

    /// <summary>Records a player death; subscribed by the tests that need it.</summary>
    private void OnPlayerDied()
    {
        _playerDiedCount++;
    }

    /// <summary>Records collected experience; subscribed by the tests that need it.</summary>
    /// <param name="xpAmount">Experience carried by the event.</param>
    private void OnGemCollected(int xpAmount)
    {
        _gemCollectedTotal += xpAmount;
    }

    /// <summary>Records experience announcements; subscribed by the tests that need it.</summary>
    /// <param name="xpInLevel">Experience banked toward the next level.</param>
    /// <param name="requiredForNext">Experience the next level needs.</param>
    private void OnXpChanged(int xpInLevel, int requiredForNext)
    {
        _lastXpInLevel = xpInLevel;
        _lastXpRequired = requiredForNext;
    }

    /// <summary>Records level-ups; subscribed by the tests that need it.</summary>
    /// <param name="level">Level carried by the event.</param>
    private void OnLevelUp(int level)
    {
        _levelUpCount++;
        _lastLevel = level;
    }

    /// <summary>Records stat-change announcements; subscribed by the tests that need it.</summary>
    private void OnStatsChanged()
    {
        _statsChangedCount++;
    }

    /// <summary>
    /// Clears the counters and the wiring before each test.
    ///
    /// Both halves are needed, and for different reasons:
    /// Unity's EditMode runner reuses a single fixture instance for every test in
    /// the class (verified by logging the instance hash), so fields keep their
    /// value from the previous test and must be reset here.
    /// The bus, on the other hand, is static and outlives the fixture entirely, so
    /// the subscriptions have to be dropped in TearDown.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _enemyKilledCount = 0;
        _lastKilledPosition = default(Vector2);
        _lastKilledData = null;
        _playerDiedCount = 0;
        _gemCollectedTotal = 0;
        _lastXpInLevel = -1;
        _lastXpRequired = -1;
        _levelUpCount = 0;
        _lastLevel = 0;
        _statsChangedCount = 0;
    }

    /// <summary>
    /// Drops this fixture's subscriptions. Without it a later test in the same run
    /// would still be wired into the static bus.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        EventBus.EnemyKilled -= OnEnemyKilled;
        EventBus.PlayerDied -= OnPlayerDied;
        EventBus.GemCollected -= OnGemCollected;
        EventBus.XpChanged -= OnXpChanged;
        EventBus.LevelUp -= OnLevelUp;
        EventBus.StatsChanged -= OnStatsChanged;
    }

    [Test]
    public void EnemyKilled_DeliversThePositionAndTheDeadEnemysData()
    {
        EventBus.EnemyKilled += OnEnemyKilled;
        EnemyData data = ScriptableObject.CreateInstance<EnemyData>();

        try
        {
            EventBus.RaiseEnemyKilled(new Vector2(3.5f, -1.25f), data);

            Assert.AreEqual(1, _enemyKilledCount, "The subscriber must be called exactly once.");
            Assert.AreEqual(new Vector2(3.5f, -1.25f), _lastKilledPosition,
                "The event must carry the death position unchanged.");
            Assert.AreSame(data, _lastKilledData,
                "The event must carry the dead enemy's data asset, so drops can read its worth.");
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void EnemyKilled_ReachesEverySubscriber()
    {
        int secondCount = 0;
        System.Action<Vector2, EnemyData> second = (_, __) => secondCount++;
        EventBus.EnemyKilled += OnEnemyKilled;
        EventBus.EnemyKilled += second;

        try
        {
            EventBus.RaiseEnemyKilled(Vector2.zero, null);

            Assert.AreEqual(1, _enemyKilledCount, "The first subscriber must be called.");
            Assert.AreEqual(1, secondCount, "The second subscriber must be called too.");
        }
        finally
        {
            EventBus.EnemyKilled -= second;
        }
    }

    [Test]
    public void UnsubscribedHandler_StopsReceiving()
    {
        EventBus.EnemyKilled += OnEnemyKilled;
        EventBus.RaiseEnemyKilled(Vector2.one, null);
        Assert.AreEqual(1, _enemyKilledCount, "Sanity check: the handler must fire while subscribed.");

        EventBus.EnemyKilled -= OnEnemyKilled;
        EventBus.RaiseEnemyKilled(Vector2.one, null);

        Assert.AreEqual(1, _enemyKilledCount, "A removed handler must not be called again.");
    }

    [Test]
    public void PlayerDied_ReachesSubscribers()
    {
        EventBus.PlayerDied += OnPlayerDied;

        EventBus.RaisePlayerDied();

        Assert.AreEqual(1, _playerDiedCount);
    }

    [Test]
    public void GemCollected_DeliversTheExperience()
    {
        EventBus.GemCollected += OnGemCollected;

        EventBus.RaiseGemCollected(2);
        EventBus.RaiseGemCollected(3);

        Assert.AreEqual(5, _gemCollectedTotal, "Every collection must be delivered with its worth.");
    }

    [Test]
    public void XpChanged_DeliversProgressAndRequirement()
    {
        EventBus.XpChanged += OnXpChanged;

        EventBus.RaiseXpChanged(4, 10);

        Assert.AreEqual(4, _lastXpInLevel);
        Assert.AreEqual(10, _lastXpRequired,
            "The display must be able to show progress without reading the curve itself.");
    }

    [Test]
    public void LevelUp_DeliversTheNewLevel()
    {
        EventBus.LevelUp += OnLevelUp;

        EventBus.RaiseLevelUp(3);

        Assert.AreEqual(1, _levelUpCount);
        Assert.AreEqual(3, _lastLevel);
    }

    [Test]
    public void StatsChanged_ReachesSubscribers()
    {
        EventBus.StatsChanged += OnStatsChanged;

        EventBus.RaiseStatsChanged();

        Assert.AreEqual(1, _statsChangedCount);
    }

    [Test]
    public void RaisingWithNoSubscribers_DoesNotThrow()
    {
        // A scene with no listeners is the normal case for most events, so raising
        // must be a no-op rather than a null reference.
        Assert.DoesNotThrow(() => EventBus.RaiseEnemyKilled(Vector2.zero, null));
        Assert.DoesNotThrow(() => EventBus.RaisePlayerDied());
        Assert.DoesNotThrow(() => EventBus.RaiseScoreChanged(1));
        Assert.DoesNotThrow(() => EventBus.RaiseGemCollected(1));
        Assert.DoesNotThrow(() => EventBus.RaiseXpChanged(1, 5));
        Assert.DoesNotThrow(() => EventBus.RaiseLevelUp(2));
        Assert.DoesNotThrow(() => EventBus.RaiseStatsChanged());
    }

    [Test]
    public void ScoreChanged_DeliversTheNewTotal()
    {
        int announced = -1;
        System.Action<int> listener = score => announced = score;
        EventBus.ScoreChanged += listener;

        try
        {
            EventBus.RaiseScoreChanged(42);

            Assert.AreEqual(42, announced);
        }
        finally
        {
            EventBus.ScoreChanged -= listener;
        }
    }
}
