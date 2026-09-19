using UnityEngine;

/// <summary>
/// A straight-flying shot: travels along its launch direction, damages the first
/// live enemy it reaches, and reclaims itself on impact or when its lifetime runs
/// out.
///
/// Hit detection is a spatial-grid query rather than a physics trigger, so a shot
/// needs no collider, no rigidbody and no layer setup, and it still strikes
/// whatever enemy happens to be in the way. Having no Rigidbody2D also means no
/// interpolator, so shots cannot suffer the spawn smear that kinematic bodies do -
/// and, just as usefully, a shot can be repositioned freely when it is reused from
/// a pool.
///
/// Speed, lifetime, hit radius and damage all arrive with <see cref="Launch"/>,
/// taken from the firing <see cref="WeaponData"/>. The prefab is therefore only a
/// visual shell, and one bullet look can back any number of weapons.
///
/// A shot handed out by a <see cref="ComponentPool{T}"/> returns there instead of
/// destroying itself; one created directly still destroys itself, so unpooled use
/// behaves exactly as it always did.
/// </summary>
public class Projectile : MonoBehaviour, IPooledComponent<Projectile>
{
    private Vector2 _direction = Vector2.up;
    private float _age;
    private float _speed;
    private float _lifetime;
    private float _hitRadius;
    private int _damage;

    /// <summary>
    /// True once <see cref="Launch"/> has supplied the flight values. Until then
    /// the shot has no lifetime to spend, so it must not tick - which is also what
    /// keeps a prefab used purely as an instantiate source from retiring itself.
    /// </summary>
    private bool _launched;

    /// <summary>The pool that handed this shot out, or null when it was created directly.</summary>
    private ComponentPool<Projectile> _pool;

    /// <summary>
    /// The pool that handed this shot out, or null when it was created directly.
    /// Assigned by <see cref="ComponentPool{T}"/>; null means the shot owns its own
    /// lifetime and destroys itself.
    /// </summary>
    public ComponentPool<Projectile> Pool
    {
        set => _pool = value;
    }

    /// <summary>
    /// Clears the previous flight's state before the shot is handed out again.
    ///
    /// A reused shot must not still be considered launched: the flag is what makes it
    /// tick, and it is set by <see cref="Launch"/>. The heading is deliberately left
    /// alone, because <see cref="Launch"/> keeps the current one when it is passed a
    /// degenerate direction, and zeroing it would replace that with a fixed default.
    /// </summary>
    private void OnEnable()
    {
        _launched = false;
        _age = 0f;
    }

    /// <summary>
    /// Sends the shot on its way using the weapon's own damage.
    /// </summary>
    /// <param name="direction">
    /// Travel direction. A zero vector is ignored and the shot keeps its current
    /// heading, which keeps a caller that passes a degenerate direction from
    /// producing a stationary projectile.
    /// </param>
    /// <param name="weapon">Weapon the shot came from; null discards the shot.</param>
    public void Launch(Vector2 direction, WeaponData weapon)
    {
        Launch(direction, weapon, weapon != null ? weapon.Damage : 0);
    }

    /// <summary>
    /// Sends the shot on its way with an explicit damage value.
    ///
    /// Flight values still come from the weapon; damage is passed in because only
    /// the caller knows the player's damage bonuses, and a weapon asset must stay
    /// read-only.
    ///
    /// The sprite is oriented along the flight direction: the projectile art is a
    /// right-pointing capsule, so flying up must turn it under, not have it
    /// sprawl sideways. Rotation is re-applied on every launch, so a reused shot
    /// inherits the heading of its new flight rather than the stale one from its
    /// previous life.
    /// </summary>
    /// <param name="direction">
    /// Travel direction. A zero vector is ignored and the shot keeps its current
    /// heading, which keeps a caller that passes a degenerate direction from
    /// producing a stationary projectile.
    /// </param>
    /// <param name="weapon">
    /// Weapon the shot came from. Required: a shot with no weapon has no speed or
    /// lifetime, so it retires itself instead of drifting forever.
    /// </param>
    /// <param name="damage">Hits the shot removes on impact.</param>
    public void Launch(Vector2 direction, WeaponData weapon, int damage)
    {
        if (weapon == null)
        {
            Debug.LogError($"Projectile '{name}' was launched without a WeaponData and will be discarded.", this);
            Retire();
            return;
        }

        _speed = weapon.ProjectileSpeed;
        _lifetime = weapon.ProjectileLifetime;
        _hitRadius = weapon.ProjectileHitRadius;
        _damage = Mathf.Max(0, damage);
        _age = 0f;
        _launched = true;

        // Visual scale tracks this shot's own damage relative to the weapon's
        // tuned damage (upgraded shots grow, baseline shots stay 1x). It is an
        // assignment, not an accumulation: a pooled shot relaunched with a
        // smaller damage must shrink back, so the scale is re-set on every
        // launch, before the heading block, so even a degenerate direction
        // arms it.
        float scale = 1f + Mathf.Max(0, damage - weapon.Damage) * weapon.VisualScalePerDamage;
        transform.localScale = Vector3.one * Mathf.Clamp(scale, 1f, weapon.MaxVisualScale);

        if (direction.sqrMagnitude > 0f)
        {
            _direction = direction.normalized;
            // The art points right (+x); rotate around z by the flight angle.
            transform.rotation = Quaternion.Euler(
                0f, 0f, Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg);
        }
    }

    /// <summary>
    /// Advances the shot, retires it once its lifetime is up, and resolves an
    /// impact. The grid query is radius-bounded, so a shot that hits nothing simply
    /// flies on until it expires.
    /// </summary>
    private void Update()
    {
        if (!_launched)
        {
            return;
        }

        _age += Time.deltaTime;
        if (_age >= _lifetime)
        {
            Retire();
            return;
        }

        transform.position += (Vector3)(_direction * (_speed * Time.deltaTime));

        EnemyManager manager = EnemyManager.Instance;
        if (manager == null)
        {
            return;
        }

        if (manager.TryGetNearest(transform.position, out Enemy hit, _hitRadius))
        {
            // The impact spark comes from a scene-wide spawner so this component
            // stays ignorant of who owns the effect pool; a missing spawner
            // simply skips the spark.
            HitFxSpawner.Play(transform.position);

            // Damage through the contract, not the concrete type: the grid is
            // enemy-specific today, but what a shot does on impact is not.
            IDamageable target = hit;
            target.TakeDamage(_damage);
            Retire();
        }
    }

    /// <summary>
    /// Ends the shot's life: back to its pool when it came from one, otherwise
    /// destroyed.
    ///
    /// The pool is what tells the two cases apart, and a shot that was never pooled
    /// has no pool to return to - which is why unpooled use keeps the old
    /// destroy-on-retire behaviour without a flag to choose between them.
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
