using UnityEngine;

/// <summary>
/// Fires at the closest enemy on a fixed cadence, using whatever
/// <see cref="WeaponData"/> asset is assigned.
///
/// Targeting is one spatial-grid query per shot rather than a per-frame scan: the
/// grid returns only live enemies and resolves the nearest one in about a
/// microsecond, so there is nothing worth caching or invalidating between shots.
///
/// The weapon is read through its properties rather than copied into fields, so
/// swapping the asset or retuning it in the Inspector changes behaviour on the
/// very next shot.
/// </summary>
public class PlayerAutoAttack : MonoBehaviour
{
    /// <summary>Parked-shot capacity hint; the pool still grows to meet demand.</summary>
    private const int PoolCapacity = 32;

    /// <summary>Most shots kept parked; beyond it a spent shot is destroyed instead.</summary>
    private const int PoolMaxSize = 256;

    [Header("Weapon")]
    [Tooltip("Weapon this attacker fires. Required: it supplies the cadence, range, damage and shot prefab.")]
    [SerializeField] private WeaponData _weapon;

    private IntervalTimer _timer;
    private PlayerStatModifiers _modifiers;

    /// <summary>The held gun; optional, bullets fall back to the body centre without one.</summary>
    private PlayerWeaponGrip _grip;

    /// <summary>Pool the shots come from, built on the first shot fired.</summary>
    private ComponentPool<Projectile> _pool;

    /// <summary>Shot prefab the pool was built for, so a swapped prefab can be reported.</summary>
    private Projectile _poolPrefab;

    /// <summary>Parked shots live here, so they are reclaimed with the player.</summary>
    private Transform _poolContainer;

    /// <summary>
    /// The weapon being fired, exposed for the visual linework that must share
    /// its numbers - the grip reads the ring from here rather than owning a
    /// second, drifting copy of the range.
    /// </summary>
    public WeaponData Weapon => _weapon;

    /// <summary>
    /// Rejects an attacker with no weapon asset, the same way the enemy
    /// behaviours reject a missing <see cref="EnemyData"/>: a component disabled
    /// during its own Awake never receives OnEnable, so a misconfigured prefab is
    /// inert and loud rather than firing with invented values.
    ///
    /// The upgrade bonuses are cached here too, but they are optional: a player
    /// without them simply fires the weapon as authored.
    /// </summary>
    private void Awake()
    {
        _modifiers = GetComponent<PlayerStatModifiers>();
        _grip = GetComponentInChildren<PlayerWeaponGrip>();
        if (_weapon == null)
        {
            Debug.LogError($"PlayerAutoAttack on '{name}' has no WeaponData assigned and will not fire. " +
                           "Assign a weapon asset to the PlayerAutoAttack component.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Starts a fresh cadence and starts listening for upgrades that change it.
    /// </summary>
    private void OnEnable()
    {
        if (_weapon == null)
        {
            return;
        }

        EventBus.StatsChanged += OnStatsChanged;
        RebuildTimer();
    }

    /// <summary>
    /// Drops the subscription; the bus is static and would otherwise keep this
    /// attacker reacting to a scene it no longer belongs to.
    /// </summary>
    private void OnDisable()
    {
        EventBus.StatsChanged -= OnStatsChanged;
    }

    /// <summary>
    /// Rebuilds the firing cadence from the weapon and the player's fire-rate
    /// upgrades.
    ///
    /// The cadence has to be rebuilt rather than read per shot because the timer
    /// owns the interval: without this, a fire-rate upgrade taken mid-run would
    /// never reach the weapon that is already firing.
    /// </summary>
    private void RebuildTimer()
    {
        float scale = _modifiers != null ? _modifiers.FireIntervalScale : 1f;
        _timer = new IntervalTimer(Mathf.Max(0.01f, _weapon.FireInterval * scale));
    }

    /// <summary>
    /// Rebuilds the cadence after an upgrade landed.
    /// </summary>
    private void OnStatsChanged()
    {
        if (_weapon != null)
        {
            RebuildTimer();
        }
    }

    /// <summary>
    /// Advances the cadence and, when it completes, fires one shot at the closest
    /// enemy in range.
    ///
    /// The manager is read on demand rather than cached in awake: component wake
    /// order is not guaranteed, and a reference captured before the manager exists
    /// would stay null forever.
    /// </summary>
    private void Update()
    {
        if (_timer == null || !_timer.Advance(Time.deltaTime))
        {
            return;
        }

        Projectile prefab = _weapon.ProjectilePrefab;
        if (prefab == null)
        {
            return;
        }

        EnemyManager manager = EnemyManager.Instance;
        if (manager == null)
        {
            return;
        }

        Vector2 origin = _grip != null ? _grip.MuzzlePosition : (Vector2)transform.position;
        if (!manager.TryGetNearest(origin, out Enemy target, _weapon.Range))
        {
            // Nothing in range: the interval is spent rather than banked, so a
            // crowd arriving later cannot trigger a burst of shots.
            return;
        }

        Vector2 toTarget = (Vector2)target.transform.position - origin;
        if (toTarget.sqrMagnitude <= 0f)
        {
            // Target and muzzle occupy the same point: there is no direction to
            // fire in, and normalising would collapse to a zero vector.
            return;
        }

        float bonus = _modifiers != null ? _modifiers.DamageBonus : 0f;
        int damage = Mathf.Max(1, Mathf.RoundToInt(_weapon.Damage + bonus));

        Projectile shot = TakeShot(prefab);
        if (shot == null)
        {
            return;
        }

        // Launch re-derives the sprite's rotation from the flight direction, so
        // the identity pose here only acts as the base orientation.
        shot.transform.SetPositionAndRotation(origin, Quaternion.identity);
        shot.Launch(toTarget, _weapon, damage);

        if (_grip != null)
        {
            _grip.PlayMuzzleFlash();
        }
    }

    /// <summary>
    /// Returns a shot to position and launch, building the pool the first time one is
    /// needed.
    ///
    /// The pool is built once and kept. Retuning the weapon asset still reaches the
    /// next shot, because damage, cadence and range are all read per shot - but
    /// pointing the weapon at a different shot prefab mid-run is not supported, and
    /// this says so rather than quietly rebuilding a pool underneath shots that are
    /// still in flight.
    /// </summary>
    /// <param name="prefab">Shot prefab the weapon currently names.</param>
    /// <returns>A shot ready to be configured, or null when pooling is impossible.</returns>
    private Projectile TakeShot(Projectile prefab)
    {
        if (_pool == null)
        {
            _poolContainer = new GameObject($"Pool_{prefab.name}").transform;
            _poolContainer.SetParent(transform, worldPositionStays: false);
            _poolPrefab = prefab;
            _pool = new ComponentPool<Projectile>(prefab, _poolContainer, PoolCapacity, PoolMaxSize);
        }
        else if (_poolPrefab != prefab)
        {
            Debug.LogError($"PlayerAutoAttack on '{name}' has a pool for '{_poolPrefab.name}' but the " +
                           "weapon now names a different shot prefab. Restart the run to pool the new " +
                           "shot; firing keeps using the pooled one.", this);
            return null;
        }

        return _pool.Get();
    }
}
