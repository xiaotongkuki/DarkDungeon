using UnityEngine;

/// <summary>
/// A three-frame impact sprite: plays where a shot hit, then retires itself.
///
/// The frames are wires of a hit-FX sheet, first burning, last gone. The effect
/// is pooled by <see cref="HitFxSpawner"/> because every bullet impact creates
/// one - reusing instances avoids a spawn spike during a fight, which is the
/// same churn the shots themselves were pooled for.
///
/// Playback is driven by <see cref="Time.timeAsDouble"/> rather than Animator
/// key times: the effect is three sprites and a countdown, and an Animator
/// asset per effect would cost more than the effect is worth. The armed start
/// time and the swap points are all assigned, never accumulated, so a recycled
/// effect is indistinguishable from a fresh one (the same rule
/// <see cref="Pickup.Gem"/> obeys: idempotent arming).
/// </summary>
public class HitFx : MonoBehaviour, IPooledComponent<HitFx>
{
    [Header("Frames")]
    [Tooltip("Impact frames, first burning brightest. Wired on the prefab by the designer.")]
    [SerializeField] private Sprite[] _frames;

    [Tooltip("Seconds the whole burn takes, across all frames.")]
    [SerializeField] private float _seconds = 0.12f;

    private SpriteRenderer _renderer;
    private double _endingAt;
    private double _nextSwapAt;
    private int _index;

    /// <summary>The pool that handed this effect out, or null when created directly.</summary>
    private ComponentPool<HitFx> _pool;

    /// <summary>
    /// The pool that handed this effect out. Assigned by
    /// <see cref="ComponentPool{T}"/>; null means the effect owns its lifetime
    /// and destroys itself when the burn is over.
    /// </summary>
    public ComponentPool<HitFx> Pool
    {
        set => _pool = value;
    }

    /// <summary>
    /// Hides the effect; arming brings it back. Started before a frame has run,
    /// so a newly created instance is parked invisible rather than flashing.
    /// </summary>
    private void Awake()
    {
        _renderer = GetComponent<SpriteRenderer>();
        if (_frames == null || _frames.Length == 0)
        {
            Debug.LogError($"HitFx on '{name}' has no frames wired and will show nothing. " +
                           "Wire the impact sheet's frames on the prefab.", this);
            enabled = false;
            return;
        }
    }

    /// <summary>
    /// Arms the effect right after the pool hands it out: invisible until
    /// <see cref="Arm"/> is called, matching how the pool's other reuse
    /// contracts work.
    /// </summary>
    private void OnEnable()
    {
        _index = -1;
        _endingAt = 0d;
        if (_renderer != null)
        {
            _renderer.enabled = false;
        }
    }

    /// <summary>
    /// Arms the effect at a position. Every field is assigned rather than
    /// adjusted, so arming twice is identical to arming once.
    /// </summary>
    /// <param name="position">World position the impact happened at.</param>
    public void Arm(Vector2 position)
    {
        if (_frames == null || _frames.Length == 0)
        {
            return;
        }

        transform.position = position;
        _index = 0;
        double now = Time.timeAsDouble;
        float frameSeconds = Mathf.Max(0.01f, _seconds) / _frames.Length;
        _endingAt = now + Mathf.Max(0.01f, _seconds);
        _nextSwapAt = now + frameSeconds;
        _renderer.sprite = _frames[0];
        _renderer.enabled = true;
    }

    /// <summary>
    /// Advances the burn and retires the effect once the frames are spent.
    /// </summary>
    private void Update()
    {
        if (_index < 0 || _renderer == null)
        {
            return;
        }

        double now = Time.timeAsDouble;
        if (now >= _endingAt)
        {
            Retire();
            return;
        }

        if (now >= _nextSwapAt && _index < _frames.Length - 1)
        {
            _index++;
            _renderer.sprite = _frames[_index];
            float frameSeconds = Mathf.Max(0.01f, _seconds) / _frames.Length;
            _nextSwapAt = now + frameSeconds;
        }
    }

    /// <summary>
    /// Ends the effect: back to its pool when it came from one, otherwise
    /// destroyed - the same split every pooled component in the project uses.
    /// </summary>
    private void Retire()
    {
        if (_pool != null)
        {
            _pool.Release(this);
            return;
        }

        _index = -1; // no pool: this instance is spent; stop its burn loop
        Destroy(gameObject);
    }
}
