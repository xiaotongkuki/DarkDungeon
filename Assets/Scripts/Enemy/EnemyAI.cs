using UnityEngine;

/// <summary>
/// Straight-line chase: every physics step the enemy walks toward the player and
/// keeps the spatial grid aware of the cell it ended up in.
///
/// There is deliberately no pathfinding and no obstacle avoidance — enemies pass
/// through walls and through each other, which is the standard shape for this
/// genre and keeps hundreds of them cheap. Movement goes through
/// <see cref="Rigidbody2D.MovePosition"/> rather than writing the transform,
/// because auto-sync transforms is disabled in this project and the physics world
/// must still see the current position.
///
/// The body is expected to be kinematic; the collider is expected to be a trigger
/// so enemies overlap the player without shoving it around.
///
/// Speed and its spread come from the prefab's <see cref="EnemyData"/>; only the
/// stopping distance stays here, because it is a geometric constraint against the
/// prefab's own collider rather than a balance number.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Enemy))]
public class EnemyAI : MonoBehaviour
{
    [Header("Chase")]
    [Tooltip("Distance in units at which the enemy stops closing in. Must stay below the sum of the player " +
             "and enemy collider radii, otherwise contact damage can never trigger.")]
    [SerializeField] private float _stopDistance = 0.2f;

    private Rigidbody2D _rb;
    private Enemy _enemy;
    private Transform _target;
    private float _speedScale = 1f;
    private bool _missingTargetLogged;

    /// <summary>
    /// Caches the body and the enemy, and enforces the body setup this component
    /// depends on: gravity has no meaning in a top-down world and a top-down
    /// sprite must never spin.
    ///
    /// Also rejects an enemy with no data asset, for the same reason
    /// <see cref="Enemy.Awake"/> does: a component disabled during its own Awake
    /// never receives OnEnable, so a broken prefab is inert and loud instead of
    /// chasing at some invented default speed.
    /// </summary>
    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _enemy = GetComponent<Enemy>();

        _rb.gravityScale = 0f;
        _rb.freezeRotation = true;

        if (_enemy == null || _enemy.Data == null)
        {
            Debug.LogError($"EnemyAI on '{name}' found no EnemyData to read a chase speed from and will not move.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Rolls this enemy's personal speed offset. Done on enable rather than awake
    /// so a pooled enemy gets a fresh roll each time it is reused.
    ///
    /// The missing-target warning latch is cleared here too, so a reused enemy that
    /// finds itself in a scene without a player reports it again instead of staying
    /// silent because its previous life already said so.
    /// </summary>
    private void OnEnable()
    {
        _missingTargetLogged = false;

        if (_enemy == null || _enemy.Data == null)
        {
            return;
        }

        _speedScale = 1f + Random.Range(-_enemy.Data.SpeedVariance, _enemy.Data.SpeedVariance);
    }

    /// <summary>
    /// Teleports the enemy to a position while keeping interpolation honest.
    ///
    /// Interpolation renders the Transform by sliding it from the body's previous
    /// pose to its current one. A body created by Instantiate starts with a bogus
    /// previous pose - the world origin - so simply appearing at a spawn point far
    /// away makes the first physics step draw the sprite sliding in from the
    /// origin, where the player starts: a flicker across the whole screen. Unity
    /// acknowledges the underlying reset bug (Case 1367721).
    ///
    /// The documented cure is to switch interpolation off, apply the pose, then
    /// switch it back on: the real position change in the middle is what makes the
    /// interpolator drop its stale sample. Toggling the mode without a change in
    /// between does nothing, which is why the spawner creates enemies at the
    /// origin and places them here.
    ///
    /// A pooled enemy needs this just as much, and for the same reason: its stale
    /// sample is the pose it died in rather than the origin, and the cure is
    /// identical, so reuse goes through this one path instead of a second one.
    /// </summary>
    /// <param name="position">World position to place the enemy at.</param>
    public void PlaceAt(Vector2 position)
    {
        RigidbodyInterpolation2D interpolation = _rb.interpolation;

        _rb.interpolation = RigidbodyInterpolation2D.None;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
        _rb.position = position;
        _rb.interpolation = interpolation;
    }

    /// <summary>
    /// Chases for one physics step and then tells the registry where the enemy
    /// ended up.
    ///
    /// The target is resolved lazily instead of being cached in Awake: component
    /// wake order is not guaranteed, so a locator that does not exist yet would be
    /// cached as null forever (the same trap that once made the spawner produce
    /// nothing).
    /// </summary>
    private void FixedUpdate()
    {
        if (!_enemy.IsAlive)
        {
            return;
        }

        if (_target == null)
        {
            ResolveTarget();
            if (_target == null)
            {
                return;
            }
        }

        Vector2 position = _rb.position;
        Vector2 next = ChaseSteering.Step(position, _target.position, _enemy.Data.MoveSpeed * _speedScale,
            _stopDistance, Time.fixedDeltaTime);

        if (next == position)
        {
            // Already inside the stopping ring, so the cell cannot have changed
            // either and the grid needs no update.
            return;
        }

        _rb.MovePosition(next);

        // Re-bucketing is a no-op unless the enemy actually crossed a cell.
        EnemyManager.Instance?.RefreshPosition(_enemy);
    }

    /// <summary>
    /// Looks the player up through <see cref="PlayerLocator"/> and warns once when
    /// it is missing, so a scene without a player is diagnosable without spamming
    /// the console every physics step.
    /// </summary>
    private void ResolveTarget()
    {
        PlayerLocator locator = PlayerLocator.Instance;
        if (locator != null)
        {
            _target = locator.Target;
            return;
        }

        if (!_missingTargetLogged)
        {
            _missingTargetLogged = true;
            Debug.LogWarning($"Enemy '{name}' found no PlayerLocator and will not move.", this);
        }
    }
}
