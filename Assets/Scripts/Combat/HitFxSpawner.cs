using UnityEngine;

/// <summary>
/// Spawns the impact spark that a shot leaves where it hit.
///
/// It is a scene-level singleton in the same shape as
/// <see cref="EnemyManager"/>: bullets fire from anywhere and must not know who
/// stores the effect pool, so they call the static entry point and a missing
/// spawner (tests, prefab variations) simply produces no spark rather than a
/// hard failure.
///
/// The pool is the standard <see cref="ComponentPool{T}"/>: lazily built on the
/// first spawn, generously capped, and parked under a container that is a child
/// of this component, so a scene teardown reclaims parked instances together
/// with their origin.
/// </summary>
public class HitFxSpawner : MonoBehaviour
{
    /// <summary>Parked-effect capacity hint; the pool still grows to meet demand.</summary>
    private const int PoolCapacity = 32;

    /// <summary>Most effects kept parked; beyond it a spent effect is destroyed instead.</summary>
    private const int PoolMaxSize = 256;

    /// <summary>The active spawner, claimed in Awake and released on destroy.</summary>
    public static HitFxSpawner Instance { get; private set; }

    [Header("References")]
    [Tooltip("Pooled impact prefab carrying <see cref=\"HitFx\"/>. Required.")]
    [SerializeField] private HitFx _prefab;

    /// <summary>Pool built on the first effect, hosting parked instances as its child container.</summary>
    private ComponentPool<HitFx> _pool;

    /// <summary>Impact prefab the pool was built for, so a swapped prefab can be reported.</summary>
    private HitFx _poolPrefab;

    /// <summary>Parked effects live here, reclaimed with the spawner.</summary>
    private Transform _poolContainer;

    /// <summary>
    /// Claims the singleton slot; a duplicate is dropped on the spot, matching
    /// <see cref="EnemyManager"/>'s policy of one registry per scene.
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"Duplicate HitFxSpawner on '{name}' removed; keeping '{Instance.name}'.", this);
            Destroy(this);
            return;
        }

        if (_prefab == null)
        {
            Debug.LogError($"HitFxSpawner on '{name}' has no impact prefab and will not spawn effects.", this);
            enabled = false;
        }

        Instance = this;
    }

    /// <summary>
    /// Releases the singleton slot so a later spawner (a reloaded scene, a test
    /// module) can claim it cleanly.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Plays an impact spark at a world point, creating the pool on first use.
    /// No-op when the spawner is absent from the scene.
    /// </summary>
    /// <param name="position">World position of the impact.</param>
    public static void Play(Vector2 position)
    {
        if (Instance == null)
        {
            return;
        }

        Instance.Spawn(position);
    }

    /// <summary>
    /// Takes one effect out of the pool (building the pool the first time) and
    /// arms it at the impact point. A prefab swapped mid-run keeps the old
    /// pooled prefab and says so, exactly like the other pool hosts.
    /// </summary>
    private void Spawn(Vector2 position)
    {
        if (_pool == null)
        {
            _poolContainer = new GameObject("Pool_HitFx").transform;
            _poolContainer.SetParent(transform, worldPositionStays: false);
            _poolPrefab = _prefab;
            _pool = new ComponentPool<HitFx>(_prefab, _poolContainer, PoolCapacity, PoolMaxSize);
        }
        else if (_poolPrefab != _prefab)
        {
            Debug.LogError($"HitFxSpawner on '{name}' has a pool for '{_poolPrefab.name}' but now names a " +
                           "different prefab. Restart the run to pool the new one.", this);
            return;
        }

        HitFx effect = _pool.Get();
        if (effect == null)
        {
            return;
        }

        effect.Arm(position);
    }
}
