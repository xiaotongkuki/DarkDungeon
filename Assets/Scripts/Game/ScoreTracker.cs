using UnityEngine;

/// <summary>
/// Turns "an enemy died" into a score, and announces the new total on
/// <see cref="EventBus.ScoreChanged"/>.
///
/// The scoring rule lives here rather than in the HUD, so the display never has to
/// know how points are earned. Today every kill is worth the same; when enemy
/// types start being worth different amounts, this is the only place that changes
/// and the HUD keeps working.
///
/// Subscribes on enable and unsubscribes on disable: the bus is static, so a
/// missing unsubscribe would leave this tracker receiving kills from a scene it no
/// longer belongs to.
/// </summary>
public class ScoreTracker : MonoBehaviour
{
    /// <summary>Points awarded per kill.</summary>
    private const int PointsPerKill = 1;

    /// <summary>Score accumulated since this component was enabled.</summary>
    public int Score { get; private set; }

    /// <summary>
    /// Starts a fresh run at zero and starts listening for kills.
    /// </summary>
    private void OnEnable()
    {
        Score = 0;
        EventBus.EnemyKilled += OnEnemyKilled;
    }

    /// <summary>
    /// Stops listening, so a disabled tracker never scores a kill.
    /// </summary>
    private void OnDisable()
    {
        EventBus.EnemyKilled -= OnEnemyKilled;
    }

    /// <summary>
    /// Adds the kill's points and announces the new total.
    /// </summary>
    /// <param name="position">Death position, unused here; drops and VFX consume it.</param>
    /// <param name="data">The dead enemy's balance asset, unused here: the score counts kills, not worth.</param>
    private void OnEnemyKilled(Vector2 position, EnemyData data)
    {
        Score += PointsPerKill;
        EventBus.RaiseScoreChanged(Score);
    }
}
