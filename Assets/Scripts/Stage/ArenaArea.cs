using UnityEngine;

/// <summary>
/// Marks the rectangle terrain is generated in, and shows its snapped form in
/// the Scene view.
///
/// The arena walls belong to the designer, so the generator must not guess where
/// the playable area is from them. Instead one object with a trigger collider
/// describes the area: drag the object and edit the collider's Size in the
/// Inspector, and the gizmo draws both the authored rectangle and the tile
/// lattice it snaps to, so misalignment is visible before a single prop is
/// placed.
///
/// The rectangle is read, never written: the collider is only a shape.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class ArenaArea : MonoBehaviour
{
    [Tooltip("Colour of the authored rectangle in the Scene view.")]
    [SerializeField] private Color _outlineColor = new Color(0.3f, 0.9f, 1f, 1f);
    [Tooltip("Colour of the snapped (grid-aligned) rectangle drawn on top.")]
    [SerializeField] private Color _snappedColor = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("Draw the one-unit tile lattice inside the snapped rectangle.")]
    [SerializeField] private bool _drawGrid = true;
    [Tooltip("Most grid lines drawn per axis, so a huge area cannot flood the Scene view.")]
    [SerializeField] private int _maxGridLines = 80;

    /// <summary>The collider that defines the area; cached once.</summary>
    private BoxCollider2D _shape;

    /// <summary>The authored rectangle in world space, unsnapped.</summary>
    public Rect WorldRect
    {
        get
        {
            BoxCollider2D shape = Shape;
            return new Rect(shape.bounds.min, shape.bounds.size);
        }
    }

    /// <summary>
    /// The rectangle snapped to the tile lattice. This is what terrain
    /// generation uses: the minimum corner rounds down, the maximum up, so the
    /// snapped area always covers the authored one in whole tiles.
    /// </summary>
    public Rect SnappedRect => ArenaGrid.SnapRect(WorldRect);

    /// <summary>
    /// The collider, resolved on first use so the component works whether it was
    /// added before or after the collider.
    /// </summary>
    private BoxCollider2D Shape
    {
        get
        {
            if (_shape == null)
            {
                _shape = GetComponent<BoxCollider2D>();
            }
            return _shape;
        }
    }

    /// <summary>
    /// Draws the authored rectangle, the snapped rectangle and the tile lattice
    /// so the alignment of the terrain area is visible while editing.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (Shape == null)
        {
            return;
        }

        Rect raw = WorldRect;
        Rect snapped = SnappedRect;

        Gizmos.color = _outlineColor;
        DrawRect(raw);

        Gizmos.color = _snappedColor;
        DrawRect(snapped);

        if (!_drawGrid)
        {
            return;
        }

        int columns = Mathf.RoundToInt(snapped.width / ArenaGrid.TileSize);
        int rows = Mathf.RoundToInt(snapped.height / ArenaGrid.TileSize);
        if (columns > _maxGridLines || rows > _maxGridLines)
        {
            return;
        }

        Gizmos.color = new Color(_snappedColor.r, _snappedColor.g, _snappedColor.b, 0.35f);
        for (int x = 0; x <= columns; x++)
        {
            float worldX = snapped.xMin + x * ArenaGrid.TileSize;
            Gizmos.DrawLine(new Vector3(worldX, snapped.yMin, 0f), new Vector3(worldX, snapped.yMax, 0f));
        }
        for (int y = 0; y <= rows; y++)
        {
            float worldY = snapped.yMin + y * ArenaGrid.TileSize;
            Gizmos.DrawLine(new Vector3(snapped.xMin, worldY, 0f), new Vector3(snapped.xMax, worldY, 0f));
        }
    }

    /// <summary>
    /// Draws one world-space rectangle as four lines.
    /// </summary>
    /// <param name="rect">Rectangle to outline.</param>
    private static void DrawRect(Rect rect)
    {
        Vector3 bottomLeft = new Vector3(rect.xMin, rect.yMin, 0f);
        Vector3 bottomRight = new Vector3(rect.xMax, rect.yMin, 0f);
        Vector3 topRight = new Vector3(rect.xMax, rect.yMax, 0f);
        Vector3 topLeft = new Vector3(rect.xMin, rect.yMax, 0f);

        Gizmos.DrawLine(bottomLeft, bottomRight);
        Gizmos.DrawLine(bottomRight, topRight);
        Gizmos.DrawLine(topRight, topLeft);
        Gizmos.DrawLine(topLeft, bottomLeft);
    }
}
