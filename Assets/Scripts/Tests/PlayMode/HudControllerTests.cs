using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Play-mode tests for the HUD: it starts at zero and follows the score, level and
/// experience announcements on the bus.
/// </summary>
public class HudControllerTests
{
    private GameObject _hudObject;

    [TearDown]
    public void TearDown()
    {
        if (_hudObject != null)
        {
            Object.DestroyImmediate(_hudObject);
        }
    }

    /// <summary>
    /// Creates a HUD with real labels.
    ///
    /// Built inactive so the references can be assigned before Awake runs, which is
    /// where the controller validates them.
    /// </summary>
    /// <param name="scoreLabel">Receives the score label.</param>
    /// <param name="xpLabel">Receives the level label.</param>
    private void CreateHud(out Text scoreLabel, out Text levelLabel)
    {
        _hudObject = new GameObject("Hud", typeof(RectTransform));
        _hudObject.SetActive(false);

        // One label per child object: a GameObject cannot carry two Text components,
        // and the second AddComponent would hand back null.
        var scoreObject = new GameObject("ScoreLabel", typeof(RectTransform));
        scoreObject.transform.SetParent(_hudObject.transform);
        scoreLabel = scoreObject.AddComponent<Text>();

        var levelObject = new GameObject("LevelLabel", typeof(RectTransform));
        levelObject.transform.SetParent(_hudObject.transform);
        levelLabel = levelObject.AddComponent<Text>();

        HudController hud = _hudObject.AddComponent<HudController>();
        TestData.SetObjectReference(hud, "_scoreText", scoreLabel);
        TestData.SetObjectReference(hud, "_levelText", levelLabel);

        _hudObject.SetActive(true);
    }

    [Test]
    public void StartsAtZeroAndFollowsTheScore()
    {
        CreateHud(out Text scoreLabel, out _);

        Assert.AreEqual("击杀: 0", scoreLabel.text, "A fresh run must show zero.");

        EventBus.RaiseScoreChanged(7);

        Assert.AreEqual("击杀: 7", scoreLabel.text, "The label must follow the announced score.");
    }

    [Test]
    public void LevelLabelFollowsTheLevelAnnouncementOnly()
    {
        CreateHud(out _, out Text levelLabel);

        // The starting write happens on enable; the experience announcement
        // (progress 0 of 5) must not touch the level wording any more.
        EventBus.RaiseXpChanged(0, 5);
        Assert.AreEqual("Lv.1", levelLabel.text, "The level label carries only the level, no progress text.");

        EventBus.RaiseLevelUp(2);
        Assert.AreEqual("Lv.2", levelLabel.text, "A level-up announcement must rewrite the label.");

        EventBus.RaiseXpChanged(3, 10);
        Assert.AreEqual("Lv.2", levelLabel.text,
            "Experience progress alone must not change the level label.");
    }

    [Test]
    public void AtTheLevelCap_ShowsTheCappedWording()
    {
        CreateHud(out _, out Text levelLabel);

        EventBus.RaiseLevelUp(8);
        EventBus.RaiseXpChanged(4, 0);

        Assert.AreEqual("Lv.8 MAX", levelLabel.text,
            "A zero requirement means the curve is exhausted, which must read as a cap.");
    }

    [Test]
    public void DisabledHud_StopsUpdating()
    {
        CreateHud(out Text scoreLabel, out _);

        _hudObject.SetActive(false);
        EventBus.RaiseScoreChanged(9);

        Assert.AreEqual("击杀: 0", scoreLabel.text, "A hidden HUD must not keep writing to its label.");
    }
}
