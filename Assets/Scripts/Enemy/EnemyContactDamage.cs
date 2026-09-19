using UnityEngine;

/// <summary>
/// Damages whatever damageable thing this enemy is overlapping.
///
/// The handler lives on the enemy because the enemy owns the trigger collider, and
/// a trigger message is always delivered to the object the trigger belongs to.
/// Living here also lets each enemy type carry its own contact damage, which it
/// reads from the same <see cref="EnemyData"/> asset the rest of the prefab uses.
///
/// <c>OnTriggerStay2D</c> rather than <c>OnTriggerEnter2D</c>: entering fires once,
/// so a player standing still against an enemy would only ever take a single hit.
/// Stay fires every physics step, and the target's own invulnerability window
/// decides how often that actually lands.
/// </summary>
[RequireComponent(typeof(Enemy))]
public class EnemyContactDamage : MonoBehaviour
{
    private Enemy _enemy;

    /// <summary>
    /// Caches the enemy whose data supplies the damage value, and rejects a
    /// prefab with no data asset. A component disabled during its own Awake never
    /// receives OnEnable, so a broken prefab deals nothing instead of inventing a
    /// damage value.
    /// </summary>
    private void Awake()
    {
        _enemy = GetComponent<Enemy>();

        if (_enemy == null || _enemy.Data == null)
        {
            Debug.LogError($"EnemyContactDamage on '{name}' found no EnemyData to read contact damage from " +
                           "and will not hurt anything.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Deals contact damage while overlapping a damageable object. Non-damageable
    /// objects (walls, other scenery) are simply ignored.
    /// </summary>
    /// <param name="other">The collider this enemy is touching.</param>
    private void OnTriggerStay2D(Collider2D other)
    {
        if (_enemy == null || _enemy.Data == null)
        {
            return;
        }

        IDamageable target = other.GetComponentInParent<IDamageable>();
        if (target == null)
        {
            return;
        }

        // The target decides whether the hit lands: it is the one that knows about
        // invulnerability windows and about already being dead.
        target.TakeDamage(_enemy.Data.ContactDamage);
    }
}
