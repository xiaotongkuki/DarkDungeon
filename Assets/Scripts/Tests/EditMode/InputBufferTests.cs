using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pure-logic tests for the intent buffer: window expiry, newest-sample
/// semantics, ring overwrite and the discrete-event queue. No engine play
/// mode is required, so these run as fast edit-mode tests.
/// </summary>
public class InputBufferTests
{
    /// <summary>Window used across these tests, matching the tuned gameplay default.</summary>
    private const float Window = 0.12f;

    private InputBuffer _buffer;

    [SetUp]
    public void SetUp()
    {
        _buffer = new InputBuffer(capacity: 4, windowSeconds: Window);
    }

    [Test]
    public void GetLatestIntent_EmptyBuffer_ReturnsZero()
    {
        Assert.AreEqual(Vector2.zero, _buffer.GetLatestIntent(now: 0.0));
    }

    [Test]
    public void Push_ThenGetLatestIntent_ReturnsPushedIntent()
    {
        _buffer.Push(Vector2.right, 1.0);

        Assert.AreEqual(Vector2.right, _buffer.GetLatestIntent(now: 1.05));
    }

    [Test]
    public void GetLatestIntent_AfterWindow_ReturnsZero()
    {
        _buffer.Push(Vector2.right, 1.0);

        // Just past the window the sample must read as "no input" so the
        // player never keeps moving from a forgotten press.
        Assert.AreEqual(Vector2.zero, _buffer.GetLatestIntent(now: 1.0 + Window + 0.001));
    }

    [Test]
    public void Push_Twice_GetLatestIntent_ReturnsNewestSample()
    {
        _buffer.Push(Vector2.up, 1.0);
        _buffer.Push(Vector2.right, 1.5);

        Assert.AreEqual(Vector2.right, _buffer.GetLatestIntent(now: 1.55));
    }

    [Test]
    public void Push_MoreThanCapacity_OldestOverwrittenButNewestReadable()
    {
        // Capacity is 4: push 6 samples so the ring wraps twice.
        for (int i = 0; i < 6; i++)
        {
            _buffer.Push(Vector2.up * i, timestamp: i);
        }

        Assert.AreEqual(Vector2.up * 5, _buffer.GetLatestIntent(now: 5.05));
    }

    [Test]
    public void Clear_DropsAllSamples()
    {
        _buffer.Push(Vector2.right, 1.0);
        _buffer.Clear();

        Assert.AreEqual(Vector2.zero, _buffer.GetLatestIntent(now: 1.01));
    }

    [Test]
    public void Enqueue_ThenTryDequeue_WithinWindow_SucceedsInOrder()
    {
        _buffer.Enqueue(Vector2.up, 1.0);
        _buffer.Enqueue(Vector2.right, 1.1);

        // now = 1.1 keeps BOTH events inside the window (0.1 s and 0 s old)
        // so the assertion exercises ordering, not expiry.
        bool first = _buffer.TryDequeue(now: 1.1, out Vector2 firstDirection);
        bool second = _buffer.TryDequeue(now: 1.1, out Vector2 secondDirection);

        Assert.IsTrue(first, "First event should dequeue while inside the window.");
        Assert.AreEqual(Vector2.up, firstDirection);
        Assert.IsTrue(second, "Second event should dequeue while inside the window.");
        Assert.AreEqual(Vector2.right, secondDirection);
    }

    [Test]
    public void TryDequeue_AfterWindow_DropsExpiredEvent()
    {
        _buffer.Enqueue(Vector2.up, 1.0);

        bool dequeued = _buffer.TryDequeue(now: 1.0 + Window + 0.001, out Vector2 direction);

        Assert.IsFalse(dequeued, "Expired events must not fire late.");
        Assert.AreEqual(Vector2.zero, direction);
    }

    [Test]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new InputBuffer(0, Window));
    }
}
