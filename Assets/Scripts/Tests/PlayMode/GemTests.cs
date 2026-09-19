using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for a gem: it waits until the player is close, is pulled in,
/// pays out exactly once on contact, and stays put when there is no player to
/// collect it.
/// </summary>
public class GemTests
{
    private GameObject _playerObject;
    private GameObject _gemObject;
    private int _collectedCount;
    private int _collectedXp;

    [SetUp]
    public void SetUp()
    {
        // The runner reuses one fixture instance, so the counters must be cleared.
        _collectedCount = 0;
        _collectedXp = 0;

        _playerObject = new GameObject("Player");
        _playerObject.AddComponent<PlayerLocator>();
        _playerObject.transform.position = Vector3.zero;

        EventBus.GemCollected += OnGemCollected;
    }

    [TearDown]
    public void TearDown()
    {
        EventBus.GemCollected -= OnGemCollected;

        if (_gemObject != null)
        {
            Object.DestroyImmediate(_gemObject);
        }
        if (_playerObject != null)
        {
            Object.DestroyImmediate(_playerObject);
        }
    }

    /// <summary>Records a collected gem.</summary>
    /// <param name="xpAmount">Experience the gem was worth.</param>
    private void OnGemCollected(int xpAmount)
    {
        _collectedCount++;
        _collectedXp += xpAmount;
    }

    /// <summary>
    /// Creates a gem at the given position, armed with its worth and magnet bonus.
    /// </summary>
    /// <param name="position">World position to place it at.</param>
    /// <param name="xpValue">Experience it is worth.</param>
    /// <param name="magnetBonus">Extra magnet radius from upgrades.</param>
    /// <returns>The created gem.</returns>
    private Gem CreateGem(Vector2 position, int xpValue = 1, float magnetBonus = 0f)
    {
        _gemObject = new GameObject("Gem");
        _gemObject.transform.position = position;
        Gem gem = _gemObject.AddComponent<Gem>();
        gem.Spawn(xpValue, magnetBonus);
        return gem;
    }

    /// <summary>Distance from the gem to the player, in world units.</summary>
    /// <returns>Current distance.</returns>
    private float DistanceToPlayer()
    {
        return Vector2.Distance(_gemObject.transform.position, _playerObject.transform.position);
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
    public IEnumerator InsideTheMagnetRadius_IsPulledTowardThePlayer()
    {
        CreateGem(new Vector2(3f, 0f));
        float before = DistanceToPlayer();

        yield return WaitForGameTime(0.3f);

        // A slow frame can carry the gem all the way in, so being collected counts
        // as proof too; a gem that ignored the magnet would do neither.
        Assert.IsTrue(_gemObject == null || DistanceToPlayer() < before - 0.1f,
            "A gem inside the magnet radius must close in on the player.");
    }

    [UnityTest]
    public IEnumerator OutsideTheMagnetRadius_StaysWhereItLanded()
    {
        CreateGem(new Vector2(9f, 0f));
        Vector2 before = _gemObject.transform.position;

        yield return WaitForGameTime(0.3f);

        Assert.AreEqual(before, (Vector2)_gemObject.transform.position,
            "A gem outside the magnet radius must wait to be walked over.");
    }

    [UnityTest]
    public IEnumerator ReachingThePlayer_PaysOutExactlyOnce()
    {
        CreateGem(new Vector2(0.3f, 0f), xpValue: 3);

        yield return WaitForGameTime(0.2f);

        Assert.AreEqual(1, _collectedCount, "The gem must be collected exactly once.");
        Assert.AreEqual(3, _collectedXp, "It must pay out the experience it was armed with.");
        Assert.IsTrue(_gemObject == null, "A collected gem must remove itself.");
    }

    [UnityTest]
    public IEnumerator WithoutAnActivePlayer_Waits()
    {
        CreateGem(new Vector2(2f, 0f));
        Vector2 before = _gemObject.transform.position;
        _playerObject.SetActive(false);

        yield return WaitForGameTime(0.3f);

        Assert.AreEqual(before, (Vector2)_gemObject.transform.position,
            "Gems must not chase a dead player.");
        Assert.AreEqual(0, _collectedCount, "A gem must not pay out with no living player.");
    }

    [UnityTest]
    public IEnumerator MagnetBonus_ExtendsThePullRange()
    {
        // 5.5 units out is beyond the prefab's own radius but inside it plus the
        // bonus a magnet upgrade would grant.
        CreateGem(new Vector2(5.5f, 0f), magnetBonus: 1.5f);
        float before = DistanceToPlayer();

        yield return WaitForGameTime(0.3f);

        Assert.IsTrue(_gemObject == null || DistanceToPlayer() < before - 0.1f,
            "The magnet bonus granted by upgrades must widen the pull range.");
    }

    [UnityTest]
    public IEnumerator ArmingTwice_DoesNotWidenThePullRange()
    {
        // Arming is idempotent: the bonus is this gem's own value, not an amount added
        // to the authored radius. Adding to the authored field would work once per
        // fresh instance and then creep upward on every reuse, which is exactly what
        // pooling would do to it.
        Gem gem = CreateGem(new Vector2(7f, 0f), magnetBonus: 1.5f);
        Vector2 before = _gemObject.transform.position;

        // The base radius is 4.5 and the bonus 1.5, so 7 units sits outside the pull.
        // A bonus that accumulated would reach 7.5 and drag the gem in.
        gem.Spawn(1, 1.5f);

        yield return WaitForGameTime(0.3f);

        Assert.AreEqual(before, (Vector2)_gemObject.transform.position,
            "Arming a gem twice must leave its pull range exactly as armed once.");
    }
}
