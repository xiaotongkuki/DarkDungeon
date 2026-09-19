using System;
using UnityEngine;

/// <summary>
/// Frame-rate independent periodic timer: accumulates real time and reports the
/// frame on which a whole interval has elapsed.
///
/// Used by systems that must act on a fixed cadence (firing, spawning) without
/// tying that cadence to the render rate. Surplus time is discarded when the
/// interval completes, so a long frame can never queue a burst of actions.
/// </summary>
public sealed class IntervalTimer
{
    /// <summary>Smallest interval the timer accepts, to keep the comparison meaningful.</summary>
    private const float MinInterval = 0.0001f;

    private float _interval;
    private float _elapsed;

    /// <summary>
    /// Creates a timer for the given cadence.
    /// </summary>
    /// <param name="interval">Seconds between firings; must be positive.</param>
    public IntervalTimer(float interval)
    {
        if (interval <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be positive.");
        }

        _interval = interval;
    }

    /// <summary>Seconds between firings. Changing it takes effect from the next cycle.</summary>
    public float Interval
    {
        get => _interval;
        set => _interval = Mathf.Max(MinInterval, value);
    }

    /// <summary>
    /// Advances the timer by one frame's worth of time.
    /// </summary>
    /// <param name="deltaTime">Seconds since the previous call; zero or negative is ignored.</param>
    /// <returns>True on the frame an interval completes, at most once per call.</returns>
    public bool Advance(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return false;
        }

        _elapsed += deltaTime;
        if (_elapsed < _interval)
        {
            return false;
        }

        // Drop the surplus rather than carrying it: otherwise one long frame would
        // let the following frames fire several times in a row.
        _elapsed = 0f;
        return true;
    }

    /// <summary>
    /// Discards accumulated time, e.g. after a manual action or on respawn.
    /// </summary>
    public void Reset()
    {
        _elapsed = 0f;
    }
}
