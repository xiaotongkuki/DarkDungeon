using UnityEngine;

/// <summary>
/// Balance values for one enemy type, stored as a project asset.
///
/// Designers retune an enemy by editing this asset, and a new enemy type is a new
/// asset plus a prefab that points at it - no code change and no recompile. The
/// values live here rather than on the prefab so that one type's numbers can be
/// diffed, duplicated and reviewed as a single file.
///
/// Assets are shared, so components must treat them as read-only at runtime:
/// anything that changes per enemy (a speed roll, current health) belongs on the
/// component, never written back here.
/// </summary>
[CreateAssetMenu(fileName = "EnemyData", menuName = "My project/Data/Enemy")]
public class EnemyData : ScriptableObject
{
    [Header("Health")]
    [Tooltip("Hits the enemy survives before dying.")]
    [SerializeField] private int _maxHealth = 3;

    [Header("Movement")]
    [Tooltip("Chase speed in units per second. The player tops out at 8, so enemies must stay well below.")]
    [SerializeField] private float _moveSpeed = 1.8f;
    [Tooltip("Per-enemy speed spread as a fraction of move speed (+/-), so a wave does not move as one block.")]
    [SerializeField] private float _speedVariance = 0.25f;

    [Header("Contact")]
    [Tooltip("Hits dealt to a damageable target on contact.")]
    [SerializeField] private int _contactDamage = 1;

    [Header("Reward")]
    [Tooltip("Experience the dropped gem is worth. A tougher enemy should pay more than a frail one.")]
    [SerializeField] private int _xpValue = 1;

    /// <summary>Hits the enemy survives before dying.</summary>
    public int MaxHealth => _maxHealth;

    /// <summary>Chase speed in units per second, before the per-enemy variance roll.</summary>
    public float MoveSpeed => _moveSpeed;

    /// <summary>Per-enemy speed spread as a fraction of move speed (+/-); 0 makes a wave move as one block.</summary>
    public float SpeedVariance => _speedVariance;

    /// <summary>Hits dealt to a damageable target on contact.</summary>
    public int ContactDamage => _contactDamage;

    /// <summary>Experience the gem dropped by this enemy is worth.</summary>
    public int XpValue => _xpValue;
}
