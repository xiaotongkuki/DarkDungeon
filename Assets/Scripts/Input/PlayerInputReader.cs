using UnityEngine;

/// <summary>
/// Samples the Move action once per rendered frame and pushes it into the
/// input buffer. Update-rate sampling plus FixedUpdate consumption (in
/// <see cref="PlayerMovement"/>) decouples variable frame timing from the
/// fixed physics clock, which is the core of the responsive-controls design.
/// </summary>
public class PlayerInputReader : MonoBehaviour
{
    [Header("Buffering")]
    [Tooltip("Maximum number of intent samples retained in the ring buffer.")]
    [SerializeField] private int _bufferCapacity = 8;
    [Tooltip("Seconds an intent stays valid after being sampled. 120 ms covers the render-to-fixed-step gap.")]
    [SerializeField] private float _bufferWindow = 0.12f;

    /// <summary>
    /// Buffer filled by this reader and consumed by <see cref="PlayerMovement"/>.
    /// Created in Awake so sibling consumers can rely on it from their own
    /// Awake/OnEnable.
    /// </summary>
    public InputBuffer Buffer { get; private set; }

    /// <summary>True when a Roll press is waiting to be consumed.</summary>
    private bool _rollPending;

    private PlayerControls _controls;

    /// <summary>
    /// Creates the buffer and the input action collection before any consumer
    /// wakes up; null-guards elsewhere cover the editor domain-reload edge
    /// case where serialized state exists but fields are not yet set.
    /// </summary>
    private void Awake()
    {
        Buffer = new InputBuffer(_bufferCapacity, _bufferWindow);
        _controls = new PlayerControls();
    }

    /// <summary>
    /// Enables the Gameplay action map; symmetric with OnDisable so a disabled
    /// reader never keeps the map alive (which would double-sample input).
    /// </summary>
    private void OnEnable()
    {
        if (_controls != null)
        {
            _controls.Enable();
        }
    }

    /// <summary>
    /// Disables the action map; symmetric with OnEnable.
    /// </summary>
    private void OnDisable()
    {
        if (_controls != null)
        {
            _controls.Disable();
        }
    }

    /// <summary>
    /// Disposes the action collection to release its native resources; the
    /// collection is created per-instance in Awake.
    /// </summary>
    private void OnDestroy()
    {
        if (_controls != null)
        {
            _controls.Dispose();
            _controls = null;
        }
    }

    /// <summary>
    /// Samples the current move vector into the buffer every frame, and latches
    /// the Roll button. Zero vectors are pushed too: they expire naturally
    /// through the window and let GetLatestIntent fall back to "no input" once
    /// the player lets go.
    /// </summary>
    private void Update()
    {
        Buffer.Push(_controls.Gameplay.Move.ReadValue<Vector2>(), Time.timeAsDouble);

        if (_controls.Gameplay.Roll.WasPressedThisFrame())
        {
            _rollPending = true;
        }
    }

    /// <summary>
    /// Consumes the pending Roll press. One press maps to exactly one roll even
    /// when it was sampled many frames before the roller finally looks, which is
    /// why the take clears the latch instead of polling the raw action: a press
    /// pressed before a roll ends must still be spent rather than rerolled.
    /// </summary>
    /// <returns>True if the caller should start a roll.</returns>
    public bool TakeRollRequest()
    {
        bool pending = _rollPending;
        _rollPending = false;
        return pending;
    }
}
