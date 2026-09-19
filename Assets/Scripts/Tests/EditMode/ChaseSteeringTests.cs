using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pure-logic tests for the chase steering kernel: step length, stopping ring,
/// overshoot clamping and degenerate inputs. No play mode or scene objects are
/// involved, so these run as fast edit-mode tests.
/// </summary>
public class ChaseSteeringTests
{
    /// <summary>Speed used throughout, in units per second.</summary>
    private const float Speed = 2f;

    /// <summary>Stopping ring used throughout, in world units.</summary>
    private const float StopDistance = 0.2f;

    /// <summary>One 50 Hz physics step, in seconds.</summary>
    private const float DeltaTime = 0.02f;

    /// <summary>Float comparison tolerance for world-space distances.</summary>
    private const float Tolerance = 1e-4f;

    [Test]
    public void Step_MovesTowardTargetBySpeedTimesDeltaTime()
    {
        Vector2 next = ChaseSteering.Step(Vector2.zero, new Vector2(10f, 0f), Speed, StopDistance, DeltaTime);

        Assert.AreEqual(Speed * DeltaTime, next.x, Tolerance);
        Assert.AreEqual(0f, next.y, Tolerance);
    }

    [Test]
    public void Step_DiagonalTarget_KeepsConstantSpeed()
    {
        Vector2 start = Vector2.zero;
        Vector2 next = ChaseSteering.Step(start, new Vector2(5f, 5f), Speed, StopDistance, DeltaTime);

        // A unit diagonal advances both components by speed * dt / sqrt(2), so the
        // travelled distance is still speed * dt: no diagonal speed bonus.
        float expectedComponent = Speed * DeltaTime / Mathf.Sqrt(2f);
        Assert.AreEqual(expectedComponent, next.x, Tolerance);
        Assert.AreEqual(expectedComponent, next.y, Tolerance);
        Assert.AreEqual(Speed * DeltaTime, (next - start).magnitude, Tolerance);
    }

    [Test]
    public void Step_MovesTowardAnyQuadrant()
    {
        Vector2 target = new Vector2(-3f, -4f);
        Vector2 next = ChaseSteering.Step(Vector2.zero, target, Speed, StopDistance, DeltaTime);

        // 3-4-5 triangle: the step must be parallel to the target direction.
        Assert.Less(next.x, 0f, "Movement must be negative on x for a negative target.");
        Assert.Less(next.y, 0f, "Movement must be negative on y for a negative target.");
        Assert.AreEqual(Speed * DeltaTime, next.magnitude, Tolerance);
    }

    [Test]
    public void Step_WithinStopDistance_DoesNotMove()
    {
        Vector2 start = new Vector2(1f, 1f);
        Vector2 target = start + new Vector2(StopDistance * 0.5f, 0f);

        Assert.AreEqual(start, ChaseSteering.Step(start, target, Speed, StopDistance, DeltaTime));
    }

    [Test]
    public void Step_ExactlyOnStopDistance_DoesNotMove()
    {
        Vector2 start = Vector2.zero;
        Vector2 target = new Vector2(StopDistance, 0f);

        Assert.AreEqual(start, ChaseSteering.Step(start, target, Speed, StopDistance, DeltaTime));
    }

    [Test]
    public void Step_OversizedStep_LandsOnTheStopRing()
    {
        // A step far longer than the remaining gap must be clamped, otherwise the
        // enemy would jump past the target and jitter back and forth.
        Vector2 start = Vector2.zero;
        Vector2 target = new Vector2(StopDistance + 0.05f, 0f);

        Vector2 next = ChaseSteering.Step(start, target, speed: 100f, StopDistance, deltaTime: 1f);

        Assert.AreEqual(target.x - StopDistance, next.x, Tolerance);
        Assert.AreEqual(StopDistance, (target - next).magnitude, Tolerance);
    }

    [Test]
    public void Step_PositionCoincidesWithTarget_ProducesNoNaN()
    {
        Vector2 position = new Vector2(3f, -2f);

        Vector2 next = ChaseSteering.Step(position, position, Speed, StopDistance, DeltaTime);

        Assert.AreEqual(position, next);
        Assert.IsFalse(float.IsNaN(next.x) || float.IsNaN(next.y),
            "Coincident positions must not produce NaN.");
    }

    [Test]
    public void Step_NonPositiveSpeed_DoesNotMove()
    {
        Vector2 start = Vector2.zero;
        Vector2 target = new Vector2(10f, 0f);

        Assert.AreEqual(start, ChaseSteering.Step(start, target, 0f, StopDistance, DeltaTime));
        Assert.AreEqual(start, ChaseSteering.Step(start, target, -5f, StopDistance, DeltaTime));
    }

    [Test]
    public void Step_NonPositiveDeltaTime_DoesNotMove()
    {
        Vector2 start = Vector2.zero;
        Vector2 target = new Vector2(10f, 0f);

        Assert.AreEqual(start, ChaseSteering.Step(start, target, Speed, StopDistance, 0f));
        Assert.AreEqual(start, ChaseSteering.Step(start, target, Speed, StopDistance, -1f));
    }

    [Test]
    public void Step_ZeroStopDistance_ReachesTheTargetExactly()
    {
        Vector2 target = new Vector2(1f, 0f);

        Vector2 next = ChaseSteering.Step(Vector2.zero, target, speed: 100f, stopDistance: 0f, deltaTime: 1f);

        Assert.AreEqual(target.x, next.x, Tolerance);
        Assert.AreEqual(target.y, next.y, Tolerance);
    }

    [Test]
    public void Step_NegativeStopDistance_IsTreatedAsZero()
    {
        Vector2 target = new Vector2(1f, 0f);

        Vector2 next = ChaseSteering.Step(Vector2.zero, target, speed: 100f, stopDistance: -3f, deltaTime: 1f);

        Assert.AreEqual(target.x, next.x, Tolerance);
    }

    [Test]
    public void Step_Repeated_ConvergesOnTheStopRing()
    {
        Vector2 target = new Vector2(5f, 0f);
        Vector2 position = Vector2.zero;

        for (int i = 0; i < 500; i++)
        {
            position = ChaseSteering.Step(position, target, Speed, StopDistance, DeltaTime);
        }

        Assert.AreEqual(StopDistance, (target - position).magnitude, 0.01f,
            "Repeated steps must settle exactly on the stopping ring.");
    }
}
