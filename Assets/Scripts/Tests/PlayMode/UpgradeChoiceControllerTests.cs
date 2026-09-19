using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Play-mode tests for the level-up choice: it pauses the run and offers three
/// distinct upgrades, applies the picked one, resumes, and works through every
/// level-up a single gem may have earned.
///
/// Time.timeScale is global, so every test restores it in teardown; a leaked pause
/// would freeze every later test in the run.
/// </summary>
public class UpgradeChoiceControllerTests
{
    private GameObject _flowObject;
    private GameObject _panelObject;
    private GameObject _modifiersObject;
    private readonly List<GameObject> _buttonObjects = new List<GameObject>();
    private readonly List<UpgradeData> _upgrades = new List<UpgradeData>();
    private UpgradePoolData _pool;
    private PlayerStatModifiers _modifiers;
    private UpgradeChoiceController _controller;

    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 1f;

        _modifiersObject = new GameObject("Player");
        _modifiers = _modifiersObject.AddComponent<PlayerStatModifiers>();

        // Four identical damage upgrades: any pick is deterministic to assert on.
        for (int i = 0; i < 4; i++)
        {
            _upgrades.Add(TestData.CreateUpgradeData(UpgradeStat.Damage, 1f));
        }
        _pool = TestData.CreateUpgradePoolData(_upgrades.ToArray());

        // Built inactive so the references can be assigned before Awake runs, which
        // is where the controller validates them and hides the panel.
        _flowObject = new GameObject("Flow", typeof(RectTransform));
        _flowObject.SetActive(false);

        _panelObject = new GameObject("UpgradePanel", typeof(RectTransform));
        _panelObject.transform.SetParent(_flowObject.transform);

        var buttons = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            var buttonObject = new GameObject("ChoiceButton" + i, typeof(RectTransform));
            buttonObject.transform.SetParent(_panelObject.transform);
            buttons[i] = buttonObject.AddComponent<Button>();

            var labelObject = new GameObject("Label" + i, typeof(RectTransform));
            labelObject.transform.SetParent(buttonObject.transform);
            labelObject.AddComponent<Text>();

            _buttonObjects.Add(buttonObject);
        }

        _controller = _flowObject.AddComponent<UpgradeChoiceController>();
        TestData.SetObjectReference(_controller, "_pool", _pool);
        TestData.SetObjectReference(_controller, "_modifiers", _modifiers);
        TestData.SetObjectReference(_controller, "_panel", _panelObject);

        var so = new SerializedObject(_controller);
        SerializedProperty array = so.FindProperty("_choiceButtons");
        array.arraySize = 3;
        for (int i = 0; i < 3; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
        }
        so.ApplyModifiedProperties();

        _flowObject.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;

        for (int i = 0; i < _buttonObjects.Count; i++)
        {
            Object.DestroyImmediate(_buttonObjects[i]);
        }
        _buttonObjects.Clear();

        Object.DestroyImmediate(_panelObject);
        Object.DestroyImmediate(_flowObject);
        Object.DestroyImmediate(_modifiersObject);

        for (int i = 0; i < _upgrades.Count; i++)
        {
            Object.DestroyImmediate(_upgrades[i]);
        }
        _upgrades.Clear();

        Object.DestroyImmediate(_pool);
    }

    /// <summary>Reads the upgrades currently offered, for assertions.</summary>
    /// <returns>The offered upgrades.</returns>
    private List<UpgradeData> OfferedUpgrades()
    {
        var field = typeof(UpgradeChoiceController).GetField("_offered",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (List<UpgradeData>)field.GetValue(_controller);
    }

    [Test]
    public void LevelUp_ShowsThreeDistinctChoicesAndPauses()
    {
        EventBus.RaiseLevelUp(2);

        Assert.IsTrue(_panelObject.activeSelf, "A level-up must bring the panel up.");
        Assert.AreEqual(0f, Time.timeScale, "The run must pause while the choice is open.");

        List<UpgradeData> offered = OfferedUpgrades();
        Assert.AreEqual(3, offered.Count, "Exactly three upgrades must be offered.");
        Assert.AreNotSame(offered[0], offered[1], "Three copies of one upgrade is not a choice.");
        Assert.AreNotSame(offered[1], offered[2], "Three copies of one upgrade is not a choice.");
        Assert.AreNotSame(offered[0], offered[2], "Three copies of one upgrade is not a choice.");
    }

    [Test]
    public void Choosing_AppliesTheUpgradeAndResumes()
    {
        EventBus.RaiseLevelUp(2);
        _controller.Choose(0);

        Assert.AreEqual(1f, _modifiers.DamageBonus, "The picked upgrade must be applied.");
        Assert.IsFalse(_panelObject.activeSelf, "The panel must close once the choice is made.");
        Assert.AreEqual(1f, Time.timeScale, "The run must resume.");
    }

    [Test]
    public void TwoLevelUpsInOneGem_AreChosenOneAfterAnother()
    {
        // A single big gem can cross two thresholds; both choices must be offered,
        // not silently dropped.
        EventBus.RaiseLevelUp(2);
        EventBus.RaiseLevelUp(3);

        Assert.IsTrue(_panelObject.activeSelf, "The panel must stay up for the second choice.");

        _controller.Choose(0);
        Assert.AreEqual(1f, _modifiers.DamageBonus, "The first choice must have applied.");
        Assert.IsTrue(_panelObject.activeSelf, "The second level-up must still be waiting.");
        Assert.AreEqual(0f, Time.timeScale, "The run must stay paused between the two choices.");

        _controller.Choose(1);
        Assert.AreEqual(2f, _modifiers.DamageBonus, "The second choice must have applied too.");
        Assert.IsFalse(_panelObject.activeSelf);
        Assert.AreEqual(1f, Time.timeScale, "Only after the last choice may the run resume.");
    }

    [UnityTest]
    public IEnumerator DisabledController_IgnoresLevelUps()
    {
        _controller.enabled = false;

        EventBus.RaiseLevelUp(2);
        yield return null;

        Assert.IsFalse(_panelObject.activeSelf, "A disabled controller must not open the panel.");
        Assert.AreEqual(1f, Time.timeScale, "A disabled controller must not pause the run.");
    }
}
