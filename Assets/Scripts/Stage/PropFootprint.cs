using UnityEngine;

/// <summary>
/// Declares how many tiles a prop covers and whether it may be turned, so the
/// placement algorithm can reserve the right cells instead of guessing from
/// collider bounds.
///
/// The footprint is authored in whole tiles because the arena is a tile lattice:
/// a 2x1 wall segment and a 2x2 L piece must reserve different amounts of space,
/// and a designer reading the prefab should see that without measuring pixels.
/// </summary>
public class PropFootprint : MonoBehaviour
{
    [Tooltip("Tiles the prop covers at rotation zero, as (width, height).")]
    [SerializeField] private Vector2Int _size = Vector2Int.one;
    [Tooltip("Whether the placer may turn the prop in quarter turns, swapping its footprint when it does.")]
    [SerializeField] private bool _rotatable;

    /// <summary>Tiles covered at rotation zero.</summary>
    public Vector2Int Size => new Vector2Int(Mathf.Max(1, _size.x), Mathf.Max(1, _size.y));

    /// <summary>Whether the prop may be turned in quarter turns.</summary>
    public bool Rotatable => _rotatable;

    /// <summary>
    /// The footprint after a quarter-turn count: odd counts swap width and
    /// height.
    /// </summary>
    /// <param name="quarterTurns">Quarter turns applied, in 90-degree steps.</param>
    /// <returns>Footprint in tiles after the rotation.</returns>
    public Vector2Int SizeAfter(int quarterTurns)
    {
        int turns = ((quarterTurns % 4) + 4) % 4;
        Vector2Int size = Size;
        return turns % 2 == 1 ? new Vector2Int(size.y, size.x) : size;
    }

    /// <summary>
    /// Sets the footprint in code, for prefabs built by tooling and for tests.
    /// </summary>
    /// <param name="size">Tiles covered at rotation zero.</param>
    /// <param name="rotatable">Whether the prop may be turned.</param>
    public void Configure(Vector2Int size, bool rotatable)
    {
        _size = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        _rotatable = rotatable;
    }
}
