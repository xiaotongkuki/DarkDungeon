using NUnit.Framework;

/// <summary>
/// Covers the hazard rhythm: which phase a moment belongs to, how far into the
/// extend window it is, and which frame that maps to. The damage window and the
/// animation are derived from this one clock, so a mistake here would show up as
/// spikes that bite while they look retracted.
/// </summary>
public class HazardCycleTests
{
    private const float Period = 2.5f;
    private const float Extended = 0.8f;
    private const float Warning = 0.4f;

    [Test]
    public void PhaseAt_WalksRestWarningThenStrike()
    {
        // The cycle ends with the extended window and the warning sits just
        // before it: rest 0..1.3, warning 1.3..1.7, extended 1.7..2.5. The
        // assertions stay clear of the exact boundaries, which float maths
        // cannot pin down to the last bit.
        Assert.AreEqual(HazardPhase.Retracted, HazardCycle.PhaseAt(0f, Period, Extended, Warning));
        Assert.AreEqual(HazardPhase.Retracted, HazardCycle.PhaseAt(1.2f, Period, Extended, Warning));
        Assert.AreEqual(HazardPhase.Warning, HazardCycle.PhaseAt(1.5f, Period, Extended, Warning));
        Assert.AreEqual(HazardPhase.Extended, HazardCycle.PhaseAt(1.8f, Period, Extended, Warning));
        Assert.AreEqual(HazardPhase.Extended, HazardCycle.PhaseAt(2.4f, Period, Extended, Warning));
    }

    [Test]
    public void PhaseAt_WrapsPastThePeriod()
    {
        Assert.AreEqual(HazardCycle.PhaseAt(0.2f, Period, Extended, Warning),
            HazardCycle.PhaseAt(0.2f + Period * 3f, Period, Extended, Warning),
            "A later cycle must repeat the same phase.");
    }

    [Test]
    public void PhaseAt_NoWarningWindow_StartsStrikingRightAfterRest()
    {
        // Warning 0 collapses the telegraph: rest 0..1.7, extended 1.7..2.5.
        Assert.AreEqual(HazardPhase.Retracted, HazardCycle.PhaseAt(1.5f, Period, Extended, 0f));
        Assert.AreEqual(HazardPhase.Extended, HazardCycle.PhaseAt(1.8f, Period, Extended, 0f));
    }

    [Test]
    public void PhaseAt_ZeroPeriod_ResolvesWithoutThrowing()
    {
        // A degenerate cycle has no extended window left, so the trap reads as
        // permanently retracted rather than dividing by zero.
        Assert.AreEqual(HazardPhase.Retracted, HazardCycle.PhaseAt(0f, 0f, 0f, 0f));
        Assert.AreEqual(HazardPhase.Retracted, HazardCycle.PhaseAt(5f, 0f, 0f, 0f));
    }

    [Test]
    public void ExtendedProgress_RunsFromZeroToOneAcrossTheWindow()
    {
        Assert.AreEqual(0f, HazardCycle.ExtendedProgress(1.7f, Period, Extended), 0.0001f);
        Assert.AreEqual(0.5f, HazardCycle.ExtendedProgress(2.1f, Period, Extended), 0.0001f);
        Assert.AreEqual(0.9875f, HazardCycle.ExtendedProgress(2.49f, Period, Extended), 0.01f);
    }

    [Test]
    public void ExtendedProgress_AtTheWrap_RestartsAtZero()
    {
        // The period is exclusive: the instant the cycle restarts the spikes are
        // down again, so progress restarts rather than reporting a finished
        // window that the damage gate would not honour.
        Assert.AreEqual(0f, HazardCycle.ExtendedProgress(Period, Period, Extended), 0.0001f);
    }

    [Test]
    public void ExtendedProgress_OutsideTheWindow_IsClamped()
    {
        Assert.AreEqual(0f, HazardCycle.ExtendedProgress(0.5f, Period, Extended), "Rest clamps to zero.");
        Assert.AreEqual(0f, HazardCycle.ExtendedProgress(0f, Period, 0f), "No window means no progress.");
    }

    [Test]
    public void FrameIndex_PlaysTheSequenceThenHoldsTheLastFrame()
    {
        // Three frames over 0.2s inside a 0.8s window: they play in the first
        // quarter and the final pose is held for the rest.
        Assert.AreEqual(0, HazardCycle.FrameIndex(0f, Extended, 0.2f, 3));
        Assert.AreEqual(0, HazardCycle.FrameIndex(0.05f, Extended, 0.2f, 3));
        Assert.AreEqual(1, HazardCycle.FrameIndex(0.12f, Extended, 0.2f, 3));
        Assert.AreEqual(2, HazardCycle.FrameIndex(0.2f, Extended, 0.2f, 3));
        Assert.AreEqual(2, HazardCycle.FrameIndex(0.5f, Extended, 0.2f, 3), "The last frame is held.");
        Assert.AreEqual(2, HazardCycle.FrameIndex(1f, Extended, 0.2f, 3));
    }

    [Test]
    public void FrameIndex_NeverLeavesTheFrameRange()
    {
        for (int i = 0; i <= 20; i++)
        {
            float progress = i / 20f;
            int index = HazardCycle.FrameIndex(progress, Extended, 0.05f, 4);
            Assert.GreaterOrEqual(index, 0);
            Assert.Less(index, 4);
        }
    }

    [Test]
    public void FrameIndex_ZeroOrOneFrame_IsAlwaysFrameZero()
    {
        Assert.AreEqual(0, HazardCycle.FrameIndex(0.5f, Extended, 0.2f, 0));
        Assert.AreEqual(0, HazardCycle.FrameIndex(0.5f, Extended, 0.2f, 1));
    }

    [Test]
    public void FrameIndex_AnimationLongerThanTheWindow_StretchesToFit()
    {
        // A 2s animation inside a 0.8s window must still finish: the clamp keeps
        // the last frame reachable instead of stopping halfway.
        Assert.AreEqual(0, HazardCycle.FrameIndex(0f, Extended, 2f, 3));
        Assert.AreEqual(2, HazardCycle.FrameIndex(1f, Extended, 2f, 3));
    }
}
