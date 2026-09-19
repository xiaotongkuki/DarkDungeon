using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Covers the arena geometry service the camera and the spawner share: the
/// playable area, the wider camera area that lets the walls be seen, and the
/// pass-through behaviour when no arena exists (bare scenes and tests).
/// </summary>
public class StageBoundsTests
{
    /// <summary>
    /// Both bound sets are static, so the fixture's reuse would otherwise leak
    /// between tests.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        StageBounds.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        StageBounds.Clear();
    }

    [Test]
    public void WithoutBounds_PassesPointsThrough()
    {
        Assert.IsFalse(StageBounds.HasBounds, "A fresh session must publish no bounds.");

        Vector2 point = new Vector2(999f, -999f);
        Assert.AreEqual(point, StageBounds.ClampInside(point, 1f), "With no arena the clamp must be a no-op.");
        Assert.AreEqual(point, StageBounds.ClampCamera(point, Vector2.one), "The camera clamp must be a no-op too.");
        Assert.IsTrue(StageBounds.IsInside(point, 1f), "With no arena everything counts as inside.");
    }

    [Test]
    public void ClampsPointsIntoTheArena()
    {
        StageBounds.SetSize(new Vector2(40f, 22f));

        Assert.AreEqual(new Vector2(19.5f, 10.5f), StageBounds.ClampInside(new Vector2(99f, 99f), 0.5f),
            "A far point must clamp to the inset limit.");
        Assert.AreEqual(new Vector2(-19.5f, -10.5f), StageBounds.ClampInside(new Vector2(-99f, -99f), 0.5f));
        Assert.IsTrue(StageBounds.IsInside(new Vector2(0f, 0f), 0.5f));
        Assert.IsFalse(StageBounds.IsInside(new Vector2(20f, 0f), 0.5f),
            "A point on the wall must count as outside the playable area.");
        Assert.AreEqual(StageBounds.InnerSize, new Vector2(40f, 22f), "Inner size must echo the set size.");
    }

    [Test]
    public void ClampsPerAxisInsets()
    {
        StageBounds.SetSize(new Vector2(28f, 28f));

        // x keeps the wide inset out while y keeps the narrow one: one shared
        // inset cannot express this, which is what the per-axis overload is for.
        Vector2 clamped = StageBounds.ClampInside(new Vector2(999f, 999f), new Vector2(9f, 5f));
        Assert.AreEqual(new Vector2(5f, 9f), clamped,
            "A wide-x inset must squeeze x harder than y on a square arena.");
    }

    [Test]
    public void CameraBoundsReachPastTheWalls()
    {
        StageBounds.SetSize(new Vector2(28f, 28f));
        StageBounds.SetCameraSize(new Vector2(32f, 32f));

        Vector2 inset = new Vector2(11f, 5f);
        Vector2 spawnLimit = StageBounds.ClampInside(new Vector2(99f, 99f), inset);
        Vector2 cameraLimit = StageBounds.ClampCamera(new Vector2(99f, 99f), inset);

        Assert.AreEqual(new Vector2(3f, 9f), spawnLimit, "Spawns stay inside the playable area.");
        Assert.AreEqual(new Vector2(5f, 11f), cameraLimit,
            "The camera may roam further than spawns, so the wall lands inside the view.");
    }

    [Test]
    public void ClearDropsTheArena()
    {
        StageBounds.SetSize(new Vector2(40f, 22f));
        StageBounds.SetCameraSize(new Vector2(44f, 26f));
        StageBounds.Clear();

        Assert.IsFalse(StageBounds.HasBounds, "Clear must drop the published bounds.");
    }
}
