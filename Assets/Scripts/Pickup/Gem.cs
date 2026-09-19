using UnityEngine;

/// <summary>
/// An experience gem: it rests where the enemy fell until the player comes close,
/// then accelerates into them and is collected.
///
/// Movement is a plain transform write rather than a Rigidbody2D. The gem needs no
/// collision, and a body would bring an interpolator whose stale sample smears the
/// sprite across the screen on the frame it appears - the trap
/// <see cref="EnemyAI.PlaceAt"/> exists to work around. Without a body a gem can also
/// be repositioned freely when it is reused from a pool.
///
/// A gem handed out by a <see cref="ComponentPool{T}"/> goes back there once
/// collected; one created directly still destroys itself. <see cref="Spawn"/> is the
/// single arming point and assigns every per-use field rather than adjusting it, so a
/// recycled gem is indistinguishable from a fresh one.
/// </summary>
public class Gem : MonoBehaviour, IPooledComponent<Gem>
{
    [Header("Pickup")]
    [Tooltip("Base distance in units at which the gem starts flying toward the player, before upgrades.")]
    [SerializeField] private float _magnetRadius = 4.5f;
    [Tooltip("Distance in units at which the gem counts as collected.")]
    [SerializeField] private float _collectRadius = 0.5f;

    [Header("Flight")]
    [Tooltip("Acceleration in units per second squared while being pulled in.")]
    [SerializeField] private float _acceleration = 60f;
    [Tooltip("Speed cap in units per second, so a long pull cannot overshoot the player.")]
    [SerializeField] private float _maxSpeed = 20f;

    /// <summary>Experience this gem is worth; set once by the spawner.</summary>
    public int XpValue { get; private set; }

    /// <summary>
    /// Magnet radius this gem was armed with, on top of the authored base.
    ///
    /// Held apart from <see cref="_magnetRadius"/> and assigned rather than added:
    /// adding to the serialized field would work exactly once per instance and then
    /// creep upward on every reuse, because the field is the authored value and not
    /// this gem's running total.
    /// </summary>
    private float _magnetRadiusBonus;

    /// <summary>Effective magnet radius: the authored base plus this gem's bonus.</summary>
    private float MagnetRadius => _magnetRadius + _magnetRadiusBonus;

    private float _speed;
    private bool _collected;

    /// <summary>The pool that handed this gem out, or null when it was created directly.</summary>
    private ComponentPool<Gem> _pool;

    /// <summary>
    /// The pool that handed this gem out, or null when it was created directly.
    /// Assigned by <see cref="ComponentPool{T}"/>; null means the gem owns its own
    /// lifetime and destroys itself once collected.
    /// </summary>
    public ComponentPool<Gem> Pool
    {
        set => _pool = value;
    }

    /// <summary>
    /// Arms the gem with its worth and any magnet radius the player's upgrades have
    /// added.
    ///
    /// Both are frozen here rather than read live: a gem already lying on the ground
    /// should not start flying because the player just took a magnet upgrade, and
    /// freezing keeps the read out of the per-frame path.
    ///
    /// Every per-use field is assigned here rather than adjusted, so arming is
    /// idempotent: a gem that is armed twice ends up exactly as armed once.
    /// </summary>
    /// <param name="xpValue">Experience the gem is worth.</param>
    /// <param name="magnetRadiusBonus">Extra magnet radius from player upgrades; 0 for none.</param>
    public void Spawn(int xpValue, float magnetRadiusBonus)
    {
        XpValue = Mathf.Max(0, xpValue);
        _magnetRadiusBonus = Mathf.Max(0f, magnetRadiusBonus);
        _speed = 0f;
        _collected = false;
    }

    /// <summary>
    /// Pulls the gem toward the player once it is inside the magnet radius, and
    /// collects it on contact.
    ///
    /// The player is resolved every frame rather than cached, because a gem outlives
    /// the object that dropped it and a scene without a player must leave gems
    /// resting instead of throwing. The pull also stops while the player object is
    /// inactive, so gems do not pile onto a corpse after the run ends.
    /// </summary>
    private void Update()
    {
        if (_collected)
        {
            return;
        }

        PlayerLocator locator = PlayerLocator.Instance;
        if (locator == null || locator.Target == null || !locator.Target.gameObject.activeInHierarchy)
        {
            return;
        }

        Vector2 toPlayer = (Vector2)locator.Target.position - (Vector2)transform.position;
        float distance = toPlayer.magnitude;

        if (distance <= _collectRadius)
        {
            Collect();
            return;
        }

        if (distance > MagnetRadius)
        {
            // Outside the pull: the gem waits where it landed.
            return;
        }

        _speed = Mathf.Min(_speed + _acceleration * Time.deltaTime, _maxSpeed);

        // Distance is above the collect radius here, so the division is safe.
        transform.position += (Vector3)(toPlayer / distance * (_speed * Time.deltaTime));
    }

    /// <summary>
    /// Retires the gem and announces its experience. Guarded so a gem pays out
    /// exactly once even if collection is reached twice in one frame.
    /// </summary>
    private void Collect()
    {
        _collected = true;
        EventBus.RaiseGemCollected(XpValue);
        Retire();
    }

    /// <summary>
    /// Ends the gem's collect path while staying silent: no reward, no
    /// announcement. The stage switch calls this on leftover ground gems - a
    /// stage being cleared must not pay its gems out to a player standing in
    /// the next stage, and the pooled gem re-arms through <see cref="Spawn"/>
    /// the next time the pool hands it out.
    /// </summary>
    public void Discard()
    {
        if (_collected)
        {
            return;
        }

        _collected = true;
        Retire();
    }

    /// <summary>
    /// Ends the gem's life: back to its pool when it came from one, otherwise
    /// destroyed.
    ///
    /// The pool is what tells the two cases apart, and a gem that was never pooled has
    /// no pool to return to - which is why unpooled use keeps the old
    /// destroy-on-collect behaviour without a flag to choose between them.
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
