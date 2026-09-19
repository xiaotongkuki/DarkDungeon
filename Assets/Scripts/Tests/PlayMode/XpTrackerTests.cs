using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for progression: gems become experience, experience becomes
/// levels, one gem can cover several levels, and a finished run stops counting.
/// </summary>
public class XpTrackerTests
{
    private GameObject _trackerObject;
    private XpTracker _tracker;
    private LevelCurveData _curve;
    private int _levelUpCount;
    private int _lastLevel;
    private int _lastXpInLevel;
    private int _lastRequired;

    [SetUp]
    public void SetUp()
    {
        // The runner reuses one fixture instance, so the counters must be cleared.
        _levelUpCount = 0;
        _lastLevel = 0;
        _lastXpInLevel = -1;
        _lastRequired = -1;

        EventBus.LevelUp += OnLevelUp;
        EventBus.XpChanged += OnXpChanged;
    }

    [TearDown]
    public void TearDown()
    {
        EventBus.LevelUp -= OnLevelUp;
        EventBus.XpChanged -= OnXpChanged;

        if (_trackerObject != null)
        {
            Object.DestroyImmediate(_trackerObject);
        }
        if (_curve != null)
        {
            Object.DestroyImmediate(_curve);
        }
    }

    /// <summary>Records a level-up.</summary>
    /// <param name="level">Level the player reached.</param>
    private void OnLevelUp(int level)
    {
        _levelUpCount++;
        _lastLevel = level;
    }

    /// <summary>Records an experience announcement.</summary>
    /// <param name="xpInLevel">Experience banked toward the next level.</param>
    /// <param name="requiredForNext">Experience the next level needs.</param>
    private void OnXpChanged(int xpInLevel, int requiredForNext)
    {
        _lastXpInLevel = xpInLevel;
        _lastRequired = requiredForNext;
    }

    /// <summary>
    /// Creates a tracker on the given curve. Built inactive so the curve can be
    /// assigned before Awake runs, which is where the tracker validates it.
    /// </summary>
    /// <param name="xpRequired">Level curve thresholds.</param>
    private void CreateTracker(params int[] xpRequired)
    {
        _curve = TestData.CreateLevelCurveData(xpRequired);

        _trackerObject = new GameObject("XpTracker");
        _trackerObject.SetActive(false);
        _tracker = _trackerObject.AddComponent<XpTracker>();
        TestData.SetObjectReference(_tracker, "_curve", _curve);
        _trackerObject.SetActive(true);
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

    [Test]
    public void StartsAtLevelOneWithNothingBanked()
    {
        CreateTracker(2, 3);

        Assert.AreEqual(1, _tracker.Level);
        Assert.AreEqual(0, _tracker.XpInLevel);
        Assert.AreEqual(2, _tracker.RequiredForNextLevel, "The first level must cost the first threshold.");
    }

    [Test]
    public void CollectedExperience_BanksAndIsAnnounced()
    {
        CreateTracker(2, 3);

        EventBus.RaiseGemCollected(1);

        Assert.AreEqual(1, _tracker.Level, "One point is not a level yet.");
        Assert.AreEqual(1, _tracker.XpInLevel);
        Assert.AreEqual(1, _lastXpInLevel, "The display must hear the banked experience.");
        Assert.AreEqual(2, _lastRequired, "The display must hear what the next level costs.");
        Assert.AreEqual(0, _levelUpCount, "No level has been reached yet.");
    }

    [Test]
    public void ReachingTheThreshold_LevelsUpAndCarriesTheRemainder()
    {
        CreateTracker(2, 3);

        EventBus.RaiseGemCollected(2);

        Assert.AreEqual(2, _tracker.Level);
        Assert.AreEqual(0, _tracker.XpInLevel, "The spent experience must not carry over by accident.");
        Assert.AreEqual(1, _levelUpCount);
        Assert.AreEqual(2, _lastLevel);
        Assert.AreEqual(3, _tracker.RequiredForNextLevel, "The cost must move to the next threshold.");
    }

    [Test]
    public void OneBigGem_CanCoverSeveralLevels()
    {
        CreateTracker(2, 3);

        EventBus.RaiseGemCollected(5);

        Assert.AreEqual(3, _tracker.Level, "5 points must cover both thresholds.");
        Assert.AreEqual(2, _levelUpCount, "Each level crossed must be announced on its own.");
        Assert.AreEqual(3, _lastLevel);
        Assert.AreEqual(0, _tracker.XpInLevel);
        Assert.AreEqual(0, _tracker.RequiredForNextLevel, "Past the last threshold there is nothing left to buy.");
    }

    [Test]
    public void AtTheLevelCap_ExperienceStillBanks()
    {
        CreateTracker(2);

        EventBus.RaiseGemCollected(2);
        Assert.AreEqual(2, _tracker.Level, "Sanity check: the single threshold must have been crossed.");

        EventBus.RaiseGemCollected(4);

        Assert.AreEqual(2, _tracker.Level, "The curve is exhausted, so no further level is possible.");
        Assert.AreEqual(1, _levelUpCount);
    }

    [Test]
    public void AfterThePlayerDies_ExperienceStopsCounting()
    {
        CreateTracker(2, 3);

        EventBus.RaisePlayerDied();
        EventBus.RaiseGemCollected(10);

        Assert.AreEqual(1, _tracker.Level, "A finished run must not keep leveling up.");
        Assert.AreEqual(0, _tracker.XpInLevel);
    }

    [Test]
    public void ReEnabling_StartsAFreshRun()
    {
        CreateTracker(2, 3);
        EventBus.RaiseGemCollected(1);

        _trackerObject.SetActive(false);
        _trackerObject.SetActive(true);

        Assert.AreEqual(1, _tracker.Level);
        Assert.AreEqual(0, _tracker.XpInLevel);
    }

    [UnityTest]
    public IEnumerator AnnouncesTheStartingStateOnceEveryComponentIsEnabled()
    {
        CreateTracker(2, 3);

        // The initial announcement is made in Start, so a display that subscribed in
        // its own OnEnable is guaranteed to be attached by the time it arrives.
        yield return WaitForGameTime(0.1f);

        Assert.AreEqual(0, _lastXpInLevel);
        Assert.AreEqual(2, _lastRequired);
    }
}
