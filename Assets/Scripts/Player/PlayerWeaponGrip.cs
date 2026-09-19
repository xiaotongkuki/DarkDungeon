using UnityEngine;

/// <summary>
/// The gun the player visibly holds: a child object that re-aims at the nearest
/// live enemy every frame and flashes its muzzle when a shot leaves the barrel.
///
/// Aiming reads the spatial grid, not the shot logic, so aiming is decoupled
/// from firing - a weapon's cadence can be slow while the barrel still tracks
/// the nearest threat smoothly. The query is the same allocation-free
/// <see cref="EnemyManager.TryGetNearest"/> call the live systems use, which is
/// why doing it per frame costs a handful of cell visits rather than anything
/// worth caching.
///
/// The gun art points right, so aiming into the left hemisphere flips the
/// sprite vertically instead of showing the gun upside-down. Rotation lives on
/// this object, so the muzzle flash and the muzzle point rotate with it for
/// free; that child transform is what lets the spawner put shots exactly at the
/// barrel mouth.
///
/// The muzzle flash reuses one child renderer rather than spawning per shot,
/// keeping fire at zero allocation: toggle visible, swap frame, hide again.
/// </summary>
public class PlayerWeaponGrip : MonoBehaviour
{
    /// <summary>Seconds the muzzle flash burns across its three frames.</summary>
    private const float FlashSeconds = 0.09f;

    /// <summary>Seconds one flash frame stays visible (three frames share the window).</summary>
    private const float FrameSeconds = FlashSeconds / 3f;

    [Header("References")]
    [Tooltip("Point at the barrel mouth where bullets spawn.")]
    [SerializeField] private Transform _muzzle;
    [Tooltip("Flash sprite shown briefly on fire. Optional: the grip still aims without one.")]
    [SerializeField] private SpriteRenderer _flash;
    [Tooltip("Flash frames, first burning brightest. Wired from the flash sheet by the designer.")]
    [SerializeField] private Sprite[] _flashFrames;

    private SpriteRenderer _renderer;

    /// <summary>The firing component sharing its range with the grip; optional for bare grips.</summary>
    private PlayerAutoAttack _attack;

    /// <summary>Clock (Time.timeAsDouble) when the flash's current window ends.</summary>
    private double _flashEndingAt;

    /// <summary>Clock moment the flash swaps to its next frame.</summary>
    private double _frameSwapsAt;

    /// <summary>Index of the frame currently being shown (0..n-1, -1 = off).</summary>
    private int _flashFrameIndex = -1;

    /// <summary>World position bullets leave from; falls back to the grip when unwired.</summary>
    public Vector2 MuzzlePosition => _muzzle != null
        ? (Vector2)_muzzle.position
        : (Vector2)transform.position;

    /// <summary>
    /// Caches the renderer and verifies the muzzle point. A missing muzzle is an
    /// error (bullets would spawn off the barrel); a missing flash is a warning
    /// only, since a grip without flash effects still aims correctly.
    /// </summary>
    private void Awake()
    {
        _renderer = GetComponent<SpriteRenderer>();
        _attack = GetComponentInParent<PlayerAutoAttack>();

        if (_renderer == null)
        {
            Debug.LogError($"PlayerWeaponGrip on '{name}' has no SpriteRenderer and will show nothing.", this);
        }

        if (_muzzle == null)
        {
            Debug.LogError($"PlayerWeaponGrip on '{name}' has no muzzle point; bullets will spawn from the grip body.", this);
        }

        if (_flash == null || _flashFrames == null || _flashFrames.Length == 0)
        {
            _flash = null; // collapse a half-wired flash into "no flash" uniformly
            _flashEndingAt = 0d;
            return;
        }

        _flash.enabled = false;
    }

    /// <summary>
    /// Turns the barrel toward the nearest live enemy once per frame, and keeps
    /// the flash's brief visibility window running while one is burning.
    /// </summary>
    private void Update()
    {
        Aim();
        UpdateFlash();
    }

    /// <summary>
    /// Starts the muzzle flash. During overlapping fires the window simply
    /// restarts, which at authored cadences (one shot per second or slower)
    /// cannot overlap anyway.
    /// </summary>
    public void PlayMuzzleFlash()
    {
        if (_flash == null || _flashFrames == null || _flashFrames.Length == 0)
        {
            return;
        }

        double now = Time.timeAsDouble;
        _flashEndingAt = now + FlashSeconds;
        _flashFrameIndex = 0;
        _frameSwapsAt = now + FrameSeconds;
        _flash.sprite = _flashFrames[0];
        _flash.enabled = true;
    }

    /// <summary>
    /// Aims at the nearest registered enemy **inside the weapon's firing ring**.
    /// Beyond the ring the barrel relaxes instead of tracking: the firing logic
    /// will not shoot at an out-of-range monster, so pointing at one would
    /// suggest a lock that the next volley will not honour.
    ///
    /// The ring comes from the firing component's weapon asset; a bare grip
    /// (tests, no shooter) falls back to an unbounded ring so its behaviour is
    /// unchanged. With no target inside the ring, the barrel settles back to
    /// the sprite's authored facing.
    /// </summary>
    private void Aim()
    {
        float aimRange = _attack != null && _attack.Weapon != null ? _attack.Weapon.Range : 0f;

        Vector2 aim;
        EnemyManager manager = EnemyManager.Instance;
        Enemy nearest;
        if (manager != null && manager.TryGetNearest(transform.position, out nearest, aimRange))
        {
            aim = (Vector2)nearest.transform.position - (Vector2)transform.position;
        }
        else
        {
            aim = Vector2.right;
        }

        float angle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        if (_renderer != null)
        {
            // A right-facing gun in the left hemisphere would present its
            // underside; flip the sprite so the grip stays on the hand side.
            _renderer.flipY = aim.x < 0f;
        }
    }

    /// <summary>
    /// Advances the flash through its frames for the burn window, then hides it.
    /// </summary>
    private void UpdateFlash()
    {
        if (_flashFrameIndex < 0)
        {
            return;
        }

        double now = Time.timeAsDouble;
        if (now >= _flashEndingAt)
        {
            _flashFrameIndex = -1;
            _flash.enabled = false;
            return;
        }

        if (now >= _frameSwapsAt && _flashFrameIndex < _flashFrames.Length - 1)
        {
            _flashFrameIndex++;
            _flash.sprite = _flashFrames[_flashFrameIndex];
            _frameSwapsAt = now + FrameSeconds;
        }
    }
}
