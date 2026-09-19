using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Covers the footprint declaration props use to reserve tiles, including the
/// axis swap a quarter turn applies. A wrong swap is invisible in code review
/// and obvious only as overlapping props, so it is pinned here.
/// </summary>
public class PropFootprintTests
{
    private GameObject _host;

    [SetUp]
    public void SetUp()
    {
        _host = new GameObject("PropFootprintHost");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_host);
    }

    /// <summary>Adds a footprint component configured as asked.</summary>
    /// <param name="size">Footprint at rotation zero.</param>
    /// <param name="rotatable">Whether turns are allowed.</param>
    /// <returns>The component.</returns>
    private PropFootprint Make(Vector2Int size, bool rotatable)
    {
        PropFootprint footprint = _host.AddComponent<PropFootprint>();
        footprint.Configure(size, rotatable);
        return footprint;
    }

    [Test]
    public void Size_IsNeverSmallerThanOneTile()
    {
        PropFootprint footprint = Make(Vector2Int.zero, false);
        Assert.AreEqual(Vector2Int.one, footprint.Size, "A prop always covers at least one tile.");
    }

    [Test]
    public void SizeAfter_EvenTurns_KeepsTheFootprint()
    {
        PropFootprint footprint = Make(new Vector2Int(2, 1), true);
        Assert.AreEqual(new Vector2Int(2, 1), footprint.SizeAfter(0));
        Assert.AreEqual(new Vector2Int(2, 1), footprint.SizeAfter(2));
        Assert.AreEqual(new Vector2Int(2, 1), footprint.SizeAfter(4));
    }

    [Test]
    public void SizeAfter_OddTurns_SwapsTheAxes()
    {
        PropFootprint footprint = Make(new Vector2Int(3, 1), true);
        Assert.AreEqual(new Vector2Int(1, 3), footprint.SizeAfter(1));
        Assert.AreEqual(new Vector2Int(1, 3), footprint.SizeAfter(3));
        Assert.AreEqual(new Vector2Int(1, 3), footprint.SizeAfter(5), "Five turns is one turn past a full spin.");
        Assert.AreEqual(new Vector2Int(3, 1), footprint.SizeAfter(4));
    }

    [Test]
    public void SizeAfter_NegativeTurns_StillWraps()
    {
        PropFootprint footprint = Make(new Vector2Int(2, 1), true);
        Assert.AreEqual(new Vector2Int(1, 2), footprint.SizeAfter(-1));
        Assert.AreEqual(new Vector2Int(2, 1), footprint.SizeAfter(-2));
    }

    [Test]
    public void Configure_ReportsWhetherTurnsAreAllowed()
    {
        Assert.IsTrue(Make(new Vector2Int(1, 1), true).Rotatable);
        Assert.IsFalse(Make(new Vector2Int(1, 1), false).Rotatable);
    }
}
