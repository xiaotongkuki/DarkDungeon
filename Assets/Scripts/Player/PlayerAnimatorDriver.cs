using UnityEngine;

/// <summary>
/// Drives the player's Animator state machine from movement and health state.
///
/// Two looped clips exist today (Idle, Run) and two one-shot reactions (Hurt,
/// Death), so the parameter surface is deliberately tiny: a boolean for the
/// speed state and triggers for the reactions. Facing is not an Animator
/// parameter - the sheets author one side only, so facing is expressed by
/// flipping the sprite, and this component is the single place that decides it.
///
/// The velocity is read rather than the buffered intent, because velocity also
/// contains the deceleration tail: the walk animation keeps playing while the
/// player is still visibly sliding to a stop, which reads as motion, whereas
/// switching on intent would clamp the animation to idle while the sprite
/// still moves across the screen.
///
/// Hurt fire comes from <see cref="PlayerHealth"/>'s invulnerability window:
/// crossing into invulnerability is exactly the visual moment a hit landed,
/// without needing a health event on the bus. The edge is latched so the
/// window's end does not look like a new hit. Death subscribe is wired but
/// inert until the health flow stops deactivating the body immediately, which
/// would cut the clip on the very first frame.
/// </summary>
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerAnimatorDriver : MonoBehaviour
{
    /// <summary>Speed (units per second) above which the body counts as moving.</summary>
    private const float MovingThreshold = 0.1f;

    /// <summary>
    /// Horizontal speed (units per second) at which the sprite turns around,
    /// chosen above zero so a physics-step-sized jitter does not oscillate the
    /// flip back and forth.
    /// </summary>
    private const float FacingEpsilon = 0.02f;

    private static readonly int MovingParam = Animator.StringToHash("Moving");
    private static readonly int HurtTrigger = Animator.StringToHash("Hurt");
    private static readonly int DeathTrigger = Animator.StringToHash("Death");

    private Animator _animator;
    private SpriteRenderer _renderer;
    private Rigidbody2D _rb;
    private PlayerHealth _health;

    /// <summary>
    /// Invulnerability edge latch: true when the previous frame was inside the
    /// invulnerability window. Seeded false and first evaluated without firing,
    /// since component wake order is not guaranteed and the window may already
    /// be open when this component enables (a respawn mid-invulnerability must
    /// not replay the flinch).
    /// </summary>
    private bool _wasInvulnerable;

    /// <summary>True once the first Update has read the window without firing.</summary>
    private bool _edgeArmed;

    /// <summary>
    /// Caches the animator, renderer, body and health pool. The controller must
    /// be assigned, or this component has nothing to drive: it disables itself
    /// loudly rather than silently doing nothing.
    /// </summary>
    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _renderer = GetComponent<SpriteRenderer>();
        _rb = GetComponent<Rigidbody2D>();
        _health = GetComponent<PlayerHealth>();

        if (_animator.runtimeAnimatorController == null)
        {
            Debug.LogError($"PlayerAnimatorDriver on '{name}' has no runtime animator controller and will be inert. " +
                           "Assign the Player controller to the Animator component.", this);
            enabled = false;
            return;
        }
    }

    /// <summary>
    /// Resets the hurt edge so a respawn starts quiet.
    /// </summary>
    private void OnEnable()
    {
        _wasInvulnerable = false;
        _edgeArmed = false;
        EventBus.PlayerDied += OnPlayerDied;
    }

    /// <summary>
    /// Drops the bus subscription; the bus is static and would otherwise keep
    /// firing into a driver whose scene is gone.
    /// </summary>
    private void OnDisable()
    {
        EventBus.PlayerDied -= OnPlayerDied;
    }

    /// <summary>
    /// Feeds the state machine once per frame: motion selects Idle/Run, the
    /// sign of the horizontal velocity flips the sprite, and crossing into the
    /// invulnerability window replays the Hurt clip.
    /// </summary>
    private void Update()
    {
        if (_animator == null || _rb == null)
        {
            return;
        }

        bool moving = _rb.velocity.magnitude > MovingThreshold;
        _animator.SetBool(MovingParam, moving);

        if (_renderer != null)
        {
            FlipWithVelocity();
        }

        WatchInvulnerabilityEdge();
    }

    /// <summary>
    /// Flips the sprite to face the direction of travel. The sheets author a
    /// right-facing pose, so positive horizontal motion removes the flip.
    /// </summary>
    private void FlipWithVelocity()
    {
        if (_rb.velocity.x > FacingEpsilon)
        {
            _renderer.flipX = false;
        }
        else if (_rb.velocity.x < -FacingEpsilon)
        {
            _renderer.flipX = true;
        }
    }

    /// <summary>
    /// Raises the Hurt trigger on the falling edge of "not invulnerable to
    /// invulnerable". The first evaluation arms the latch without firing so
    /// spawn order cannot produce a phantom flinch.
    /// </summary>
    private void WatchInvulnerabilityEdge()
    {
        bool nowInvulnerable = _health != null && _health.IsInvulnerable;
        if (!_edgeArmed)
        {
            _edgeArmed = true;
            _wasInvulnerable = nowInvulnerable;
            return;
        }

        if (nowInvulnerable && !_wasInvulnerable)
        {
            _animator.SetTrigger(HurtTrigger);
        }
        _wasInvulnerable = nowInvulnerable;
    }

    /// <summary>
    /// Reacts to the player's death by triggering the Death clip. The clip only
    /// reaches the screen once the death flow stops deactivating the player in
    /// the same frame the event is raised.
    /// </summary>
    private void OnPlayerDied()
    {
        if (_animator == null)
        {
            return;
        }

        _animator.SetTrigger(DeathTrigger);
    }
}
