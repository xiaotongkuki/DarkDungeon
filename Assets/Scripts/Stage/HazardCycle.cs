using UnityEngine;

/// <summary>
/// Which visual state a hazard is in, and which frame of its extend animation
/// that state calls for.
/// </summary>
public enum HazardPhase
{
    /// <summary>Spikes down; nothing is dangerous.</summary>
    Retracted,
    /// <summary>About to extend; the telegraph window.</summary>
    Warning,
    /// <summary>Spikes out and dangerous.</summary>
    Extended
}

/// <summary>
/// Pure timing for a rhythmic floor hazard: the phase within the cycle, how far
/// into the extend window it is, and which animation frame that maps to.
///
/// The damage window and the animation are computed from the same clock on
/// purpose. A hazard whose picture is driven by a separate animation can drift
/// out of step with the frames that actually hurt, and a spike that looks down
/// while it bites is worse than no telegraph at all. Keeping the maths here -
/// free of components and of Time - also means the rhythm can be tested.
/// </summary>
public static class HazardCycle
{
    /// <summary>Shortest cycle the maths will honour, so a zero period cannot divide by zero.</summary>
    private const float MinimumCycle = 0.01f;

    /// <summary>
    /// The phase at a moment in the cycle.
    ///
    /// The cycle ends with the extended window, and the warning window sits
    /// immediately before it, so the sequence reads as: rest, telegraph, strike.
    /// </summary>
    /// <param name="elapsed">Seconds into the cycle; wrapped if past the period.</param>
    /// <param name="period">Full cycle length in seconds.</param>
    /// <param name="extendedSeconds">Seconds the spikes are out.</param>
    /// <param name="warningSeconds">Seconds of telegraph before they come out.</param>
    /// <returns>The phase at that moment.</returns>
    public static HazardPhase PhaseAt(float elapsed, float period, float extendedSeconds, float warningSeconds)
    {
        float cycle = Mathf.Max(period, MinimumCycle);
        float t = Mathf.Repeat(elapsed, cycle);
        float extended = Mathf.Clamp(extendedSeconds, 0f, cycle);
        float extendedStart = cycle - extended;
        float warningStart = Mathf.Max(0f, extendedStart - Mathf.Max(0f, warningSeconds));

        if (t >= extendedStart)
        {
            return HazardPhase.Extended;
        }
        return t >= warningStart ? HazardPhase.Warning : HazardPhase.Retracted;
    }

    /// <summary>
    /// How far through the extended window a moment is, from 0 at the instant
    /// the spikes start coming out to 1 when they retract.
    /// </summary>
    /// <param name="elapsed">Seconds into the cycle.</param>
    /// <param name="period">Full cycle length in seconds.</param>
    /// <param name="extendedSeconds">Seconds the spikes are out.</param>
    /// <returns>Progress in 0..1; 0 when the extended window has no length.</returns>
    public static float ExtendedProgress(float elapsed, float period, float extendedSeconds)
    {
        float cycle = Mathf.Max(period, MinimumCycle);
        float extended = Mathf.Clamp(extendedSeconds, 0f, cycle);
        if (extended <= 0f)
        {
            return 0f;
        }

        float t = Mathf.Repeat(elapsed, cycle);
        float start = cycle - extended;
        return Mathf.Clamp01((t - start) / extended);
    }

    /// <summary>
    /// The animation frame for the current extend progress.
    ///
    /// The frames play over <paramref name="animationSeconds"/> and the last one
    /// is then held for the rest of the window, so a short pop-out animation can
    /// sit inside a long dangerous window without the spikes flickering back to
    /// a half-extended pose.
    /// </summary>
    /// <param name="progress">Progress through the extended window, 0..1.</param>
    /// <param name="extendedSeconds">Seconds the spikes are out.</param>
    /// <param name="animationSeconds">Seconds the frame sequence takes to play.</param>
    /// <param name="frameCount">Frames available; zero or one always yields frame 0.</param>
    /// <returns>Frame index in 0..frameCount-1.</returns>
    public static int FrameIndex(float progress, float extendedSeconds, float animationSeconds, int frameCount)
    {
        if (frameCount <= 1)
        {
            return 0;
        }

        float window = Mathf.Max(extendedSeconds, MinimumCycle);
        float animation = Mathf.Clamp(animationSeconds, MinimumCycle, window);
        float elapsedInWindow = Mathf.Clamp01(progress) * window;
        float played = Mathf.Clamp01(elapsedInWindow / animation);

        int index = Mathf.FloorToInt(played * frameCount);
        return Mathf.Clamp(index, 0, frameCount - 1);
    }
}
