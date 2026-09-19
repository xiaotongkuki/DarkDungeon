using UnityEngine;

/// <summary>
/// Static description of the currently built arena, so systems outside the
/// arena builder can query it.
///
/// The spawner needs the bounds to keep off-screen spawns inside the walls, the
/// camera needs them so the view never shows beyond the arena, and the prop
/// scatter needs them to pick floor positions. All three would otherwise hold
/// their own drifting copies. Being merely state without behaviour, it is a
/// static class, like the event bus.
///
/// Set once when a stage is built by <see cref="ArenaBuilder"/>; unset means
/// "no arena yet", and every consumer falls back to its unbounded behaviour so
/// tests and bare scenes behave exactly as before this module existed.
/// </summary>
public static class StageBounds
{
    /// <summary>Half width and half height of the current arena's playable area.</summary>
    private static Vector2 _halfExtents;

    /// <summary>
    /// Half extents the camera is allowed to roam over. Usually the playable area
    /// grown by the wall thickness, so the view may reach the outer face of the
    /// walls: clamping to the playable area alone would park the wall exactly at
    /// the screen edge and the player would never see it.
    /// </summary>
    private static Vector2 _cameraHalfExtents;

    /// <summary>Whether an arena has been built in this session.</summary>
    public static bool HasBounds { get; private set; }

    /// <summary>Inner playable size of the current arena, in world units.</summary>
    public static Vector2 InnerSize => _halfExtents * 2f;

    /// <summary>
    /// Publishes the arena's playable extents. Call when a stage is applied.
    /// </summary>
    /// <param name="size">Inner playable size in world units.</param>
    public static void SetSize(Vector2 size)
    {
        _halfExtents = new Vector2(Mathf.Abs(size.x) * 0.5f, Mathf.Abs(size.y) * 0.5f);
        _cameraHalfExtents = _halfExtents;
        HasBounds = true;
    }

    /// <summary>
    /// Publishes the extents the camera may roam over, typically the playable
    /// area plus the wall thickness. Call after <see cref="SetSize"/>; without
    /// it the camera bounds stay equal to the playable area.
    /// </summary>
    /// <param name="size">Camera bounds size in world units.</param>
    public static void SetCameraSize(Vector2 size)
    {
        _cameraHalfExtents = new Vector2(Mathf.Abs(size.x) * 0.5f, Mathf.Abs(size.y) * 0.5f);
    }

    /// <summary>
    /// Drops the current bounds so consumers return to their unbounded behaviour.
    /// </summary>
    public static void Clear()
    {
        HasBounds = false;
        _halfExtents = Vector2.zero;
        _cameraHalfExtents = Vector2.zero;
    }

    /// <summary>
    /// Clamps a point into the camera's roaming area, keeping the given inset
    /// away from its edges. This is the camera's clamp: it is the playable area
    /// grown by the walls, so the view edge lands on the wall's outer face
    /// instead of hiding the wall just off screen.
    /// </summary>
    /// <param name="point">Point in world space.</param>
    /// <param name="inset">Units to keep away from the camera bounds' edges, per axis.</param>
    /// <returns>The clamped point.</returns>
    public static Vector2 ClampCamera(Vector2 point, Vector2 inset)
    {
        if (!HasBounds)
        {
            return point;
        }

        Vector2 limit = Vector2.Max(_cameraHalfExtents - Vector2.Max(inset, Vector2.zero), Vector2.zero);
        return new Vector2(Mathf.Clamp(point.x, -limit.x, limit.x), Mathf.Clamp(point.y, -limit.y, limit.y));
    }

    /// <summary>
    /// Clamps a point into the playable area, keeping it <paramref name="inset"/>
    /// units away from the walls on both axes (the classic working inset when
    /// the arena is square and one inset serves both dimensions). With no arena
    /// built, the point passes through untouched.
    /// </summary>
    /// <param name="point">Point in world space.</param>
    /// <param name="inset">Units to keep away from the walls, both axes.</param>
    /// <returns>The clamped point.</returns>
    public static Vector2 ClampInside(Vector2 point, float inset)
    {
        return ClampInside(point, new Vector2(inset, inset));
    }

    /// <summary>
    /// Clamps a point into the playable area with an inset per axis. This is
    /// the exact form the camera needs: a view is rarely as wide as it is
    /// tall, so x keeps the half view width while y keeps the half height -
    /// one shared inset would over-restrict the narrow axis. With no arena
    /// built, the point passes through untouched.
    /// </summary>
    /// <param name="point">Point in world space.</param>
    /// <param name="inset">Units to keep away from the walls, per axis.</param>
    /// <returns>The clamped point.</returns>
    public static Vector2 ClampInside(Vector2 point, Vector2 inset)
    {
        if (!HasBounds)
        {
            return point;
        }

        Vector2 limit = Vector2.Max(_halfExtents - Vector2.Max(inset, Vector2.zero), Vector2.zero);
        return new Vector2(Mathf.Clamp(point.x, -limit.x, limit.x), Mathf.Clamp(point.y, -limit.y, limit.y));
    }

    /// <summary>
    /// True when the point lies inside the playable area, inset excluded.
    /// </summary>
    /// <param name="point">Point in world space.</param>
    /// <param name="inset">Units counted as being beyond the playable area.</param>
    /// <returns>Whether the point is inside.</returns>
    public static bool IsInside(Vector2 point, float inset)
    {
        if (!HasBounds)
        {
            return true;
        }

        Vector2 limit = Vector2.Max(_halfExtents - Vector2.one * Mathf.Max(0f, inset), Vector2.zero);
        return Mathf.Abs(point.x) <= limit.x && Mathf.Abs(point.y) <= limit.y;
    }
}
