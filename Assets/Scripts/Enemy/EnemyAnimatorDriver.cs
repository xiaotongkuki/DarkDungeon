using UnityEngine;

/// <summary>
/// Drives a monster's Animator state machine from its own motion, exactly the
/// way <see cref="PlayerAnimatorDriver"/> drives the player: whether the body is
/// moving selects Idle vs Run, and the sign of the horizontal motion flips the
/// sprite (the 0x72 sheets author a right-facing pose).
///
/// Speed is read from the transform, not from the rigidbody: a kinematic chaser
/// moves through <c>MovePosition</c>, which never touches <c>velocity</c>, so
/// the body reports zeros while the sprite visibly walks. The frame-to-frame
/// position delta divided by render delta time produces the same "is it
/// moving" signal at a fraction of a unit per second, which is all this state
/// machine needs.
///
/// The driver also rewinds its state machine on enable, because a pooled
/// monster is reactivated rather than recreated: whatever the previous life
/// left running (a half-played run cycle) would otherwise carry over into this
/// one.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyAnimatorDriver : MonoBehaviour
{
    /// <summary>Recovered motion (units per second) above which the body counts as moving.</summary>
    private const float MovingThreshold = 0.1f;

    /// <summary>
    /// Horizontal recovered speed (units per second) at which the sprite turns
    /// around; above zero so a single physics-step jitter does not toggle the
    /// flip back and forth.
    /// </summary>
    private const float FacingEpsilonSq = 1e-5f;

    private static readonly int MovingParam = Animator.StringToHash("Moving");

    private Animator _animator;

    /// <summary>Whether the first sampled frame has seeded the previous position.</summary>
    private bool _hasLastPosition;

    /// <summary>Previous rendered position, for the per-frame delta.</summary>
    private Vector2 _lastPosition;

    /// <summary>
    /// Caches the animator and rejects a monster with none: there is nothing to
    /// drive, so the component disables itself instead of being a silent
    /// passenger.
    /// </summary>
    private void Awake()
    {
        _animator = GetComponent<Animator>();

        if (_animator == null)
        {
            Debug.LogError($"EnemyAnimatorDriver on '{name}' found no Animator and will be inert.", this);
            enabled = false;
            return;
        }
    }

    /// <summary>
    /// Rewinds to the Idle state and re-arms the position sample, both of which
    /// make reuse safe: a recycled monster starts the same way a fresh one does.
    /// </summary>
    private void OnEnable()
    {
        _hasLastPosition = false;
        _lastPosition = transform.position;

        if (_animator != null)
        {
            _animator.SetBool(MovingParam, false);
            if (_animator.runtimeAnimatorController != null)
            {
                _animator.Play("Idle", 0, 0f);
            }
        }
    }

    /// <summary>
    /// Reads the recovered per-frame motion and feeds the state machine once.
    /// </summary>
    private void Update()
    {
        if (_animator == null)
        {
            return;
        }

        Vector2 position = transform.position;
        if (!_hasLastPosition)
        {
            _hasLastPosition = true;
            _lastPosition = position;
            return;
        }

        Vector2 delta = position - _lastPosition;
        _lastPosition = position;

        float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
        bool moving = speed > MovingThreshold;
        _animator.SetBool(MovingParam, moving);

        if (delta.sqrMagnitude > FacingEpsilonSq && delta.x != 0f)
        {
            SpriteRenderer renderer = GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                renderer.flipX = delta.x < 0f;
            }
        }
    }
}
