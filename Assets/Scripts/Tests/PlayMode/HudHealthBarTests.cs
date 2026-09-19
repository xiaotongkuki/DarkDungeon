using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Play-mode tests for the heart row: the broadcast drives redraws between
/// full, half and empty sprites, the heart COUNT tracks the announced maximum
/// (cloning through a layout group), a pool smaller than the row never throws,
/// and drops never leave stale hearts visible.
/// </summary>
public class HudHealthBarTests
{
    private GameObject _rowObject;
    private readonly List<Image> _hearts = new List<Image>();

    [SetUp]
    public void SetUp()
    {
        _rowObject = null;
        _hearts.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        if (_rowObject != null)
        {
            Object.DestroyImmediate(_rowObject);
        }
    }

    /// <summary>
    /// Builds the row inactive first (project arming order): a template heart,
    /// the sprites wired, a HorizontalLayoutGroup as the arranger, then
    /// activation - the template itself is cloned on demand by the row.
    /// </summary>
    /// <param name="withHalf">Whether a half-heart sprite is wired.</param>
    /// <returns>The active row component.</returns>
    private HudHealthBar CreateRow(bool withHalf)
    {
        _rowObject = new GameObject("HudHealthRow");
        _rowObject.SetActive(false);
        _rowObject.AddComponent<RectTransform>();

        var template = new GameObject("HeartTemplate", typeof(RectTransform));
        template.transform.SetParent(_rowObject.transform, false);
        Image templateImage = template.AddComponent<Image>();
        templateImage.sprite = BuildHeartSprite();
        template.SetActive(false); // dormant: never takes a layout slot
        _hearts.Add(templateImage);

        var layout = _rowObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        HudHealthBar row = _rowObject.AddComponent<HudHealthBar>();
        var so = new SerializedObject(row);
        so.FindProperty("_heartTemplate").objectReferenceValue = templateImage;
        so.FindProperty("_fullHeart").objectReferenceValue = BuildHeartSprite();
        so.FindProperty("_emptyHeart").objectReferenceValue = BuildHeartSprite();
        if (withHalf)
        {
            so.FindProperty("_halfHeart").objectReferenceValue = BuildHalfSprite();
        }
        so.FindProperty("_healthPerHeart").floatValue = 2f;
        so.ApplyModifiedProperties();

        _rowObject.SetActive(true);
        return row;
    }

    /// <summary>Creates a dummy-square heart sprite; identity - not pixels - is what the assertions inspect.</summary>
    /// <returns>A binned heart sprite.</returns>
    private static Sprite BuildHeartSprite()
    {
        return Sprite.Create(new Texture2D(4, 4), new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 16f);
    }

    /// <summary>
    /// Creates a distinct half-heart sprite; kept identifiable so the
    /// half/empty/full roles read separately in identity assertions.
    /// </summary>
    /// <returns>A binned half-heart sprite.</returns>
    private static Sprite BuildHalfSprite()
    {
        return Sprite.Create(new Texture2D(8, 4), new Rect(0f, 0f, 8f, 4f), new Vector2(0.5f, 0.5f), 16f);
    }

    /// <summary>
    /// Collects the row's cloned hearts (the template itself hides after the
    /// first clone), left to right, exactly as the player sees them.
    /// </summary>
    /// <param name="row">Owner of the row.</param>
    /// <returns>The visible heart images.</returns>
    private List<Image> VisibleHearts(HudHealthBar row)
    {
        var visible = new List<Image>();
        foreach (Image heart in row.GetComponentsInChildren<Image>(false))
        {
            if (heart.gameObject.activeSelf && heart.gameObject.name != "HeartTemplate")
            {
                visible.Add(heart);
            }
        }
        return visible;
    }

    [UnityTest]
    public IEnumerator Announcements_FillHalfAndEmptyHearts()
    {
        HudHealthBar row = CreateRow(withHalf: true);
        var visible = VisibleHearts(row);
        Assert.AreEqual(0, visible.Count, "The row must start empty before the first pool announcement.");

        EventBus.RaiseHealthChanged(current: 8, max: 8);
        yield return null;

        visible = VisibleHearts(row);

        // The template itself must stay dormant: an active template would be
        // laid out as the leftmost (never-filled) empty heart - the exact
        // "blood pool looks reversed" bug seen in play.
        UnityEngine.Transform template = row.transform.Find("HeartTemplate");
        Assert.IsFalse(template.gameObject.activeSelf,
            "The heart template must not wake up and take a layout slot.");

        // 4 hearts of pool -> 4 clones; a full pool fills all of them full.
        Assert.AreEqual(4, visible.Count, "An 8/8 pool at 2 hp per heart must clone exactly four hearts.");
        UnityEngine.Sprite full = visible[0].sprite;

        EventBus.RaiseHealthChanged(current: 7, max: 8);
        yield return null;

        visible = VisibleHearts(row);
        Assert.AreEqual(4, visible.Count, "7/8 keeps the same four hearts (no shrink).");
        Assert.AreEqual(3, CountMatching(visible, full), "Hearts 0-2 hold 2 hp each and stay full.");

        // The odd last point breaks to the half sprite.
        Assert.IsFalse(ReferenceEquals(full, visible[3].sprite),
            "Heart 3's odd last point must leave the full sprite (half here).");

        EventBus.RaiseHealthChanged(current: 0, max: 8);
        yield return null;

        Assert.AreEqual(0, VisibleHearts(row).Count, "Zero health must hide every heart.");
    }

    [UnityTest]
    public IEnumerator HeartCount_FollowsTheAnnouncedCap()
    {
        HudHealthBar row = CreateRow(withHalf: false);
        var visible = VisibleHearts(row);

        EventBus.RaiseHealthChanged(current: 4, max: 4);
        yield return null;
        Assert.AreEqual(2, VisibleHearts(row).Count, "4 of 4 at 2 hp/heart must show two hearts.");

        EventBus.RaiseHealthChanged(current: 10, max: 10);
        yield return null;
        Assert.AreEqual(5, VisibleHearts(row).Count, "A bigger cap must grow the row on the next announcement.");
    }

    [UnityTest]
    public IEnumerator DamageAnnouncements_FlowToTheRow()
    {
        HudHealthBar row = CreateRow(withHalf: true);
        var visible = VisibleHearts(row);

        // A bare player (no death presentation) shares the real pipeline:
        // Start announces the pool, TakeDamage announces the hit.
        var player = new GameObject("Player");
        player.SetActive(false);
        var health = player.AddComponent<PlayerHealth>();
        TestData.SetObjectReference(health, "_data", TestData.CreatePlayerData(maxHealth: 10));
        player.SetActive(true);
        yield return null; // Start announce
        yield return null;

        Assert.AreEqual(5, VisibleHearts(row).Count, "A 10 hp pool at 2 hp/heart must grow to five hearts.");

        health.TakeDamage(3);
        yield return null;

        visible = VisibleHearts(row);
        Assert.AreEqual(4, visible.Count, "7 hp shows four hearts: three full plus the half-filled fourth.");
        Assert.AreEqual(3, CountMatching(visible, visible[0].sprite),
            "Seven hp fills hearts 0-2 completely; heart 3 holds the odd point as a half.");

        Object.DestroyImmediate(player);
    }

    [UnityTest]
    public IEnumerator SmallPool_DoesNotReadPastTheRow()
    {
        HudHealthBar row = CreateRow(withHalf: false);
        var visible = VisibleHearts(row);

        Assert.DoesNotThrow(() => EventBus.RaiseHealthChanged(current: 1, max: 1));
        yield return null;

        Assert.AreEqual(1, VisibleHearts(row).Count, "1 hp / 2 hp per heart rounds UP to one half-filled heart.");
    }

    /// <summary>
    /// Counts how many of the row's hearts carry exactly the given sprite,
    /// which reads the half/empty/full pattern without wiring names.
    /// </summary>
    /// <param name="hearts">Visible heart images.</param>
    /// <param name="sprite">Sprite to match.</param>
    /// <returns>How many hearts use that sprite.</returns>
    private static int CountMatching(List<Image> hearts, UnityEngine.Sprite sprite)
    {
        int count = 0;
        foreach (Image heart in hearts)
        {
            if (heart.sprite != null && heart.sprite.GetInstanceID() == sprite.GetInstanceID())
            {
                count++;
            }
        }
        return count;
    }
}
