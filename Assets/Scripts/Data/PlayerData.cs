using UnityEngine;

/// <summary>
/// Balance values for the player, stored as a project asset.
///
/// Kept separate from <see cref="EnemyData"/> and <see cref="WeaponData"/> because
/// the player is a single long-lived entity rather than a spawnable type, and its
/// numbers are retuned on a different rhythm (survivability and feel, not wave
/// difficulty).
///
/// Assets are shared, so components must treat them as read-only at runtime: a
/// respawn refills health from here, it never writes back.
/// </summary>
[CreateAssetMenu(fileName = "PlayerData", menuName = "My project/Data/Player")]
public class PlayerData : ScriptableObject
{
    [Header("Health")]
    [Tooltip("Hits the player survives before dying.")]
    [SerializeField] private int _maxHealth = 10;
    [Tooltip("Seconds of immunity after a hit, so a crowd cannot shred the player in one frame.")]
    [SerializeField] private float _invulnerabilitySeconds = 0.5f;

    [Header("Movement")]
    [Tooltip("Top speed in units per second once fully accelerated.")]
    [SerializeField] private float _maxSpeed = 8f;
    [Tooltip("Acceleration in units per second squared (0 -> top speed).")]
    [SerializeField] private float _acceleration = 40f;
    [Tooltip("Deceleration in units per second squared (top speed -> 0); higher than acceleration for a crisp stop.")]
    [SerializeField] private float _deceleration = 60f;
    [Tooltip("Brake multiplier when the intent opposes the current velocity; reversing must feel snappy.")]
    [SerializeField] private float _turnBoost = 3f;

    [Header("Roll")]
    [Tooltip("Speed in units per second while a roll is running. Above MaxSpeed so a roll overshoots walking.")]
    [SerializeField] private float _rollSpeed = 14f;
    [Tooltip("Seconds one roll lasts, matched to the Roll animation clip (6 frames around 18 fps).")]
    [SerializeField] private float _rollSeconds = 0.33f;

    /// <summary>Hits the player survives before dying.</summary>
    public int MaxHealth => _maxHealth;

    /// <summary>Seconds of immunity after a hit.</summary>
    public float InvulnerabilitySeconds => _invulnerabilitySeconds;

    /// <summary>Top speed in units per second once fully accelerated.</summary>
    public float MaxSpeed => _maxSpeed;

    /// <summary>Acceleration in units per second squared (0 -> top speed).</summary>
    public float Acceleration => _acceleration;

    /// <summary>Deceleration in units per second squared (top speed -> 0).</summary>
    public float Deceleration => _deceleration;

    /// <summary>Brake multiplier when the intent opposes the current velocity.</summary>
    public float TurnBoost => _turnBoost;

    /// <summary>Speed in units per second while a roll is running.</summary>
    public float RollSpeed => _rollSpeed;

    /// <summary>Seconds one roll lasts.</summary>
    public float RollSeconds => _rollSeconds;
}
