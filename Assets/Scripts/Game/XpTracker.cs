using UnityEngine;

/// <summary>
/// Turns collected gems into experience, and experience into levels.
///
/// It owns the only copy of the player's level and banked experience, so the HUD
/// only has to listen and the upgrade panel only has to react. Thresholds come from
/// a <see cref="LevelCurveData"/> asset, so pacing is a data change.
///
/// Subscribes on enable and unsubscribes on disable: the bus is static, so a
/// missing unsubscribe would leave this tracker leveling a scene it no longer
/// belongs to.
/// </summary>
public class XpTracker : MonoBehaviour
{
    [Header("Data")]
    [Tooltip("Level curve that decides how much experience each level costs.")]
    [SerializeField] private LevelCurveData _curve;

    /// <summary>Level the player is on; starts at 1.</summary>
    public int Level { get; private set; }

    /// <summary>Experience banked toward the next level.</summary>
    public int XpInLevel { get; private set; }

    /// <summary>Experience the next level needs, or 0 once the curve is exhausted.</summary>
    public int RequiredForNextLevel => _curve != null ? _curve.RequiredForNextLevel(Level) : 0;

    /// <summary>True once the player has died; the run's experience stops counting.</summary>
    private bool _frozen;

    /// <summary>
    /// Rejects a tracker with no level curve, like the project's other data-driven
    /// components: a component disabled during its own Awake never reaches OnEnable,
    /// so a misconfigured tracker is inert and loud.
    /// </summary>
    private void Awake()
    {
        if (_curve == null)
        {
            Debug.LogError($"XpTracker on '{name}' has no level curve assigned and will not level anything up.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Starts a fresh run at level 1 with nothing banked, and starts listening for
    /// gems and for the player's death.
    /// </summary>
    private void OnEnable()
    {
        if (_curve == null)
        {
            return;
        }

        Level = 1;
        XpInLevel = 0;
        _frozen = false;

        EventBus.GemCollected += OnGemCollected;
        EventBus.PlayerDied += OnPlayerDied;
    }

    /// <summary>
    /// Announces the starting state once every component has finished enabling.
    ///
    /// Deliberately in Start rather than OnEnable: a display that subscribes in its
    /// own OnEnable may not be attached yet while this component enables, and the
    /// required-experience figure cannot be guessed by the display.
    /// </summary>
    private void Start()
    {
        if (_curve == null)
        {
            return;
        }

        EventBus.RaiseXpChanged(XpInLevel, RequiredForNextLevel);
    }

    /// <summary>
    /// Drops both subscriptions; the bus is static and outlives this scene.
    /// </summary>
    private void OnDisable()
    {
        EventBus.GemCollected -= OnGemCollected;
        EventBus.PlayerDied -= OnPlayerDied;
    }

    /// <summary>
    /// Stops counting experience once the run is over, so gems still lying near the
    /// corpse cannot keep leveling a dead player up.
    /// </summary>
    private void OnPlayerDied()
    {
        _frozen = true;
    }

    /// <summary>
    /// Banks a collected gem and spends it on as many levels as it covers.
    ///
    /// A single gem can be worth more than one level, so the thresholds are crossed
    /// in a loop and each crossing is announced separately; the upgrade panel queues
    /// them and asks for one choice per level.
    /// </summary>
    /// <param name="xpAmount">Experience the collected gem was worth.</param>
    private void OnGemCollected(int xpAmount)
    {
        if (_frozen || xpAmount <= 0)
        {
            return;
        }

        XpInLevel += xpAmount;

        while (true)
        {
            int required = RequiredForNextLevel;
            if (required <= 0 || XpInLevel < required)
            {
                break;
            }

            XpInLevel -= required;
            Level++;
            EventBus.RaiseLevelUp(Level);
        }

        EventBus.RaiseXpChanged(XpInLevel, RequiredForNextLevel);
    }
}
