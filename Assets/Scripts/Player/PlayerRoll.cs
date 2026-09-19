using UnityEngine;

/// <summary>
/// A dodge roll: a short full-speed slide along the direction the body is
/// already moving (or heading), which briefly takes over from walking.
///
/// While a roll runs, <see cref="PlayerMovement"/> is disabled so its easing
/// cannot fight the roll velocity - the mover would otherwise pull the speed
/// back toward the walking cap within one fixed step and the roll would die
/// instantly. The movement component is parked and restored, not destroyed or
/// re-created, so its own tuning state is untouched.
///
/// Direction comes from the current velocity first, then the buffered intent,
/// then (if the player rolled from a standstill with no input) the sprite's
/// facing, which keeps the roll useful as a reaction tool as well as an escape.
///
/// When the movement component is already frozen by someone else (the death
/// presentation window), the roll is refused rather than fought over: whoever
/// parked the locomotion owns it until the body's next enable.
///
/// Balance numbers come from <see cref="PlayerData"/>'s roll section, and the
/// roll's screen life matches the Roll clip authored for the player animator,
/// which this component triggers in the same hardcoded family as the Hurt and
/// Death reactions driven by <see cref="PlayerAnimatorDriver"/>.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerInputReader))]
public class PlayerRoll : MonoBehaviour
{
    /// <summary>Default roll heading when the player rolls from a standstill with no intent.</summary>
    private static readonly Vector2 FacingRightDefault = Vector2.right;

    /// <summary>Floor for the roll window (seconds), so a mistuned asset cannot have a zero-length roll.</summary>
    private const float MinRollSeconds = 0.05f;

    [Header("Data")]
    [Tooltip("Balance values for the player. Required: it supplies the roll speed and duration.")]
    [SerializeField] private PlayerData _data;

    private Rigidbody2D _rb;
    private PlayerInputReader _reader;
    private PlayerMovement _movement;
    private Animator _animator;
    private PlayerHealth _health;

    private static readonly int RollTrigger = Animator.StringToHash("Roll");

    /// <summary>Absolute clock moment the running roll ends (Time.timeAsDouble).</summary>
    private double _rollEndingAt;

    /// <summary>Direction the current roll travels.</summary>
    private Vector2 _direction;

    /// <summary>Whether the movement component is currently parked by this roller.</summary>
    private bool _movementParked;

    /// <summary>True while a roll is mid-flight.</summary>
    public bool IsRolling => Time.timeAsDouble < _rollEndingAt;

    /// <summary>
    /// Caches the body, reader, mover, animator, health and data. Rejects a
    /// player with no data asset, the same way every other configurable player
    /// component does: disabled during its own Awake, so the roll can never run
    /// on invented numbers. The mover, animator and health are optional - a
    /// bare test platform simply rolls without animation or death guarding.
    /// </summary>
    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _reader = GetComponent<PlayerInputReader>();
        _movement = GetComponent<PlayerMovement>();
        _animator = GetComponent<Animator>();
        _health = GetComponent<PlayerHealth>();

        if (_data == null)
        {
            Debug.LogError($"PlayerRoll on '{name}' has no PlayerData assigned and will not roll. " +
                           "Assign a data asset to the PlayerRoll component.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Consumes a pending Shift press once per frame and starts a roll when the
    /// player is not already rolling one.
    /// </summary>
    private void Update()
    {
        if (_reader.TakeRollRequest())
        {
            TryRoll();
        }
    }

    /// <summary>
    /// Starts a roll if none is running and the player is alive (public so
    /// tests can drive it without live keyboard state).
    /// </summary>
    /// <returns>True when the roll started.</returns>
    public bool TryRoll()
    {
        if (_health != null && !_health.IsAlive)
        {
            return false;
        }

        if (IsRolling)
        {
            return false;
        }

        if (!_movement.CanRollTakeOver)
        {
            // Locomotion is frozen by someone else (death presentation); the
            // roll does not steal control back from whoever parked it.
            return false;
        }

        _direction = PickDirection();
        _rollEndingAt = Time.timeAsDouble + Mathf.Max(MinRollSeconds, _data.RollSeconds);

        _movement.enabled = false;
        _movementParked = true;

        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            _animator.SetTrigger(RollTrigger);
        }

        return true;
    }

    /// <summary>
    /// Drives the roll's velocity while it runs and hands the body back to the
    /// mover once it is over. Fixed step keeps the physics world honest, and the
    /// restore happens regardless of how the window ends.
    /// </summary>
    private void FixedUpdate()
    {
        if (!IsRolling)
        {
            EndRoll();
            return;
        }

        _rb.velocity = _direction * _data.RollSpeed;
    }

    /// <summary>
    /// Ends a roll that outlives its component (a teardown mid-roll) by
    /// restoring the walking mover, so a disabled roller never leaves the body
    /// parked without anyone driving it.
    /// </summary>
    private void OnDisable()
    {
        EndRoll();
    }

    /// <summary>
    /// Restores the walking mover and clears the latch, so a spent roll does
    /// not linger as frozen movement.
    /// </summary>
    private void EndRoll()
    {
        if (!_movementParked)
        {
            return;
        }

        if (_movement != null)
        {
            _movement.enabled = true;
        }
        _movementParked = false;
        _rollEndingAt = 0d;
    }

    /// <summary>
    /// Picks the roll direction: where the body is already going, else where
    /// the buffered intent points, else (from a standstill with no input) the
    /// sprite's facing so a stationary tap still produces a roll.
    /// </summary>
    /// <returns>Normalized roll direction.</returns>
    private Vector2 PickDirection()
    {
        if (_rb.velocity.sqrMagnitude > 0.0001f)
        {
            return _rb.velocity.normalized;
        }

        Vector2 intent = _reader.Buffer.GetLatestIntent(Time.timeAsDouble);
        if (intent.sqrMagnitude > 0.0001f)
        {
            return intent.normalized;
        }

        SpriteRenderer renderer = GetComponent<SpriteRenderer>();
        return renderer != null && renderer.flipX ? Vector2.left : FacingRightDefault;
    }
}
