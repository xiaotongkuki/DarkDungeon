using System;
using UnityEngine;

/// <summary>
/// Global publish/subscribe hub for gameplay facts that several unrelated systems
/// care about.
///
/// Without it, every new reaction to "an enemy died" means another direct
/// reference from the producer to the consumer: the enemy would have to know
/// about score, drops, audio and the kill counter, and every test that spawns an
/// enemy would drag those systems along. Here the producer states what happened
/// and whoever cares listens.
///
/// Deliberately a static class rather than a scene object: the bus holds no state
/// beyond its subscriber list, so a GameObject would add a lifetime to manage for
/// nothing. The cost is that subscriptions outlive a scene, which is why every
/// subscriber must unsubscribe in OnDisable - the same symmetry the project
/// already requires for its other event wiring.
///
/// Dispatch is synchronous: handlers run inside the raise call, in subscription
/// order, so a handler must stay cheap and must not re-raise the same event.
/// </summary>
public static class EventBus
{
    /// <summary>
    /// Raised when an enemy dies, carrying the death position and the type that
    /// died.
    ///
    /// The position is what drops, score popups and death VFX need; the data asset
    /// is what lets a drop know the type's worth without keeping a reference to an
    /// object that is about to be destroyed.
    /// </summary>
    public static event Action<Vector2, EnemyData> EnemyKilled;

    /// <summary>
    /// Raised when the player dies. The game-over flow subscribes here instead of
    /// polling the health pool.
    /// </summary>
    public static event Action PlayerDied;

    /// <summary>
    /// Raised whenever the score changes, carrying the new total.
    ///
    /// The HUD subscribes to this rather than counting kills itself, so the
    /// scoring rule stays in one place and the display never has to know how
    /// points are earned.
    /// </summary>
    public static event Action<int> ScoreChanged;

    /// <summary>
    /// Raised when a gem reaches the player, carrying the experience it was worth.
    /// </summary>
    public static event Action<int> GemCollected;

    /// <summary>
    /// Raised whenever the experience inside the current level changes, carrying
    /// the experience banked so far and the amount the next level needs, so a
    /// display can show progress without reading the level curve itself.
    /// </summary>
    public static event Action<int, int> XpChanged;

    /// <summary>
    /// Raised when the player reaches a new level, carrying it. The upgrade choice
    /// panel subscribes here.
    /// </summary>
    public static event Action<int> LevelUp;

    /// <summary>
    /// Raised after an upgrade has been applied to the player's stats, so systems
    /// that copied a value at startup (the firing cadence, for one) can re-read it.
    /// </summary>
    public static event Action StatsChanged;

    /// <summary>
    /// Raised whenever the player's health pool changes, carrying the current and
    /// maximum pool, so a health display can redraw without reading the player.
    /// </summary>
    public static event Action<int, int> HealthChanged;

    /// <summary>
    /// Raised when a wave begins, carrying its one-based number. The HUD's wave
    /// label subscribes here.
    /// </summary>
    public static event Action<int> WaveStarted;

    /// <summary>
    /// Raised when every enemy a wave spawned has been killed, so the exit
    /// portal is opening. Produces the portal marker and the wave-cleared
    /// notification.
    /// </summary>
    public static event Action<int> WaveCleared;

    /// <summary>
    /// Raised the frame the player walks into an open portal. The wave
    /// controller reacts by building the next wave; it listens for this rather
    /// than polling the portal.
    /// </summary>
    public static event Action PortalEntered;

    /// <summary>
    /// Emits <see cref="WaveStarted"/>. Called by the wave controller when a
    /// wave becomes live.
    /// </summary>
    /// <param name="wave">One-based wave number.</param>
    public static void RaiseWaveStarted(int wave)
    {
        WaveStarted?.Invoke(wave);
    }

    /// <summary>
    /// Emits <see cref="WaveCleared"/>. Called by the wave controller when the
    /// last enemy of the wave dies.
    /// </summary>
    /// <param name="wave">One-based wave number that was just cleared.</param>
    public static void RaiseWaveCleared(int wave)
    {
        WaveCleared?.Invoke(wave);
    }

    /// <summary>
    /// Emits <see cref="PortalEntered"/>. Called by the portal when the player
    /// walks into it.
    /// Safe to call with no subscribers.
    /// </summary>
    public static void RaisePortalEntered()
    {
        PortalEntered?.Invoke();
    }

    /// <summary>
    /// Emits <see cref="EnemyKilled"/>. Called by <see cref="Enemy.Die"/>.
    /// Safe to call with no subscribers.
    /// </summary>
    /// <param name="position">World position the enemy died at.</param>
    /// <param name="data">Balance asset of the enemy that died; may be null on a misconfigured prefab.</param>
    public static void RaiseEnemyKilled(Vector2 position, EnemyData data)
    {
        EnemyKilled?.Invoke(position, data);
    }

    /// <summary>
    /// Emits <see cref="PlayerDied"/>. Called by <see cref="PlayerHealth.Die"/>.
    /// Safe to call with no subscribers.
    /// </summary>
    public static void RaisePlayerDied()
    {
        PlayerDied?.Invoke();
    }

    /// <summary>
    /// Emits <see cref="ScoreChanged"/>. Called by <see cref="ScoreTracker"/>.
    /// Safe to call with no subscribers.
    /// </summary>
    /// <param name="score">The player's new total score.</param>
    public static void RaiseScoreChanged(int score)
    {
        ScoreChanged?.Invoke(score);
    }

    /// <summary>
    /// Emits <see cref="GemCollected"/>. Called by <see cref="Gem"/>.
    /// Safe to call with no subscribers.
    /// </summary>
    /// <param name="xpAmount">Experience the collected gem was worth.</param>
    public static void RaiseGemCollected(int xpAmount)
    {
        GemCollected?.Invoke(xpAmount);
    }

    /// <summary>
    /// Emits <see cref="XpChanged"/>. Called by <see cref="XpTracker"/>.
    /// Safe to call with no subscribers.
    /// </summary>
    /// <param name="xpInLevel">Experience banked toward the next level.</param>
    /// <param name="requiredForNext">Experience the next level needs in total.</param>
    public static void RaiseXpChanged(int xpInLevel, int requiredForNext)
    {
        XpChanged?.Invoke(xpInLevel, requiredForNext);
    }

    /// <summary>
    /// Emits <see cref="LevelUp"/>. Called by <see cref="XpTracker"/>.
    /// Safe to call with no subscribers.
    /// </summary>
    /// <param name="level">The level the player just reached.</param>
    public static void RaiseLevelUp(int level)
    {
        LevelUp?.Invoke(level);
    }

    /// <summary>
    /// Emits <see cref="StatsChanged"/>. Called by
    /// <see cref="PlayerStatModifiers"/> after an upgrade lands.
    /// Safe to call with no subscribers.
    /// </summary>
    public static void RaiseStatsChanged()
    {
        StatsChanged?.Invoke();
    }

    /// <summary>
    /// Emits <see cref="HealthChanged"/>. Called by <see cref="PlayerHealth"/>
    /// whenever its pool is refilled, reduced or emptied.
    /// Safe to call with no subscribers.
    /// </summary>
    /// <param name="current">Hits the player has left.</param>
    /// <param name="max">Total hits the pool holds.</param>
    public static void RaiseHealthChanged(int current, int max)
    {
        HealthChanged?.Invoke(current, max);
    }
}
