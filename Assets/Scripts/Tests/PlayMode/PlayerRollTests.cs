using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the dodge roll: the takeover of locomotion, the velocity
/// the window drives, and the hand-back to the walking mover when it is over.
///
/// The keyboard itself is not simulated; the roller exposes <see cref="PlayerRoll.TryRoll"/>
/// as the exact entry point its Update path uses, so the tests drive the same
/// call the Shift press would trigger.
/// </summary>
public class PlayerRollTests
{
    /// <summary>Roll speed and window used by these tests (units per second / seconds).</summary>
    private const float RollSpeed = 20f;
    private const float RollSeconds = 0.2f;

    private GameObject _player;
    private PlayerMovement _movement;
    private PlayerRoll _roll;
    private Rigidbody2D _rb;

    [SetUp]
    public void SetUp()
    {
        // Built inactive so the data asset can be assigned before Awake runs, the
        // same convention the other player-component tests follow.
        _player = new GameObject("Roller");
        _player.SetActive(false);

        Rigidbody2D body = _player.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.freezeRotation = true;

        _player.AddComponent<PlayerInputReader>();

        _movement = _player.AddComponent<PlayerMovement>();
        TestData.SetObjectReference(_movement, "_data", TestData.CreatePlayerData(maxHealth: 3));

        _roll = _player.AddComponent<PlayerRoll>();
        TestData.SetObjectReference(_roll, "_data", TestData.CreatePlayerData(maxHealth: 3, rollSpeed: RollSpeed, rollSeconds: RollSeconds));

        _player.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        if (_player != null)
        {
            Object.DestroyImmediate(_player);
        }
    }

    /// <summary>
    /// Yields until the given amount of game time has passed, regardless of how
    /// many frames that takes on the current machine and editor focus state.
    /// </summary>
    /// <param name="seconds">Game-time duration to wait for.</param>
    private static IEnumerator WaitForGameTime(float seconds)
    {
        float deadline = Time.time + seconds;
        while (Time.time < deadline)
        {
            yield return null;
        }
    }

    [Test]
    public void Roll_StartsAndParksTheWalkingMover()
    {
        Assert.IsTrue(_roll.TryRoll(), "A fresh roller must accept the first request.");
        Assert.IsTrue(_roll.IsRolling);
        Assert.IsFalse(_movement.enabled,
            "While rolling, the walking mover must be parked so its easing cannot cancel the roll.");
    }

    [UnityTest]
    public IEnumerator Roll_FromAStandstill_UsesTheFacingDirection()
    {
        // No velocity, no buffered intent: the standstill roll must go the way
        // the sprite faces (right by default) rather than nowhere.
        Rigidbody2D body = _player.GetComponent<Rigidbody2D>();
        Assert.IsTrue(_roll.TryRoll());

        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        Assert.Greater(body.velocity.x, 0f,
            "A standstill roll must follow the sprite's facing (right).");
        Assert.AreEqual(0f, body.velocity.y, 0.01f, "A standstill roll must not move vertically.");
    }

    [UnityTest]
    public IEnumerator RollingBody_MovesAtTheRollSpeed_ThenHandsBackControl()
    {
        Assert.IsTrue(_roll.TryRoll());

        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        float observed = _player.GetComponent<Rigidbody2D>().velocity.magnitude;
        Assert.AreEqual(RollSpeed, observed, 0.5f,
            "During the roll window the body must travel at the roll speed, not the walking cap.");

        yield return WaitForGameTime(RollSeconds + 0.2f);

        Assert.IsFalse(_roll.IsRolling, "The roll window must end on its own.");
        Assert.IsTrue(_movement.enabled,
            "Once the window is over, the walking mover must be restored.");
    }

    [UnityTest]
    public IEnumerator SecondRequestDuringTheRoll_IsRefused()
    {
        Assert.IsTrue(_roll.TryRoll());
        Assert.IsFalse(_roll.TryRoll(), "A roll in flight must not be re-triggered.");

        // A request spammed while rolling also cannot queue a second one.
        yield return WaitForGameTime(RollSeconds + 0.2f);
        Assert.IsTrue(_roll.TryRoll(), "A fresh request after the window is over must work again.");
    }

    [UnityTest]
    public IEnumerator Roll_DirectionFollowsCurrentVelocity()
    {
        // Pre-stock a rightward drift, then roll: the window must continue the
        // same heading (x positive) rather than defaulting to the facing.
        Rigidbody2D body = _player.GetComponent<Rigidbody2D>();
        body.velocity = Vector2.right * 2f;
        yield return WaitForGameTime(0.05f);

        Assert.IsTrue(_roll.TryRoll());

        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        Assert.Greater(body.velocity.x, 0f,
            "The roll must continue the direction the body was already traveling.");
    }
}
