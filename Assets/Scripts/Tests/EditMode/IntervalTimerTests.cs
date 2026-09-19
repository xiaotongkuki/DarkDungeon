using NUnit.Framework;

/// <summary>
/// Pure-logic tests for the cadence timer: interval completion, frame-rate
/// independence, surplus handling and reset behaviour. No play mode or scene
/// objects are involved.
/// </summary>
public class IntervalTimerTests
{
    /// <summary>Interval used by most tests, in seconds.</summary>
    private const float Interval = 0.5f;

    [Test]
    public void Constructor_NonPositiveInterval_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntervalTimer(0f));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new IntervalTimer(-1f));
    }

    [Test]
    public void Advance_BelowInterval_ReturnsFalse()
    {
        var timer = new IntervalTimer(Interval);

        Assert.IsFalse(timer.Advance(Interval * 0.5f));
    }

    [Test]
    public void Advance_CompletingInterval_ReturnsTrueThenFalse()
    {
        var timer = new IntervalTimer(Interval);

        Assert.IsTrue(timer.Advance(Interval), "Reaching the interval must report exactly one firing.");
        Assert.IsFalse(timer.Advance(Interval * 0.5f), "The next cycle starts from zero.");
        Assert.IsTrue(timer.Advance(Interval * 0.5f), "And completes after another full interval.");
    }

    [Test]
    public void Advance_AccumulatesPartialSteps()
    {
        var timer = new IntervalTimer(Interval);

        // Nine tenths of the interval: definitely short of the cadence.
        for (int i = 0; i < 9; i++)
        {
            Assert.IsFalse(timer.Advance(Interval * 0.1f));
        }

        // The tenth tenth completes it. Float accumulation can land a hair either
        // side of the boundary, so accept the step after as well rather than
        // asserting on a knife edge.
        bool fired = timer.Advance(Interval * 0.1f) || timer.Advance(Interval * 0.1f);

        Assert.IsTrue(fired, "Accumulated partial steps must eventually complete the interval.");
    }

    [Test]
    public void Advance_IsFrameRateIndependent()
    {
        const float totalSeconds = 5.1f;
        const float coarseStep = 1f / 10f;   // 10 fps
        const float fineStep = 1f / 300f;    // 300 fps

        int coarseFirings = CountFirings(coarseStep, totalSeconds);
        int fineFirings = CountFirings(fineStep, totalSeconds);

        Assert.AreEqual(10, coarseFirings, "5.1 s at 0.5 s per firing is 10 firings.");
        Assert.AreEqual(coarseFirings, fineFirings,
            "The cadence must come from accumulated time, not from the frame count.");
    }

    [Test]
    public void Advance_LongFrame_FiresAtMostOnceAndDropsTheSurplus()
    {
        var timer = new IntervalTimer(Interval);

        Assert.IsTrue(timer.Advance(10f), "A very long frame still completes an interval.");

        // The surplus is dropped, so the next firing needs a full interval again
        // instead of arriving immediately.
        Assert.IsFalse(timer.Advance(Interval * 0.9f),
            "Surplus time must not be banked, or a stall would queue a burst.");
    }

    [Test]
    public void Advance_NonPositiveDeltaTime_DoesNotAccumulate()
    {
        var timer = new IntervalTimer(Interval);

        Assert.IsFalse(timer.Advance(0f));
        Assert.IsFalse(timer.Advance(-Interval));
        Assert.IsTrue(timer.Advance(Interval), "Only the positive step counts.");
    }

    [Test]
    public void Reset_DiscardsAccumulatedTime()
    {
        var timer = new IntervalTimer(Interval);

        timer.Advance(Interval * 0.9f);
        timer.Reset();

        Assert.IsFalse(timer.Advance(Interval * 0.5f), "Time accumulated before the reset must not count.");
        Assert.IsTrue(timer.Advance(Interval * 0.5f));
    }

    [Test]
    public void Interval_ChangedMidCycle_TakesEffectImmediately()
    {
        var timer = new IntervalTimer(Interval);

        timer.Advance(Interval * 0.5f);
        timer.Interval = Interval * 0.25f;

        Assert.IsTrue(timer.Advance(Interval * 0.25f),
            "Accumulated time is measured against the new interval.");
    }

    /// <summary>
    /// Runs a timer at a fixed step size for a fixed span and reports how often it
    /// fired.
    /// </summary>
    /// <param name="step">Seconds per advance.</param>
    /// <param name="totalSeconds">Total span to simulate.</param>
    /// <returns>Number of firings.</returns>
    private static int CountFirings(float step, float totalSeconds)
    {
        var timer = new IntervalTimer(Interval);
        int firings = 0;

        for (float elapsed = 0f; elapsed < totalSeconds; elapsed += step)
        {
            if (timer.Advance(step))
            {
                firings++;
            }
        }

        return firings;
    }
}
