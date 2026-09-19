using UnityEngine;

/// <summary>
/// The exit portal of a stage: appears once the stage's quota is met and
/// triggers the stage switch the moment the player walks into it.
///
/// Placement lives outside: the stage controller positions and deactivates the
/// portal, this component only says "I am open" and "the player was here".
/// Like the spike trap it uses a distance check instead of physics triggers, so
/// no collider or layer config is needed for a walking-in test.
/// </summary>
public class StagePortal : MonoBehaviour
{
    [Tooltip("Distance in units at which the player counts as having walked into the portal.")]
    [SerializeField] private float _triggerRadius = 1f;

    /// <summary>Cached renderer for the open/close visual pulse.</summary>
    private SpriteRenderer _renderer;

    /// <summary>While armed, a player entering the radius raises the event once.</summary>
    private bool _armed;

    /// <summary>
    /// Set once the player has been outside the radius while armed. The portal
    /// appears where the player spawns, so without this latch a portal opening
    /// under their feet would fire on the same frame it appeared and skip the
    /// rest of the wave's loot.
    /// </summary>
    private bool _readyToTrigger;

    /// <summary>
    /// Caches the renderer for the visual state.
    /// </summary>
    private void Awake()
    {
        _renderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// Opens the portal: armed and visibly ready for entry. It stays inert until
    /// the player steps out of the radius and back in.
    /// </summary>
    public void Arm()
    {
        _armed = true;
        _readyToTrigger = false;
        if (_renderer != null)
        {
            _renderer.color = new Color(0.4f, 0.8f, 1f, 1f);
        }
    }

    /// <summary>
    /// Closes the portal; it stops watching and goes dormant.
    /// </summary>
    public void Disarm()
    {
        _armed = false;
        _readyToTrigger = false;
        if (_renderer != null)
        {
            _renderer.color = new Color(0.25f, 0.35f, 0.45f, 0.6f);
        }
    }

    /// <summary>
    /// Watches for the player's entry while armed and announces it once. The
    /// first frame the player is clear of the radius arms the trigger, so a
    /// portal spawned on top of them needs a deliberate walk-in.
    /// </summary>
    private void Update()
    {
        if (!_armed)
        {
            return;
        }

        PlayerLocator locator = PlayerLocator.Instance;
        if (locator == null || locator.Target == null)
        {
            return;
        }

        Vector2 toPlayer = locator.Target.position - transform.position;
        bool inside = toPlayer.sqrMagnitude <= _triggerRadius * _triggerRadius;

        if (!inside)
        {
            _readyToTrigger = true;
            return;
        }

        if (!_readyToTrigger)
        {
            return;
        }

        // Disarm before announcing: the handler often starts a transition, and
        // a second raising from the same overlap would queue a second switch.
        _armed = false;
        EventBus.RaisePortalEntered();
    }
}
