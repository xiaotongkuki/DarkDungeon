using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fixed-capacity ring buffer of timestamped movement intents, plus a queue
/// for discrete press events. It decouples input sampling (render frames,
/// variable rate) from input consumption (fixed physics steps): intents stay
/// readable for a short window after being sampled, so a press that lands
/// between physics steps is never dropped — this is what makes controls feel
/// responsive even under frame spikes.
/// </summary>
public class InputBuffer
{
    // ---- Continuous intents (movement) ----
    private readonly Vector2[] _intents;
    private readonly double[] _timestamps;
    private readonly double _windowSeconds;
    private int _head;
    private int _count;

    // ---- Discrete events (reserved for jump/attack style presses) ----
    private readonly Queue<DiscreteEvent> _events = new Queue<DiscreteEvent>();

    /// <summary>
    /// A single discrete press with its sample time; used by the event queue
    /// so window expiry can be checked at dequeue time.
    /// </summary>
    private readonly struct DiscreteEvent
    {
        public readonly Vector2 Direction;
        public readonly double Timestamp;

        /// <summary>
        /// Stores one press. Kept as a struct so enqueue/dequeue stays
        /// allocation-free on the gameplay hot path.
        /// </summary>
        public DiscreteEvent(Vector2 direction, double timestamp)
        {
            Direction = direction;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Creates a buffer whose samples expire after the given window.
    /// </summary>
    /// <param name="capacity">
    /// Maximum number of retained intent samples; older ones are overwritten.
    /// Must be positive.
    /// </param>
    /// <param name="windowSeconds">
    /// How long a sampled intent stays readable, in seconds. 120 ms covers
    /// the gap between a render-frame press and the next physics step.
    /// </param>
    public InputBuffer(int capacity, float windowSeconds)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }
        _intents = new Vector2[capacity];
        _timestamps = new double[capacity];
        _windowSeconds = windowSeconds;
    }

    /// <summary>
    /// Records one sampled intent with its sample time. When the ring is
    /// full, the oldest slot is overwritten so the buffer never stalls on
    /// stale data.
    /// </summary>
    /// <param name="intent">Raw move vector as sampled; magnitude may exceed 1.</param>
    /// <param name="timestamp">
    /// Sample time. Use <see cref="Time.timeAsDouble"/> so intents do not
    /// expire while the game is paused.
    /// </param>
    public void Push(Vector2 intent, double timestamp)
    {
        _intents[_head] = intent;
        _timestamps[_head] = timestamp;
        _head = (_head + 1) % _intents.Length;
        if (_count < _intents.Length)
        {
            _count++;
        }
    }

    /// <summary>
    /// Returns the newest intent if it is still within the buffer window.
    /// Intents older than the window are treated as "no input" so the player
    /// never keeps moving from a forgotten press. Only the newest sample is
    /// consulted: earlier samples are strictly older and can never outlive it.
    /// </summary>
    /// <param name="now">Current time, from the same clock as the pushed timestamps.</param>
    /// <returns>
    /// The newest valid intent, or <see cref="Vector2.zero"/> when the buffer
    /// is empty or the newest sample has expired.
    /// </returns>
    public Vector2 GetLatestIntent(double now)
    {
        if (_count == 0)
        {
            return Vector2.zero;
        }
        int index = _head - 1;
        if (index < 0)
        {
            index += _intents.Length;
        }
        return now - _timestamps[index] <= _windowSeconds ? _intents[index] : Vector2.zero;
    }

    /// <summary>
    /// Queues a discrete press (jump/attack style) with its timestamp.
    /// Reserved for future use; movement only reads continuous intents.
    /// </summary>
    /// <param name="direction">Optional direction pressed together with the button.</param>
    /// <param name="timestamp">Press time from the same clock as <see cref="Push"/>.</param>
    public void Enqueue(Vector2 direction, double timestamp)
    {
        _events.Enqueue(new DiscreteEvent(direction, timestamp));
    }

    /// <summary>
    /// Dequeues the oldest discrete event if it is still inside the window.
    /// Expired events are silently discarded so old presses never fire late
    /// ("input buffering" for discrete actions).
    /// </summary>
    /// <param name="now">Current time to compare against event timestamps.</param>
    /// <param name="direction">Direction of the dequeued press, if any.</param>
    /// <returns>True when a still-valid event was dequeued; false otherwise.</returns>
    public bool TryDequeue(double now, out Vector2 direction)
    {
        while (_events.Count > 0)
        {
            DiscreteEvent oldest = _events.Peek();
            if (now - oldest.Timestamp > _windowSeconds)
            {
                _events.Dequeue();
                continue;
            }
            _events.Dequeue();
            direction = oldest.Direction;
            return true;
        }
        direction = Vector2.zero;
        return false;
    }

    /// <summary>
    /// Drops all buffered intents and events (e.g. on respawn or scene
    /// transitions) so stale inputs cannot leak into the next context.
    /// </summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
        _events.Clear();
    }
}
