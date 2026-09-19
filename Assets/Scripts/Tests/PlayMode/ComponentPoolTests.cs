using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the pool itself: what a hand-out does, what a hand-back does,
/// what happens at the cap, and what a reused instance's state looks like.
///
/// Shots stand in for the pooled type because they need no data asset and their
/// reuse contract - do not tick until launched - is the one easiest to get wrong.
/// </summary>
public class ComponentPoolTests
{
    /// <summary>Parked cap used by most tests, kept small so overflow is cheap to reach.</summary>
    private const int MaxSize = 4;

    /// <summary>Seconds to wait for a deferred destroy to land.</summary>
    private const float DestroyDelay = 0.05f;

    private GameObject _prefabObject;
    private GameObject _ownerObject;
    private Projectile _prefab;
    private ComponentPool<Projectile> _pool;

    [SetUp]
    public void SetUp()
    {
        // The instantiate source: built inactive, given its component, then activated,
        // so the component's own enable runs in a defined order.
        _prefabObject = new GameObject("ShotPrefab");
        _prefabObject.SetActive(false);
        _prefab = _prefabObject.AddComponent<Projectile>();
        _prefabObject.SetActive(true);

        // The pool's container is parented to this, which is what lets a scene teardown
        // reclaim parked instances.
        _ownerObject = new GameObject("Owner");
        _pool = new ComponentPool<Projectile>(_prefab, _ownerObject.transform, capacity: 4, maxSize: MaxSize);
    }

    [TearDown]
    public void TearDown()
    {
        if (_ownerObject != null)
        {
            Object.DestroyImmediate(_ownerObject);
        }
        if (_prefabObject != null)
        {
            Object.DestroyImmediate(_prefabObject);
        }
    }

    /// <summary>
    /// Yields until the given amount of game time has passed, regardless of how many
    /// frames that takes on the current machine and editor focus state.
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
    public void Get_ActivatesAnInstanceAndCountsIt()
    {
        Projectile shot = _pool.Get();

        Assert.IsNotNull(shot, "An empty pool must create an instance rather than return nothing.");
        Assert.IsTrue(shot.gameObject.activeSelf, "A handed-out instance must be active.");
        Assert.AreEqual(1, _pool.CountActive, "The instance must count as handed out.");
        Assert.AreEqual(0, _pool.CountInactive, "Nothing is parked while it is out.");
    }

    [UnityTest]
    public IEnumerator AHandedOutInstance_IsDetachedFromItsPoolContainer()
    {
        // The container belongs to the pool owner; an active instance parented
        // under it would inherit the owner's motion - the bullet-hugging-player
        // regression. Detaching on hand-out is what keeps a shot in world space.
        Projectile shot = _pool.Get();

        Assert.IsNull(shot.transform.parent,
            "A handed-out instance must live in the world, not under the pool container.");

        _pool.Release(shot);

        Assert.AreEqual(_ownerObject.transform, shot.transform.parent,
            "Parking must still place the instance back under the container for teardown reclaim.");

        yield break;
    }

    [Test]
    public void Release_ParksTheInstanceInactiveUnderTheContainer()
    {
        Projectile shot = _pool.Get();

        _pool.Release(shot);

        Assert.IsFalse(shot.gameObject.activeSelf, "A parked instance must be inactive.");
        Assert.AreEqual(0, _pool.CountActive, "A parked instance is no longer handed out.");
        Assert.AreEqual(1, _pool.CountInactive, "It must be waiting to be reused.");
        Assert.AreEqual(_ownerObject.transform, shot.transform.parent,
            "Parked instances must live under the owner, so a teardown reclaims them.");
    }

    [Test]
    public void GetAfterRelease_ReturnsTheSameInstance()
    {
        Projectile first = _pool.Get();
        _pool.Release(first);

        Projectile second = _pool.Get();

        Assert.AreSame(first, second, "A released instance must be reused before a new one is made.");
    }

    [Test]
    public void Release_AnInstanceThePoolDoesNotHold_Throws()
    {
        // Releasing a stranger would let the pool hand the same instance out twice, so
        // the pool refuses it rather than quietly accepting the corruption.
        var stranger = new GameObject("Stranger");
        Projectile foreign = stranger.AddComponent<Projectile>();

        Assert.That(() => _pool.Release(foreign), Throws.Exception,
            "Releasing an instance the pool never handed out must be reported, not ignored.");

        Object.DestroyImmediate(stranger);
    }

    [UnityTest]
    public IEnumerator BeyondTheCap_ReleasedInstancesAreDestroyedNotKept()
    {
        // One more than the parked cap, so exactly one has nowhere to park.
        var shots = new Projectile[MaxSize + 1];
        for (int i = 0; i < shots.Length; i++)
        {
            shots[i] = _pool.Get();
        }

        for (int i = 0; i < shots.Length; i++)
        {
            _pool.Release(shots[i]);
        }

        yield return WaitForGameTime(DestroyDelay);

        Assert.AreEqual(MaxSize, _pool.CountInactive,
            "The parked stack must stop at its cap instead of growing without limit.");
        Assert.IsTrue(shots[shots.Length - 1] == null,
            "The instance that did not fit must have been destroyed.");
    }

    [UnityTest]
    public IEnumerator AReusedShot_StaysPutUntilItIsLaunchedAgain()
    {
        WeaponData weapon = TestData.CreateWeaponData(damage: 1, projectileSpeed: 20f,
            projectileLifetime: 0.1f, projectileHitRadius: 0.35f);

        Projectile shot = _pool.Get();
        shot.Launch(Vector2.right, weapon);

        // The lifetime is short, so the shot retires into the pool on its own.
        yield return WaitForGameTime(0.3f);

        Assert.AreEqual(0, _pool.CountActive, "A spent shot must retire back into its pool.");
        Assert.AreEqual(1, _pool.CountInactive, "It must be waiting to be reused.");

        Projectile reused = _pool.Get();
        Assert.AreSame(shot, reused, "The next shot must be the recycled one.");

        Vector3 parked = reused.transform.position;
        yield return WaitForGameTime(0.2f);

        Assert.AreEqual(parked, reused.transform.position,
            "A reused shot must not fly on its own; only Launch may send it.");

        Object.DestroyImmediate(weapon);
    }
}
