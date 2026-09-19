using UnityEngine;

/// <summary>
/// A spike trap: a floor hazard that extends on a fixed rhythm and damages the
/// player on contact while extended.
///
/// Traps are readable territory, not random damage: the spike telegraphs with a
/// short warning tint before extending, so the player can dodge by rhythm the
/// same way they dodge enemies by distance. Damage goes through the same entry
/// the contact damage uses, so the invulnerability window guards it too - a
/// spike extended across several frames cannot drain the pool per-frame.
///
/// The trap does not use physics: a distance check against the player reads the
/// same truth as a trigger but with no layer setup, and there is nothing to
/// bounce a projectile off. Monsters ignore spikes in Sprint 1, so they cannot
/// be baited by pits they will walk into.
/// </summary>
public class MapHazard : MonoBehaviour
{
    [Header("Cycle")]
    [Tooltip("Seconds for a full extend/hide cycle. A period is the trap's signature: same asset, same rhythm.")]
    [SerializeField] private float _period = 2.5f;
    [Tooltip("Seconds the spike is out (and dangerous) within one cycle. Must be below the period.")]
    [SerializeField] private float _extendedSeconds = 0.8f;
    [Tooltip("Seconds before extending that the warning plays, so the extend is visible before it bites.")]
    [SerializeField] private float _warningSeconds = 0.4f;

    [Header("Art")]
    [Tooltip("Sprite shown while the spikes are down. Empty keeps whatever the renderer already shows.")]
    [SerializeField] private Sprite _idleSprite;
    [Tooltip("Sprite shown during the warning window. Empty keeps the idle sprite and tints it as the telegraph.")]
    [SerializeField] private Sprite _warningSprite;
    [Tooltip("Frames played while the spikes come out, in order; the last frame is held until they retract. "
             + "Empty falls back to tinting the renderer, which is the original look.")]
    [SerializeField] private Sprite[] _extendFrames;
    [Tooltip("Seconds the extend frames take to play. The rest of the extended window holds the last frame.")]
    [SerializeField] private float _extendAnimationSeconds = 0.2f;
    [Tooltip("Colour multiplied over the warning pose. White leaves the art untouched; the frames are expected "
             + "to carry the telegraph, so this is only for a prefab that wants an extra cue.")]
    [SerializeField] private Color _warningTint = Color.white;

    [Header("Hit")]
    [Tooltip("Contact radius in units around the spike, defining when a player counts as standing on it.")]
    [SerializeField] private float _contactRadius = 0.6f;
    [Tooltip("Hits removed per touch; repeat contact is throttled by the player's own invulnerability window.")]
    [SerializeField] private int _damage = 1;

    /// <summary>Renderer whose colour carries the warning and extension states.</summary>
    private SpriteRenderer _renderer;

    /// <summary>Player's damage entry, resolved on first contact chance and kept while the scene lives.</summary>
    private PlayerHealth _player;

    /// <summary>Random phase offset so a field of spikes does not fire as one block.</summary>
    private float _phase;

    /// <summary>
    /// Caches the renderer and randomises the cycle phase so spikes in a group
    /// do not extend in unison.
    /// </summary>
    private void Awake()
    {
        _renderer = GetComponent<SpriteRenderer>();
        _phase = Random.value * Mathf.Max(_period, 0.01f);
    }

    /// <summary>
    /// Advances the cycle, applies the visual for the current phase, and damages
    /// the player while the spikes are out.
    ///
    /// The phase and the frame both come from <see cref="HazardCycle"/> on the
    /// same clock that gates the damage, so the picture can never promise a
    /// window the hitbox does not honour.
    /// </summary>
    private void Update()
    {
        float elapsed = Time.time + _phase;
        HazardPhase phase = HazardCycle.PhaseAt(elapsed, _period, _extendedSeconds, _warningSeconds);
        ApplyVisual(phase, HazardCycle.ExtendedProgress(elapsed, _period, _extendedSeconds));

        if (phase != HazardPhase.Extended)
        {
            return;
        }

        ResolvePlayer();
        if (_player == null || !_player.IsAlive)
        {
            return;
        }

        PlayerLocator locator = PlayerLocator.Instance;
        if (locator == null || locator.Target == null)
        {
            return;
        }

        Vector2 toPlayer = locator.Target.position - transform.position;
        if (toPlayer.sqrMagnitude <= _contactRadius * _contactRadius)
        {
            _player.TakeDamage(_damage);
        }
    }

    /// <summary>
    /// Shows the sprite for the current phase.
    ///
    /// With extend frames assigned the trap is a flipbook and the art is left
    /// alone: the idle pose while down, the frames as the spikes come out, and
    /// no colour change at all - the animation is the telegraph, and tinting it
    /// would fight whatever palette the frames use. A prefab that wants an extra
    /// warning cue sets <see cref="_warningTint"/>.
    ///
    /// With no frames assigned it falls back to the original tint-only look, so
    /// a prefab that has not been given art yet still reads correctly.
    /// </summary>
    /// <param name="phase">Phase to draw.</param>
    /// <param name="progress">Progress through the extended window, 0..1.</param>
    private void ApplyVisual(HazardPhase phase, float progress)
    {
        if (_renderer == null)
        {
            return;
        }

        bool hasFrames = _extendFrames != null && _extendFrames.Length > 0;

        if (!hasFrames)
        {
            // No art to animate, so colour is the whole telegraph.
            _renderer.color = phase == HazardPhase.Extended
                ? Color.red
                : phase == HazardPhase.Warning
                    ? new Color(1f, 0.4f, 0.4f, 1f)
                    : Color.white;
            return;
        }

        if (phase == HazardPhase.Extended)
        {
            int index = HazardCycle.FrameIndex(progress, _extendedSeconds, _extendAnimationSeconds, _extendFrames.Length);
            Sprite frame = _extendFrames[index];
            if (frame != null)
            {
                _renderer.sprite = frame;
            }
            _renderer.color = Color.white;
            return;
        }

        // Down or warning: show the idle pose (or the dedicated warning sprite),
        // tinted only by whatever the prefab asked for.
        if (phase == HazardPhase.Warning && _warningSprite != null)
        {
            _renderer.sprite = _warningSprite;
            _renderer.color = _warningTint;
            return;
        }

        if (_idleSprite != null)
        {
            _renderer.sprite = _idleSprite;
        }
        _renderer.color = phase == HazardPhase.Warning ? _warningTint : Color.white;
    }

    /// <summary>
    /// Resolves the player's damage entry once per scene instead of per frame.
    /// A dead-then-revived player keeps the same component across a stage, so a
    /// single resolve is enough for the run.
    /// </summary>
    private void ResolvePlayer()
    {
        if (_player != null)
        {
            return;
        }

        PlayerLocator locator = PlayerLocator.Instance;
        if (locator != null && locator.Target != null)
        {
            locator.Target.TryGetComponent(out _player);
        }
    }
}
