using UnityEngine;

/// <summary>
/// Damped 2D camera follow. Tracks a target transform on X/Y while keeping
/// its own Z fixed (the camera must stay in front of 2D content), smoothing
/// the catch-up with a critically-damped spring so the view glides instead
/// of snapping 1:1 to the player.
/// </summary>
public class CameraFollow2D : MonoBehaviour
{
    [Header("Follow")]
    [Tooltip("Transform to track; usually the player.")]
    [SerializeField] private Transform _target;
    [Tooltip("Approximate seconds to catch up to the target; 0 = hard snap.")]
    [SerializeField] private float _smoothTime = 0.18f;
    [Tooltip("Maximum catch-up speed in units per second; 0 = unlimited.")]
    [SerializeField] private float _maxSpeed = 50f;
    [Tooltip("World-space offset from the target position, for framing tweaks.")]
    [SerializeField] private Vector2 _offset = Vector2.zero;

    private Vector3 _velocity;

    /// <summary>Camera component on this object; provides the lens used to keep the view inside the arena.</summary>
    private Camera _camera;

    /// <summary>
    /// Caches the camera component once: the follow lives on the camera itself,
    /// so one Awake lookup covers the whole run.
    /// </summary>
    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    /// <summary>
    /// Runs after Update-driven movement so the camera tracks the final
    /// position for this frame; SmoothDamp integrates the damping over time
    /// and stores its own velocity between frames. When an arena is built, the
    /// view is clamped inside it so the walls are the last thing on screen.
    /// </summary>
    private void LateUpdate()
    {
        if (_target == null)
        {
            return;
        }
        Vector3 desired = new Vector3(
            _target.position.x + _offset.x,
            _target.position.y + _offset.y,
            transform.position.z);
        float maxSpeed = _maxSpeed > 0f ? _maxSpeed : float.PositiveInfinity;
        Vector3 smoothed = Vector3.SmoothDamp(transform.position, desired, ref _velocity, _smoothTime, maxSpeed);
        // Keep the camera Z untouched: SmoothDamp on a 3D vector would
        // otherwise drift the depth that 2D content depends on.

        // Clamp the smoothed position into the camera's roaming area, per axis:
        // x keeps the half view width out of the edge, y the half view height, so
        // the view's edges stay flush against both walls of the (square) arena on
        // every screen shape. The roaming area is the playable area grown by the
        // wall thickness, so the walls are visible at the screen edge rather than
        // parked just outside it. With no arena (tests, bare scenes) the follow
        // is unchanged.
        Vector2 clamped = StageBounds.ClampCamera(
            new Vector2(smoothed.x, smoothed.y), ViewHalfExtents());
        transform.position = new Vector3(clamped.x, clamped.y, transform.position.z);
    }

    /// <summary>
    /// Inset per axis the camera needs so the whole view stays inside the
    /// arena: the visible half width and half height of an orthographic view.
    /// Non-orthographic cameras yield no inset, and the follow covers whatever
    /// it already did.
    /// </summary>
    /// <returns>Half view width (x) and height (y) in world units; clamped at zero.</returns>
    private Vector2 ViewHalfExtents()
    {
        if (_camera == null || !_camera.orthographic)
        {
            return Vector2.zero;
        }

        float halfHeight = Mathf.Max(0f, _camera.orthographicSize);
        float halfWidth = halfHeight * _camera.aspect;
        return new Vector2(halfWidth, halfHeight);
    }
}
