using UnityEngine;

/// <summary>
/// The player's health pool and its <see cref="IDamageable"/> implementation,
/// filled from a <see cref="PlayerData"/> asset.
///
/// Contact damage arrives from every enemy the player is overlapping, so hits are
/// gated by a short invulnerability window: without it a crowd of touching enemies
/// would empty the pool within a single frame instead of over a few seconds.
///
/// Dying deactivates the player object, which stops its movement, attack and
/// further damage in one step. Re-enabling the object respawns it with a full
/// pool, because health is refilled from the data asset on enable.
///
/// When the player carries a working Animator, death is staged instead: the
/// object stays active for a short presentation window so the Death clip can
/// actually play - deactivating in the same frame as the event would cut the
/// clip before its first frame reached the screen. Gameplay is frozen for the
/// window, and the deactivation that the old path did instantly now happens at
/// the end of it. Bare objects keep the instant path, which is what keeps the
/// test-created players behaving exactly as before.
/// </summary>
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Data")]
    [Tooltip("Balance values for the player. Required: it supplies the pool size and the invulnerability window.")]
    [SerializeField] private PlayerData _data;

    [Header("Death Presentation")]
    [Tooltip("Seconds the body stays on screen after a lethal hit so the Death clip can play. " +
             "0 or negative skips the window and deactivates immediately (no-animator behaviour).")]
    [SerializeField] private float _deathPresentationSeconds = 1.45f;

    /// <summary>Hits remaining; refilled from the data asset on enable.</summary>
    public int CurrentHealth { get; private set; }

    /// <summary>True while the player still has health left.</summary>
    public bool IsAlive => CurrentHealth > 0;

    /// <summary>True while the invulnerability window from the last hit is still running.</summary>
    public bool IsInvulnerable => Time.timeAsDouble < _invulnerableUntil;

    /// <summary>Moment the current invulnerability window ends, on the same clock as <see cref="Time.timeAsDouble"/>.</summary>
    private double _invulnerableUntil;

    /// <summary>Upgrade bonuses; optional, so a player without them uses the authored pool.</summary>
    private PlayerStatModifiers _modifiers;

    /// <summary>
    /// Guards Die against running twice; set in Die, cleared on enable. Kept as a
    /// separate latch because a staged death leaves the object active and alive
    /// only by health value - a second Die during the presentation window must
    /// not stack a second deactivation coroutine.
    /// </summary>
    private bool _dying;

    /// <summary>
    /// Rejects a player with no data asset. Disabling here rather than in OnEnable
    /// is deliberate: a component disabled during its own Awake never receives
    /// OnEnable, so the player cannot spawn with an invented pool size.
    ///
    /// The upgrade bonuses are cached here too and are optional.
    /// </summary>
    private void Awake()
    {
        _modifiers = GetComponent<PlayerStatModifiers>();

        if (_data == null)
        {
            Debug.LogError($"PlayerHealth on '{name}' has no PlayerData assigned and will not take damage. " +
                           "Assign a data asset to the PlayerHealth component.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Refills the pool from the data asset plus any health upgrades and clears
    /// leftover invulnerability. Done on enable so a respawned (re-enabled) player
    /// starts clean. Also subscribes to stat changes so a health upgrade
    /// re-announces the grown pool immediately.
    /// </summary>
    private void OnEnable()
    {
        EventBus.StatsChanged += OnStatsChanged;

        if (_data == null)
        {
            return;
        }

        float maxHealth = _data.MaxHealth + (_modifiers != null ? _modifiers.MaxHealthBonus : 0f);
        CurrentHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth));
        _invulnerableUntil = 0d;
        _dying = false;
    }

    /// <summary>
    /// Drops the subscription from OnEnable; the bus is static.
    /// </summary>
    private void OnDisable()
    {
        EventBus.StatsChanged -= OnStatsChanged;
    }

    /// <summary>
    /// Re-announces the pool after an upgrade lands: a health upgrade raises
    /// <see cref="MaxHealth"/>, and the heart row must grow without waiting
    /// for the next hit.
    /// </summary>
    private void OnStatsChanged()
    {
        if (_data != null)
        {
            EventBus.RaiseHealthChanged(CurrentHealth, MaxHealth);
        }
    }

    /// <summary>
    /// Announces the filled pool once subscriptions are in place: Start runs
    /// after every module's OnEnable, so a HUD listening here always hears this
    /// first broadcast; later changes ride the same event.
    /// </summary>
    private void Start()
    {
        if (_data == null)
        {
            return;
        }

        EventBus.RaiseHealthChanged(CurrentHealth, MaxHealth);
    }

    /// <summary>
    /// Applies damage unless the player is dead or still invulnerable, then opens a
    /// fresh invulnerability window. Non-positive amounts are ignored so callers
    /// never have to guard the call.
    /// </summary>
    /// <param name="amount">Hits to remove.</param>
    public void TakeDamage(int amount)
    {
        if (!IsAlive || amount <= 0 || IsInvulnerable)
        {
            return;
        }

        CurrentHealth -= amount;
        _invulnerableUntil = Time.timeAsDouble + _data.InvulnerabilitySeconds;
        EventBus.RaiseHealthChanged(CurrentHealth, MaxHealth);

        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// The pool's full size: the authored value plus any health upgrades. The
    /// refill path computes the same thing; keeping it here gives the change
    /// announcements one definition of "maximum".
    /// </summary>
    public int MaxHealth
    {
        get
        {
            if (_data == null)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.RoundToInt(_data.MaxHealth + (_modifiers != null ? _modifiers.MaxHealthBonus : 0f)));
        }
    }

    /// <summary>
    /// Kills the player: health drops to zero and the death is announced on
    /// <see cref="EventBus.PlayerDied"/> in the same call as before, so game-over
    /// listeners observe the fact immediately.
    ///
    /// The body's exit is what changes. With a working Animator the deactivation
    /// is deferred to the end of a presentation window while gameplay (movement,
    /// attack, physics) is frozen, giving the Death animation its screen time.
    /// Without one the old instant deactivation runs, which is the path every
    /// bare-object caller takes.
    /// </summary>
    public void Die()
    {
        if (_dying)
        {
            return;
        }

        _dying = true;
        CurrentHealth = 0;
        EventBus.RaiseHealthChanged(CurrentHealth, MaxHealth);
        EventBus.RaisePlayerDied();

        if (HasDeathPresentation() && _deathPresentationSeconds > 0f)
        {
            PresentDeath();
            return;
        }

        gameObject.SetActive(false);
    }

    /// <summary>
    /// True when the object can play its own death: an Animator with an assigned
    /// controller. A bare animator (or none at all) keeps the instant path, so
    /// test-created players behave exactly as they did before staging existed.
    /// </summary>
    private bool HasDeathPresentation()
    {
        Animator animator = GetComponent<Animator>();
        return animator != null && animator.runtimeAnimatorController != null;
    }

    /// <summary>
    /// Stages the death: freezes gameplay now, deactivates later. The coroutine
    /// re-checks liveness at the end so a player externally revived during the
    /// window (health refilled without an enable cycle) keeps its body.
    /// </summary>
    private void PresentDeath()
    {
        FreezeForPresentation();
        StartCoroutine(DeactivateAfterPresentation());
    }

    /// <summary>
    /// Freezes what the presentation must not show: movement stops driving the
    /// body and the attack stops firing. Damage intake already zeroes out through
    /// <see cref="IsAlive"/>, so the pool needs no extra guarding.
    /// </summary>
    private void FreezeForPresentation()
    {
        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.enabled = false;
        }

        PlayerAutoAttack attack = GetComponent<PlayerAutoAttack>();
        if (attack != null)
        {
            attack.enabled = false;
        }

        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.velocity = Vector2.zero;
        }
    }

    /// <summary>
    /// Ends the staged death: the body leaves the scene once the window is over,
    /// unless something revived the player in the meantime.
    /// </summary>
    private System.Collections.IEnumerator DeactivateAfterPresentation()
    {
        yield return new WaitForSeconds(_deathPresentationSeconds);

        if (!IsAlive)
        {
            gameObject.SetActive(false);
        }
    }
}
