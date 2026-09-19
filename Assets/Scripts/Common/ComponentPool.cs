using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Reuses component instances instead of creating and destroying them.
///
/// Shots, enemies and drops are all created and retired constantly, and every
/// create/destroy pair costs an allocation, a component wake-up and a destroy, all
/// of which land as frame-time spikes rather than as a steady cost. A pool turns
/// that churn into a set of state resets.
///
/// It wraps the engine's <see cref="ObjectPool{T}"/> rather than reimplementing one,
/// because that pool already brings the parts that are easy to get wrong: a cap on
/// how many instances are kept and a check against releasing the same instance twice.
/// What this class adds is the Unity half - parenting parked instances under a
/// container so a scene teardown reclaims them, toggling the object active, which is
/// what actually runs the instance's own reset, and refusing to take back an instance
/// the pool never handed out.
///
/// Instances are created lazily and only up to the peak demand, because
/// <see cref="ObjectPool{T}"/> treats its capacity as a hint and does not
/// pre-instantiate. A quiet scene therefore pays nothing for a pool it never fills.
///
/// One pool serves one prefab. A spawner with several enemy types owns one pool per
/// type, which is what keeps each pool's cap meaningful.
/// </summary>
/// <typeparam name="T">Component being pooled.</typeparam>
public sealed class ComponentPool<T> where T : Component, IPooledComponent<T>
{
    /// <summary>Instances kept parked when no cap is asked for.</summary>
    private const int DefaultMaxSize = 1024;

    private readonly ObjectPool<T> _pool;
    private readonly Transform _container;

    /// <summary>
    /// Instances handed out and not yet taken back.
    ///
    /// The engine's pool only notices the same instance being released twice, so this
    /// set is what turns "released an instance this pool never handed out" from a
    /// silent double-hand-out into a reported error.
    /// </summary>
    private readonly HashSet<T> _handedOut = new HashSet<T>();

    /// <summary>
    /// Creates a pool for one prefab.
    /// </summary>
    /// <param name="prefab">Prefab cloned to fill the pool. Required.</param>
    /// <param name="container">
    /// Parent for parked instances. Required, and should belong to the component that
    /// owns the pool: a parked instance then disappears with its owner instead of
    /// leaking into the next scene or the next test.
    /// </param>
    /// <param name="capacity">Capacity hint for the parked stack; zero takes a small default.</param>
    /// <param name="maxSize">
    /// Most instances kept parked. Beyond it, a released instance is destroyed rather
    /// than kept, which bounds what a pool can hold on to. Zero or less takes the
    /// built-in default.
    /// </param>
    public ComponentPool(T prefab, Transform container, int capacity = 0, int maxSize = 0)
    {
        if (prefab == null)
        {
            throw new System.ArgumentNullException(nameof(prefab));
        }

        if (container == null)
        {
            throw new System.ArgumentNullException(nameof(container));
        }

        int parkedCapacity = capacity > 0 ? capacity : 16;
        int parkedLimit = maxSize > 0 ? maxSize : DefaultMaxSize;

        _container = container;
        _pool = new ObjectPool<T>(
            () => Object.Instantiate(prefab, container),
            OnTaken,
            OnReturned,
            OnDiscarded,
            collectionCheck: true,
            defaultCapacity: parkedCapacity,
            maxSize: parkedLimit);
    }

    /// <summary>Instances currently handed out.</summary>
    public int CountActive => _pool.CountActive;

    /// <summary>Instances currently parked and ready to reuse.</summary>
    public int CountInactive => _pool.CountInactive;

    /// <summary>Instances the pool knows about, active and parked together.</summary>
    public int CountAll => _pool.CountAll;

    /// <summary>
    /// Hands out an instance, creating one if the pool is empty. The instance is
    /// active and already knows its pool; the caller positions and configures it.
    /// </summary>
    /// <returns>A ready instance.</returns>
    public T Get()
    {
        T instance = _pool.Get();
        _handedOut.Add(instance);
        return instance;
    }

    /// <summary>
    /// Takes an instance back and parks it.
    ///
    /// An instance the pool never handed out, or the same one twice, is refused with
    /// an exception rather than accepted: parking either would let the pool give one
    /// object to two callers, which is far harder to diagnose later than here.
    /// </summary>
    /// <param name="instance">Instance to park.</param>
    /// <exception cref="System.InvalidOperationException">
    /// The instance was not handed out by this pool, or has already been returned.
    /// </exception>
    public void Release(T instance)
    {
        if (instance == null || !_handedOut.Remove(instance))
        {
            throw new System.InvalidOperationException(
                "ComponentPool.Release was given an instance it is not holding. Releasing it would let " +
                "the pool hand the same object to two callers.");
        }

        _pool.Release(instance);
    }

    /// <summary>
    /// Destroys every parked instance. Handed-out instances are untouched, so a
    /// caller that clears a pool mid-run does not delete objects still in play.
    /// </summary>
    public void Clear()
    {
        _pool.Clear();
    }

    /// <summary>
    /// Activates a handed-out instance and tells it which pool owns it.
    ///
    /// Activation comes first so the instance's own <c>OnEnable</c> runs before the
    /// caller touches it - that callback is where a reused component drops the
    /// previous use's state.
    ///
    /// The instance is then detached from the container. Parking puts it back
    /// under the pool owner, so handing it out still parented would drag every
    /// active instance along with that owner's transform - bullets fired by a
    /// moving player would travel in the player's frame instead of the world's,
    /// a straight shot visibly bending while the player moves.
    /// </summary>
    /// <param name="instance">Instance being handed out.</param>
    private void OnTaken(T instance)
    {
        instance.Pool = this;
        instance.transform.SetParent(null, worldPositionStays: true);
        instance.gameObject.SetActive(true);
    }

    /// <summary>
    /// Parks a returned instance: deactivated, and back under the container so it is
    /// out of the scene's way and is reclaimed when the owner goes.
    /// </summary>
    /// <param name="instance">Instance being parked.</param>
    private void OnReturned(T instance)
    {
        instance.gameObject.SetActive(false);
        instance.transform.SetParent(_container, worldPositionStays: false);
    }

    /// <summary>
    /// Destroys an instance the pool is dropping, which happens when the parked stack
    /// is already at its cap or when the pool is cleared. A reference that has already
    /// been destroyed by a scene teardown is skipped rather than reported.
    /// </summary>
    /// <param name="instance">Instance being dropped.</param>
    private static void OnDiscarded(T instance)
    {
        if (instance != null)
        {
            Object.Destroy(instance.gameObject);
        }
    }
}
