using UnityEngine;

/// <summary>
/// Pure chase-steering maths: given where an enemy is and where it wants to go,
/// returns the position for the next physics step.
///
/// Kept free of engine state (no Rigidbody2D, no Transform, no Random) so the
/// steering is deterministic and unit-testable in edit mode, and so a central
/// movement system could later drive many enemies through this same kernel
/// without duplicating the maths.
/// </summary>
public static class ChaseSteering
{
    /// <summary>
    /// Advances a position one step toward a target at the given speed, halting on
    /// a ring <paramref name="stopDistance"/> short of it.
    ///
    /// The step is clamped to the remaining gap so the enemy can never overshoot
    /// and jitter back and forth across the stopping ring.
    /// </summary>
    /// <param name="position">Current position in world space.</param>
    /// <param name="target">Position being chased.</param>
    /// <param name="speed">Speed in units per second; zero or negative means "do not move".</param>
    /// <param name="stopDistance">
    /// Distance from the target at which the enemy halts, in world units. Zero
    /// walks all the way onto the target; negative values are treated as zero.
    /// </param>
    /// <param name="deltaTime">Step duration in seconds; zero or negative means "do not move".</param>
    /// <returns>
    /// The position after the step, or <paramref name="position"/> unchanged when
    /// the enemy is already close enough or cannot move.
    /// </returns>
    public static Vector2 Step(Vector2 position, Vector2 target, float speed, float stopDistance, float deltaTime)
    {
        float stop = Mathf.Max(0f, stopDistance);
        if (speed <= 0f || deltaTime <= 0f)
        {
            return position;
        }

        Vector2 toTarget = target - position;
        float distance = toTarget.magnitude;
        if (distance <= stop)
        {
            return position;
        }

        // normalized rather than dividing by distance: a zero-length vector yields
        // zero here, where a division would yield NaN if the two ever coincided.
        Vector2 direction = toTarget.normalized;

        float step = speed * deltaTime;
        float allowed = distance - stop;
        if (step > allowed)
        {
            step = allowed;
        }

        return position + direction * step;
    }
}
