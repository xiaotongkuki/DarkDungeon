using UnityEngine;

/// <summary>
/// Rigidbody2D-based top-down mover. Each fixed step it consumes the buffered
/// intent from <see cref="PlayerInputReader"/> and eases the current velocity
/// toward the target speed, producing the acceleration/deceleration feel
/// required by the movement design.
///
/// Every tuning number comes from the assigned <see cref="PlayerData"/> asset, so
/// movement feel can be retuned without touching this component.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerInputReader))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Data")]
    [Tooltip("Balance values for the player. Required: it supplies top speed, acceleration, deceleration and turn boost.")]
    [SerializeField] private PlayerData _data;

    /// <summary>Squared-magnitude threshold below which an intent counts as "no input";
    /// avoids direction flicker from floating-point noise on released keys.</summary>
    private const float IntentEpsilonSq = 1e-4f;

    /// <summary>
    /// Speed (units/s) below which the body counts as standing still; used to
    /// skip the alignment check that would otherwise divide by ~0.
    /// </summary>
    private const float RestSpeedEpsilon = 0.01f;

    /// <summary>
    /// Alignment (dot of velocity direction and intent, range -1..1) above
    /// which steering counts as "same direction" and uses plain acceleration.
    /// </summary>
    private const float TurnAlignmentThreshold = 0.3f;

    private Rigidbody2D _rb;
    private PlayerInputReader _reader;
    private PlayerStatModifiers _modifiers;

    /// <summary>
    /// Whether the roll is allowed to take locomotion over: true while this
    /// mover is the one driving the body. The death presentation window parks
    /// this component, and whoever parked it owns any takeover decisions, so a
    /// roll aimed at a freshly dead corpse is refused instead of competing.
    /// </summary>
    public bool CanRollTakeOver => enabled;

    /// <summary>
    /// Caches the physics body, the input reader and the upgrade bonuses once,
    /// instead of doing component lookups every physics step. Also enforces the
    /// top-down body setup required by this component (gravityScale 0, frozen
    /// rotation): gravity has no meaning in a top-down world and would otherwise
    /// fight the Y-axis movement, and rotating a top-down sprite breaks rendering.
    ///
    /// Finally it rejects a player with no data asset: a component disabled during
    /// its own Awake never receives OnEnable, so the player cannot move with
    /// invented tuning values. The upgrade bonuses are optional, so a player
    /// without them simply moves at the authored speed.
    /// </summary>
    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale = 0f;
        _rb.freezeRotation = true;
        _reader = GetComponent<PlayerInputReader>();
        _modifiers = GetComponent<PlayerStatModifiers>();

        if (_data == null)
        {
            Debug.LogError($"PlayerMovement on '{name}' has no PlayerData assigned and will not move. " +
                           "Assign a data asset to the PlayerMovement component.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Applies one physics step: reads the buffered intent, computes the
    /// clamped target velocity and moves the current velocity toward it. The
    /// rate comes from <see cref="ChooseRate"/>: plain acceleration while
    /// steering along the current motion, higher deceleration when coasting,
    /// and a boosted brake when reversing so direction changes feel snappy.
    ///
    /// The speed bonus is read every step rather than cached, so a movement
    /// upgrade taken mid-run applies on the next step.
    /// </summary>
    private void FixedUpdate()
    {
        Vector2 intent = _reader.Buffer.GetLatestIntent(Time.timeAsDouble);
        float maxSpeed = _data.MaxSpeed + (_modifiers != null ? _modifiers.MoveSpeedBonus : 0f);
        Vector2 target = Vector2.ClampMagnitude(intent, 1f) * maxSpeed;
        float rate = ChooseRate(intent);
        _rb.velocity = Vector2.MoveTowards(_rb.velocity, target, rate * Time.fixedDeltaTime);
    }

    /// <summary>
    /// Picks the velocity change rate (units per second squared) for this step
    /// based on how the intent relates to the current motion:
    /// no input -> decelerate; from rest or steering along the motion ->
    /// accelerate; steering against the motion -> deceleration boosted by
    /// the data asset's turn boost so the player brakes hard before
    /// re-accelerating.
    /// </summary>
    /// <param name="intent">Normalized buffered move intent of this step.</param>
    /// <returns>The rate handed to <see cref="Vector2.MoveTowards"/>.</returns>
    private float ChooseRate(Vector2 intent)
    {
        if (intent.sqrMagnitude <= IntentEpsilonSq)
        {
            return _data.Deceleration;
        }

        float speed = _rb.velocity.magnitude;
        if (speed < RestSpeedEpsilon)
        {
            return _data.Acceleration;
        }

        // Dot < threshold means the player wants to go where they are not
        // moving: brake hard first, then re-accelerate in the new direction.
        float alignment = Vector2.Dot(_rb.velocity / speed, intent);
        return alignment > TurnAlignmentThreshold ? _data.Acceleration : _data.Deceleration * _data.TurnBoost;
    }
}
