using UnityEngine;

/// <summary>
/// A spawned enemy: health, death, and registration with <see cref="EnemyManager"/>.
///
/// It is an <see cref="IDamageable"/>, so weapons hit it through that contract
/// rather than through this concrete type.
///
/// This component owns the prefab's <see cref="EnemyData"/> reference and the
/// sibling behaviours read it from here, so a prefab only ever has one asset slot
/// to keep filled.
///
/// An enemy handed out by a <see cref="ComponentPool{T}"/> goes back there on death
/// instead of being destroyed; one created directly still destroys itself. Health and
/// the dying guard are both reset in <see cref="OnEnable"/>, which is what makes
/// reuse safe.
/// </summary>
public class Enemy : MonoBehaviour, IDamageable, IPooledComponent<Enemy>
{
    [Header("Data")]
    [Tooltip("Balance values for this enemy type. Required: without it the enemy has no health to give.")]
    [SerializeField] private EnemyData _data;

    /// <summary>Hits remaining; refilled from the data asset on enable.</summary>
    public int CurrentHealth { get; private set; }

    /// <summary>True while the enemy still has health left.</summary>
    public bool IsAlive => CurrentHealth > 0;

    /// <summary>
    /// Balance values for this enemy type, or null when the prefab is
    /// misconfigured. Sibling behaviours read this instead of carrying their own
    /// reference, so one prefab cannot end up with two disagreeing assets.
    ///
    /// Safe to read at any time: a serialized field is filled in before any Awake
    /// runs, so there is no component wake-order hazard.
    /// </summary>
    public EnemyData Data => _data;

    /// <summary>
    /// Guards <see cref="Die"/> against running twice. Kept separate from
    /// <see cref="IsAlive"/> because lethal damage drops health below zero before
    /// Die is reached, so a health-based guard would bail out and the enemy would
    /// never actually be removed.
    /// </summary>
    private bool _isDying;

    /// <summary>The pool that handed this enemy out, or null when it was created directly.</summary>
    private ComponentPool<Enemy> _pool;

    /// <summary>
    /// The pool that handed this enemy out, or null when it was created directly.
    /// Assigned by <see cref="ComponentPool{T}"/>; null means the enemy owns its own
    /// lifetime and destroys itself on death.
    /// </summary>
    public ComponentPool<Enemy> Pool
    {
        set => _pool = value;
    }

    /// <summary>
    /// Rejects a prefab with no data asset before anything else runs.
    ///
    /// Disabling here rather than in OnEnable is deliberate: a component disabled
    /// during its own Awake never receives OnEnable, so the enemy can neither
    /// register nor act, and the error is the only thing it does. Falling back to
    /// silent defaults would hide the misconfiguration until someone noticed the
    /// wrong damage numbers in play.
    /// </summary>
    private void Awake()
    {
        if (_data == null)
        {
            Debug.LogError($"Enemy '{name}' has no EnemyData assigned and will be inert. " +
                           "Assign a data asset to the Enemy component.", this);
            enabled = false;
        }
    }

    /// <summary>True while the enemy is registered with the manager; drives the late recovery retry.</summary>
    private bool _registered;

    /// <summary>
    /// Refills health from the data asset and registers with the manager.
    ///
    /// Registration lives here rather than in the spawner so every spawn path is
    /// covered, and it runs on enable rather than awake so a recycled (pooled)
    /// enemy rejoins the registry when it is reused.
    ///
    /// Scene-load wake order is not guaranteed: an enemy that wakes before the
    /// manager's Awake cannot register yet. Instead of staying permanently
    /// invisible to every spatial query for that lifetime, the miss is repaired
    /// on the next <see cref="LateUpdate"/>, which is one dictionary insert by
    /// then - the cheap price of letting a designer freely drop enemies in a
    /// scene by hand.
    /// </summary>
    private void OnEnable()
    {
        if (_data == null)
        {
            // Unreachable while Awake disables the component, but re-enabling by
            // hand would otherwise revive an enemy with no data to draw from.
            return;
        }

        _isDying = false;
        CurrentHealth = Mathf.Max(1, _data.MaxHealth);
        _registered = RegisterIfManagerAvailable();
    }

    /// <summary>
    /// Attempts one registration and reports whether it landed.
    /// </summary>
    private bool RegisterIfManagerAvailable()
    {
        EnemyManager manager = EnemyManager.Instance;
        if (manager != null)
        {
            manager.Register(this);
            return true;
        }

        // Pool reuse and normal play never see this path: the manager owns Awake
        // earlier than any pooled spawn. Only a hand-placed enemy that woke
        // before the manager did can get here, which is why the warning stays.
        Debug.LogWarning($"Enemy '{name}' found no EnemyManager yet; it will retry registration.", this);
        return false;
    }

    /// <summary>
    /// Repairs the load-order miss: a monster that woke before the manager now
    /// signs up on the first frame after the manager exists. The flag keeps the
    /// retry to a single dictionary insert instead of a per-frame poll.
    /// </summary>
    private void LateUpdate()
    {
        if (_registered)
        {
            return;
        }

        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.Register(this);
            _registered = true;
        }
    }

    /// <summary>
    /// Unregisters from the manager. Using the disable callback covers both
    /// destruction and pooling, so a dead or recycled enemy always leaves the
    /// registry through exactly one path.
    ///
    /// The removal is queued here, not applied: the grid is mutated at the end of
    /// the frame by <see cref="EnemyManager.FlushRemovals"/>, so for the rest of
    /// this frame the enemy is still registered. Queries filter it out by
    /// <see cref="IsAlive"/> rather than relying on its absence from the grid.
    /// </summary>
    private void OnDisable()
    {
        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.Unregister(this);
        }
        _registered = false;
    }

    /// <summary>
    /// Applies damage and kills the enemy once health runs out.
    ///
    /// Part of the <see cref="IDamageable"/> contract: non-positive amounts and
    /// hits on an already dead enemy are ignored, so callers never guard the call.
    /// </summary>
    /// <param name="amount">Hits to remove; non-positive values are ignored.</param>
    public void TakeDamage(int amount)
    {
        if (!IsAlive || amount <= 0)
        {
            return;
        }

        CurrentHealth -= amount;
        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Kills the enemy immediately: health drops to zero, the kill is announced on
    /// <see cref="EventBus.EnemyKilled"/>, then the object is deactivated and either
    /// returned to its pool or destroyed.
    ///
    /// Deactivation raises <see cref="OnDisable"/>, which queues the removal from the
    /// spatial grid; the grid itself is only mutated at the end of the frame. So a
    /// corpse stays registered until then and is kept out of query results by
    /// <see cref="IsAlive"/> filtering in <see cref="EnemyManager.TryGetNearest"/>.
    ///
    /// The announcement comes before deactivation so a handler (drops, score, death
    /// VFX) can still read the enemy - its position and its data - while the fact
    /// is fresh. Handlers must not rely on the object surviving the call.
    /// </summary>
    public void Die()
    {
        if (_isDying)
        {
            return;
        }

        _isDying = true;
        CurrentHealth = 0;

        EventBus.RaiseEnemyKilled(transform.position, _data);

        gameObject.SetActive(false);
        Retire();
    }

    /// <summary>
    /// Takes the enemy off the field without announcing a death. The stage
    /// switch uses this: a stage being cleared is not a wave of kills, and
    /// sending it through <see cref="Die"/> would leak score, drops and
    /// experience out of a transition.
    ///
    /// The pooled path is the same as a death's - deactivate and go home - but
    /// without the announcement or the dying-guard being locked. A pooled enemy
    /// re-enters play through <see cref="OnEnable"/>, which resets both flags.
    /// </summary>
    public void ForceDespawn()
    {
        if (_isDying)
        {
            return;
        }

        _isDying = true;
        CurrentHealth = 0;
        gameObject.SetActive(false);
        Retire();
    }

    /// <summary>
    /// Ends the enemy's life: back to its pool when it came from one, otherwise
    /// destroyed.
    ///
    /// The pool is what tells the two cases apart, and an enemy that was never pooled
    /// has no pool to return to - which is why unpooled use keeps the old
    /// destroy-on-death behaviour without a flag to choose between them.
    /// </summary>
    private void Retire()
    {
        if (_pool != null)
        {
            _pool.Release(this);
            return;
        }

        Destroy(gameObject);
    }
}
