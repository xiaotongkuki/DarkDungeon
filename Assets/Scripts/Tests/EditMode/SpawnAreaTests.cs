using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pure-logic tests for the off-screen spawn placement maths: samples must land
/// outside the visible rectangle, stay inside the margin band, cover all four
/// sides and distribute uniformly along the perimeter.
/// </summary>
public class SpawnAreaTests
{
    // A 16 x 9 world-unit view centred away from the origin, so nothing passes by
    // accident when the maths forgets the centre offset.
    private static readonly Vector2 Center = new Vector2(10f, -5f);
    private static readonly Vector2 HalfExtents = new Vector2(8f, 4.5f);
    private const float MarginX = 1.2f;
    private const float MarginY = 1.2f;
    private const int Samples = 400;

    /// <summary>Position tolerance for float comparisons, in world units.</summary>
    private const float Tolerance = 1e-4f;

    /// <summary>
    /// Samples the perimeter at evenly spaced values of t and hands each result
    /// to the caller, keeping the sampling loop out of every test.
    /// </summary>
    /// <param name="marginX">Horizontal margin to use.</param>
    /// <param name="marginY">Vertical margin to use.</param>
    /// <param name="visit">Called once per sample with its t and resulting point.</param>
    private static void ForEachSample(float marginX, float marginY, System.Action<float, Vector2> visit)
    {
        for (int i = 0; i < Samples; i++)
        {
            float t = i / (float)Samples;
            visit(t, SpawnArea.PickOutsidePoint(Center, HalfExtents, marginX, marginY, t));
        }
    }

    [Test]
    public void PickOutsidePoint_AlwaysLandsOutsideTheView()
    {
        ForEachSample(MarginX, MarginY, (t, point) =>
        {
            bool outsideX = Mathf.Abs(point.x - Center.x) > HalfExtents.x;
            bool outsideY = Mathf.Abs(point.y - Center.y) > HalfExtents.y;
            Assert.IsTrue(outsideX || outsideY,
                $"Sample t={t} landed inside the view rectangle: {point}");
        });
    }

    [Test]
    public void PickOutsidePoint_StaysInsideTheMarginBand()
    {
        ForEachSample(MarginX, MarginY, (t, point) =>
        {
            Assert.LessOrEqual(Mathf.Abs(point.x - Center.x), HalfExtents.x + MarginX + Tolerance,
                $"Sample t={t} overshot the horizontal margin: {point}");
            Assert.LessOrEqual(Mathf.Abs(point.y - Center.y), HalfExtents.y + MarginY + Tolerance,
                $"Sample t={t} overshot the vertical margin: {point}");
        });
    }

    [Test]
    public void PickOutsidePoint_CoversAllFourSides()
    {
        bool bottom = false, top = false, left = false, right = false;

        ForEachSample(MarginX, MarginY, (t, point) =>
        {
            bottom |= point.y < Center.y - HalfExtents.y;
            top |= point.y > Center.y + HalfExtents.y;
            left |= point.x < Center.x - HalfExtents.x;
            right |= point.x > Center.x + HalfExtents.x;
        });

        Assert.IsTrue(bottom && top && left && right,
            $"All four sides must be reachable; got bottom={bottom} top={top} left={left} right={right}.");
    }

    [Test]
    public void PickOutsidePoint_ZeroMargin_LiesOnTheViewBoundary()
    {
        // t = 0 is defined as the bottom-left corner of the (unexpanded) rectangle.
        Vector2 point = SpawnArea.PickOutsidePoint(Center, HalfExtents, 0f, 0f, 0f);

        Assert.AreEqual(Center.x - HalfExtents.x, point.x, Tolerance);
        Assert.AreEqual(Center.y - HalfExtents.y, point.y, Tolerance);
    }

    [Test]
    public void PickOutsidePoint_DistributesSamplesBySideLength()
    {
        // The rectangle is wider than it is tall, so the two horizontal sides own
        // a larger share of the perimeter; unweighted side picking would cluster
        // samples on the short sides instead.
        float width = (HalfExtents.x + MarginX) * 2f;
        float height = (HalfExtents.y + MarginY) * 2f;
        float expectedHorizontalShare = (2f * width) / (2f * (width + height));

        float bottomEdge = Center.y - HalfExtents.y - MarginY;
        float topEdge = Center.y + HalfExtents.y + MarginY;

        int horizontal = 0;
        ForEachSample(MarginX, MarginY, (t, point) =>
        {
            // Classify by which edge line the sample lies on, not by how far it is
            // from the centre: the vertical edges also reach past the corners.
            bool onHorizontalEdge = Mathf.Abs(point.y - bottomEdge) < Tolerance
                || Mathf.Abs(point.y - topEdge) < Tolerance;
            if (onHorizontalEdge)
            {
                horizontal++;
            }
        });

        float actualShare = horizontal / (float)Samples;
        Assert.AreEqual(expectedHorizontalShare, actualShare, 0.02f,
            "Samples must be spread in proportion to each side's length.");
    }

    [Test]
    public void PickOutsidePoint_WrapsOutOfRangeT()
    {
        Vector2 start = SpawnArea.PickOutsidePoint(Center, HalfExtents, MarginX, MarginY, 0f);

        Assert.AreEqual(start, SpawnArea.PickOutsidePoint(Center, HalfExtents, MarginX, MarginY, 1f));
        Assert.AreEqual(start, SpawnArea.PickOutsidePoint(Center, HalfExtents, MarginX, MarginY, 3f));
    }

    [Test]
    public void PickOutsidePoint_NegativeT_IsWrapped()
    {
        Vector2 last = SpawnArea.PickOutsidePoint(Center, HalfExtents, MarginX, MarginY, 0.99f);
        Vector2 wrapped = SpawnArea.PickOutsidePoint(Center, HalfExtents, MarginX, MarginY, -0.01f);

        // -0.01 wraps to 0.99, so both must describe the same point.
        Assert.AreEqual(last.x, wrapped.x, Tolerance);
        Assert.AreEqual(last.y, wrapped.y, Tolerance);
    }

    [Test]
    public void PickOutsidePoint_DegenerateRect_ReturnsCenter()
    {
        Vector2 point = SpawnArea.PickOutsidePoint(Vector2.zero, Vector2.zero, 0f, 0f, 0.4f);

        Assert.AreEqual(Vector2.zero, point);
    }

    [Test]
    public void GetViewRect_NullCamera_ReturnsEmptyRect()
    {
        ViewRect view = SpawnArea.GetViewRect(null, 0f);

        Assert.AreEqual(Vector2.zero, view.Center);
        Assert.AreEqual(Vector2.zero, view.HalfExtents);
    }
}
