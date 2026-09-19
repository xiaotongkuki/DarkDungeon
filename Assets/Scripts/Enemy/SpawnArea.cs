using UnityEngine;

/// <summary>
/// World-space rectangle covered by a camera on the gameplay plane, expressed as
/// a centre plus half-extents so callers can reason about it without repeating
/// viewport maths. A struct so queries and spawns stay allocation-free.
/// </summary>
public readonly struct ViewRect
{
    /// <summary>World-space centre of the visible rectangle.</summary>
    public readonly Vector2 Center;

    /// <summary>Half width and half height of the visible rectangle, in world units.</summary>
    public readonly Vector2 HalfExtents;

    /// <summary>
    /// Stores a view rectangle; both vectors are in world units.
    /// </summary>
    /// <param name="center">World-space centre.</param>
    /// <param name="halfExtents">Half width and half height.</param>
    public ViewRect(Vector2 center, Vector2 halfExtents)
    {
        Center = center;
        HalfExtents = halfExtents;
    }
}

/// <summary>
/// Pure helpers for placing spawns just outside what the player can see. Kept
/// free of engine state (no camera, no Random) so the placement maths is
/// deterministic and unit-testable, and so nothing allocates per spawn.
/// </summary>
public static class SpawnArea
{
    /// <summary>
    /// Projects the camera's viewport onto the gameplay plane and returns the
    /// world rectangle it covers.
    ///
    /// The z passed to ViewportToWorldPoint is the distance from the camera to
    /// that plane, not zero: passing zero returns points on the camera's own
    /// plane, which silently yields wrong world coordinates for the 2D setup
    /// (camera at z = -10, gameplay content at z = 0).
    /// </summary>
    /// <param name="camera">Camera to project; null yields an empty rectangle.</param>
    /// <param name="planeZ">World z of the gameplay plane.</param>
    /// <returns>The visible world rectangle.</returns>
    public static ViewRect GetViewRect(Camera camera, float planeZ)
    {
        if (camera == null)
        {
            return new ViewRect(Vector2.zero, Vector2.zero);
        }

        float depth = planeZ - camera.transform.position.z;
        if (depth <= 0f)
        {
            // The plane sits behind the camera (or on it): fall back to the near
            // clip so the projection stays finite instead of degenerating.
            depth = camera.nearClipPlane > 0f ? camera.nearClipPlane : 0.01f;
        }

        Vector3 min = camera.ViewportToWorldPoint(new Vector3(0f, 0f, depth));
        Vector3 max = camera.ViewportToWorldPoint(new Vector3(1f, 1f, depth));

        Vector2 center = new Vector2((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f);
        Vector2 halfExtents = new Vector2(Mathf.Abs(max.x - min.x) * 0.5f, Mathf.Abs(max.y - min.y) * 0.5f);
        return new ViewRect(center, halfExtents);
    }

    /// <summary>
    /// Picks a point on the perimeter of the view rectangle grown by the given
    /// margins, i.e. just outside the visible area so enemies never pop into
    /// view. <paramref name="t"/> is a normalised position along that perimeter
    /// (wrapped into 0..1) supplied by the caller, which keeps this function pure
    /// and testable. Sides are chosen in proportion to their length, so samples
    /// stay uniform along the perimeter instead of clustering on the short sides.
    /// </summary>
    /// <param name="center">World-space centre of the visible rectangle.</param>
    /// <param name="halfExtents">Half width and half height of the visible rectangle.</param>
    /// <param name="marginX">Extra distance beyond the left/right edges, in world units.</param>
    /// <param name="marginY">Extra distance beyond the top/bottom edges, in world units.</param>
    /// <param name="t">Perimeter position in 0..1; values outside wrap.</param>
    /// <returns>A world-space point outside the visible rectangle.</returns>
    public static Vector2 PickOutsidePoint(Vector2 center, Vector2 halfExtents,
        float marginX, float marginY, float t)
    {
        float outerHalfWidth = Mathf.Max(0f, halfExtents.x) + Mathf.Max(0f, marginX);
        float outerHalfHeight = Mathf.Max(0f, halfExtents.y) + Mathf.Max(0f, marginY);
        float width = outerHalfWidth * 2f;
        float height = outerHalfHeight * 2f;
        float perimeter = 2f * (width + height);
        if (perimeter <= 0f)
        {
            // Degenerate rectangle: nothing sensible to place outside of it.
            return center;
        }

        float distance = Mathf.Repeat(t, 1f) * perimeter;

        // Walk the perimeter clockwise from the bottom-left corner: bottom edge,
        // right edge, top edge, then left edge.
        if (distance < width)
        {
            return new Vector2(center.x - outerHalfWidth + distance, center.y - outerHalfHeight);
        }
        distance -= width;

        if (distance < height)
        {
            return new Vector2(center.x + outerHalfWidth, center.y - outerHalfHeight + distance);
        }
        distance -= height;

        if (distance < width)
        {
            return new Vector2(center.x + outerHalfWidth - distance, center.y + outerHalfHeight);
        }
        distance -= width;

        // Clamped so floating-point drift at t just under 1 cannot place the
        // point past the corner it is heading for.
        return new Vector2(center.x - outerHalfWidth, center.y + outerHalfHeight - Mathf.Min(distance, height));
    }
}
