using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drops an experience gem wherever an enemy dies.
///
/// It subscribes to the bus rather than being called by the enemy, so the enemy
/// stays ignorant of loot and the drop rule can be retuned or removed without
/// touching it. The gem is placed at the death position and nudged by a small random
/// offset, so a wave killed in one spot leaves a readable pile instead of one gem
/// hidden underneath another.
///
/// Gems are the highest-churn object in the game - one per kill - so they come from a
/// pool rather than from <c>Instantiate</c>.
/// </summary>
public class GemSpawner : MonoBehaviour
{
    /// <summary>Parked-gem capacity hint; the pool still grows to meet demand.</summary>
    private const int PoolCapacity = 64;

    /// <summary>Most gems kept parked; beyond it a collected gem is destroyed instead.</summary>
    private const int PoolMaxSize = 512;

    [Header("Drop")]
    [Tooltip("Gem prefab dropped on every kill; the pool is built from it.")]
    [SerializeField] private Gem _gemPrefab;
    [Tooltip("Random offset in units applied to the drop position, so gems do not stack exactly.")]
    [SerializeField] private float _scatterRadius = 0.35f;

    private PlayerStatModifiers _playerModifiers;

    /// <summary>Gems this spawner has dropped and not yet seen reclaimed, so a stage switch can
    /// recycle whatever is still lying on the floor. A set keeps a reused gem
    /// instance from being tracked twice.</summary>
    private readonly HashSet<Gem> _tracked = new HashSet<Gem>();

    /// <summary>Pool the drops come from, built on the first kill.</summary>
    private ComponentPool<Gem> _pool;

    /// <summary>Gem prefab the pool was built for, so a swapped prefab can be reported.</summary>
    private Gem _poolPrefab;

    /// <summary>Parked gems live here, so they are reclaimed with the spawner.</summary>
    private Transform _poolContainer;

    /// <summary>
    /// Rejects a spawner with no gem prefab, like the project's other configurable
    /// components: a component disabled during its own Awake never reaches OnEnable,
    /// so a misconfigured spawner is inert and loud rather than silently dropping
    /// nothing.
    /// </summary>
    private void Awake()
    {
        if (_gemPrefab == null)
        {
            Debug.LogError($"GemSpawner on '{name}' has no gem prefab assigned and will not drop anything.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Starts listening for kills.
    /// </summary>
    private void OnEnable()
    {
        if (_gemPrefab == null)
        {
            return;
        }

        EventBus.EnemyKilled += OnEnemyKilled;
    }

    /// <summary>
    /// Stops listening; the bus is static and would otherwise keep this spawner
    /// dropping gems for a scene it no longer belongs to.
    /// </summary>
    private void OnDisable()
    {
        EventBus.EnemyKilled -= OnEnemyKilled;
    }

    /// <summary>
    /// Drops one gem at the death position, worth whatever the dead enemy's data
    /// says.
    ///
    /// The enemy object is gone by the time anything else reacts, which is why its
    /// data asset travels with the event. A null asset means the prefab was
    /// misconfigured (the enemy would have refused to act), so no gem is dropped.
    /// </summary>
    /// <param name="position">World position the enemy died at.</param>
    /// <param name="data">Balance asset of the enemy that died.</param>
    private void OnEnemyKilled(Vector2 position, EnemyData data)
    {
        if (data == null)
        {
            return;
        }

        ResolvePlayerModifiers();

        Vector2 scatter = Random.insideUnitCircle * _scatterRadius;
        Gem gem = TakeGem();
        if (gem == null)
        {
            return;
        }

        // Placed before arming, so a recycled gem is never briefly live at whatever
        // spot it was collected from.
        gem.transform.SetPositionAndRotation(position + scatter, Quaternion.identity);
        gem.Spawn(data.XpValue, _playerModifiers != null ? _playerModifiers.MagnetRadiusBonus : 0f);
        _tracked.Add(gem);
    }

    /// <summary>
    /// Recycles every tracked gem still in play, without paying any of them out.
    /// Called by the stage switch: the gems belong to the stage they dropped in.
    /// Gems already collected are no-ops through <see cref="Gem.Discard"/>'s
    /// guard, and destroyed instances are skipped by the null report.
    /// </summary>
    public void ClearGroundGems()
    {
        foreach (Gem gem in _tracked)
        {
            if (gem != null)
            {
                gem.Discard();
            }
        }
        _tracked.Clear();
    }

    /// <summary>
    /// Returns a gem to arm, building the pool the first time one is needed.
    ///
    /// Built lazily rather than in <c>Awake</c> so the prefab is only resolved when a
    /// kill actually happens, and kept afterwards. The trade-off is that pointing the
    /// spawner at a different gem prefab mid-run keeps serving the pool built for the
    /// old one, and says so rather than quietly rebuilding a pool underneath gems
    /// still lying on the ground.
    /// </summary>
    /// <returns>A gem ready to be placed and armed, or null when the prefab changed.</returns>
    private Gem TakeGem()
    {
        if (_pool == null)
        {
            _poolContainer = new GameObject($"Pool_{_gemPrefab.name}").transform;
            _poolContainer.SetParent(transform, worldPositionStays: false);
            _poolPrefab = _gemPrefab;
            _pool = new ComponentPool<Gem>(_gemPrefab, _poolContainer, PoolCapacity, PoolMaxSize);
        }
        else if (_poolPrefab != _gemPrefab)
        {
            Debug.LogError($"GemSpawner on '{name}' has a pool for '{_poolPrefab.name}' but the prefab has " +
                           "changed. Restart the run to pool the new gem; drops keep using the pooled one.", this);
            return null;
        }

        return _pool.Get();
    }

    /// <summary>
    /// Caches the player's upgrade bonuses the first time a gem is dropped.
    ///
    /// Resolved lazily rather than in Awake because the player may not exist yet,
    /// and only the magnet radius is read: everything else about the gem is frozen
    /// from its prefab at spawn.
    /// </summary>
    private void ResolvePlayerModifiers()
    {
        if (_playerModifiers != null)
        {
            return;
        }

        PlayerLocator locator = PlayerLocator.Instance;
        if (locator != null && locator.Target != null)
        {
            _playerModifiers = locator.Target.GetComponent<PlayerStatModifiers>();
        }
    }
}
