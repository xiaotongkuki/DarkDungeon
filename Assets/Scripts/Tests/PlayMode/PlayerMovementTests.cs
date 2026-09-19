using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode movement tests driven by simulated keyboard input. Verifies the
/// acceleration ramp to top speed, the coast-down after release and the idle
/// state, all through the real Update->buffer->FixedUpdate pipeline.
/// Uses the serialized gameplay defaults: maxSpeed 8, accel 40, decel 60.
/// </summary>
public class PlayerMovementTests : InputTestFixture
{
    // ---- Constants mirroring the component defaults under test ----
    private const float MaxSpeed = 8f;
    private const int WarmupSteps = 30;    // 0.6 s of fixed steps: enough to reach top speed (8 / 40 = 0.2 s)
    private const int CoastSteps = 30;     // 0.6 s of fixed steps: enough to stop from top speed (8 / 60 = 0.13 s)
    private const float Tolerance = 0.1f;  // units/s: near-target tolerance for velocity assertions

    private GameObject _player;
    private Rigidbody2D _rb;
    private Keyboard _keyboard;

    [SetUp]
    public override void Setup()
    {
        base.Setup();

        // Built inactive so the data asset can be assigned before Awake runs: the
        // component rejects a missing asset, and AddComponent on a live object would
        // log that error and fail the test. The asset carries the shipped tuning
        // values the assertions below are written against.
        _player = new GameObject("Player");
        _player.SetActive(false);
        _rb = _player.AddComponent<Rigidbody2D>();
        _player.AddComponent<PlayerInputReader>();
        PlayerMovement movement = _player.AddComponent<PlayerMovement>();
        TestData.SetObjectReference(movement, "_data", TestData.CreatePlayerData());
        _player.SetActive(true);

        _keyboard = InputSystem.AddDevice<Keyboard>();
    }

    [TearDown]
    public override void TearDown()
    {
        Object.Destroy(_player);
        base.TearDown();
    }

    [UnityTest]
    public IEnumerator Move_RightHeld_AcceleratesToTopSpeed()
    {
        Press(_keyboard.dKey);

        for (int i = 0; i < WarmupSteps; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        Assert.GreaterOrEqual(_rb.velocity.x, MaxSpeed - Tolerance,
            "Holding right must accelerate to (near) top speed.");
        Assert.LessOrEqual(Mathf.Abs(_rb.velocity.y), Tolerance,
            "A single-axis press must not move the other axis.");
    }

    [UnityTest]
    public IEnumerator Move_Released_DeceleratesToZero()
    {
        Press(_keyboard.dKey);
        for (int i = 0; i < WarmupSteps; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        Release(_keyboard.dKey);

        for (int i = 0; i < CoastSteps; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        Assert.LessOrEqual(_rb.velocity.magnitude, Tolerance,
            "After release the player must coast down to a stop.");
    }

    [UnityTest]
    public IEnumerator Move_NoInput_VelocityStaysZero()
    {
        for (int i = 0; i < 5; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        Assert.AreEqual(Vector2.zero, _rb.velocity,
            "Without input the body must stay at rest.");
    }

    [UnityTest]
    public IEnumerator Move_Diagonal_IsNormalizedToTopSpeed()
    {
        Press(_keyboard.wKey);
        Press(_keyboard.dKey);

        for (int i = 0; i < WarmupSteps; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        // The Normalize processor on the Move action must cap diagonal input
        // at the same top speed as single-axis input, not sqrt(2) * max.
        Assert.LessOrEqual(_rb.velocity.magnitude, MaxSpeed + Tolerance,
            "Diagonal movement must be normalized to top speed.");
    }

    [UnityTest]
    public IEnumerator Move_Reversal_BrakesWithTurnBoost()
    {
        Press(_keyboard.dKey);
        for (int i = 0; i < WarmupSteps; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        Release(_keyboard.dKey);
        Press(_keyboard.aKey);

        // 5 fixed steps = 0.1 s. Plain deceleration (60 u/s^2) can shed only
        // 6 u/s in that time, so a sign flip proves turnBoost engaged.
        for (int i = 0; i < 5; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        Assert.Less(_rb.velocity.x, 0f,
            "Reversing must brake with the turn boost: velocity flips sign within 0.1 s.");
    }
}
