using UnityEngine;

/// <summary>
/// Balance values for one weapon, stored as a project asset.
///
/// A weapon owns everything about the shots it fires, including how the shot
/// itself flies. That is why <see cref="Projectile"/> keeps no flight values of
/// its own: one bullet prefab can back several weapons with different speeds,
/// lifetimes and damage, and tuning a weapon never means opening a prefab.
///
/// Assets are shared, so components must treat them as read-only at runtime.
/// </summary>
[CreateAssetMenu(fileName = "WeaponData", menuName = "My project/Data/Weapon")]
public class WeaponData : ScriptableObject
{
    [Header("Firing")]
    [Tooltip("Shot prefab instantiated on every firing.")]
    [SerializeField] private Projectile _projectilePrefab;
    [Tooltip("Seconds between shots; lowering it is the attack-speed knob.")]
    [SerializeField] private float _fireInterval = 0.5f;
    [Tooltip("Targeting radius in units; enemies further away are ignored.")]
    [SerializeField] private float _range = 8f;
    [Tooltip("Hits each shot removes from whatever it strikes.")]
    [SerializeField] private int _damage = 1;

    [Header("Projectile")]
    [Tooltip("Shot travel speed in units per second.")]
    [SerializeField] private float _projectileSpeed = 12f;
    [Tooltip("Seconds before a shot reclaims itself; covers shots that hit nothing.")]
    [SerializeField] private float _projectileLifetime = 1f;
    [Tooltip("Distance in units within which an enemy counts as struck.")]
    [SerializeField] private float _projectileHitRadius = 0.35f;

    [Header("Visual")]
    [Tooltip("Scale growth per point of damage above the weapon's own damage; 0 keeps all shots the same size.")]
    [SerializeField] private float _visualScalePerDamage = 0.25f;
    [Tooltip("Upper bound for the damage-driven visual scale.")]
    [SerializeField] private float _maxVisualScale = 2.5f;

    /// <summary>Shot prefab instantiated on every firing.</summary>
    public Projectile ProjectilePrefab => _projectilePrefab;

    /// <summary>Seconds between shots; the attack-speed knob.</summary>
    public float FireInterval => _fireInterval;

    /// <summary>Targeting radius in units; enemies further away are ignored.</summary>
    public float Range => _range;

    /// <summary>Hits each shot removes from whatever it strikes.</summary>
    public int Damage => _damage;

    /// <summary>Shot travel speed in units per second.</summary>
    public float ProjectileSpeed => _projectileSpeed;

    /// <summary>Seconds before a shot reclaims itself.</summary>
    public float ProjectileLifetime => _projectileLifetime;

    /// <summary>Distance in units within which an enemy counts as struck.</summary>
    public float ProjectileHitRadius => _projectileHitRadius;

    /// <summary>
    /// Scale growth per point of damage above the weapon's own damage. 0 keeps
    /// every shot the prefab's own size; damage at the baseline is always 1x.
    /// </summary>
    public float VisualScalePerDamage => _visualScalePerDamage;

    /// <summary>Upper bound for the damage-driven visual scale.</summary>
    public float MaxVisualScale => _maxVisualScale;
}
