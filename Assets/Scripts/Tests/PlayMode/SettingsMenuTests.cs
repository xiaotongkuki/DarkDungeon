using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Play-mode tests for the settings panel: open/close toggles the panel group,
/// slider changes are persisted to PlayerPrefs, and reopening restores the
/// saved volumes instead of the defaults.
/// </summary>
public class SettingsMenuTests
{
    private GameObject _root;
    private UnityEngine.UI.Button _back;
    private UnityEngine.UI.Slider _master;
    private UnityEngine.UI.Slider _bgm;
    private UnityEngine.UI.Slider _sfx;
    private SettingsMenuController _controller;

    [SetUp]
    public void SetUp()
    {
        // Each fixture instance is reused across tests in this runner: drop
        // any leftover values so the assertions never see a stale save.
        PlayerPrefs.DeleteKey(SettingsMenuController.MasterKeyForTests);
        PlayerPrefs.DeleteKey(SettingsMenuController.BgmKeyForTests);
        PlayerPrefs.DeleteKey(SettingsMenuController.SfxKeyForTests);
    }

    [TearDown]
    public void TearDown()
    {
        PlayerPrefs.DeleteKey(SettingsMenuController.MasterKeyForTests);
        PlayerPrefs.DeleteKey(SettingsMenuController.BgmKeyForTests);
        PlayerPrefs.DeleteKey(SettingsMenuController.SfxKeyForTests);
        if (_root != null)
        {
            Object.DestroyImmediate(_root);
        }
    }

    /// <summary>
    /// Builds a controller with its toggled group, back button and three
    /// sliders wired, inactive first - the project arming order - then
    /// activates it so Awake runs with the references already in place.
    /// </summary>
    private SettingsMenuController CreateController()
    {
        _root = new GameObject("SettingsRoot");
        _root.SetActive(false);

        var panel = new GameObject("Panel");
        panel.transform.SetParent(_root.transform, false);

        var backGO = new GameObject("Back", typeof(RectTransform));
        backGO.transform.SetParent(panel.transform, false);
        _back = backGO.AddComponent<UnityEngine.UI.Button>();

        _master = CreateSlider("Master", panel.transform);
        _bgm = CreateSlider("Bgm", panel.transform);
        _sfx = CreateSlider("Sfx", panel.transform);

        _controller = _root.AddComponent<SettingsMenuController>();
        var so = new SerializedObject(_controller);
        so.FindProperty("_panel").objectReferenceValue = panel;
        so.FindProperty("_backButton").objectReferenceValue = _back;
        so.FindProperty("_masterSlider").objectReferenceValue = _master;
        so.FindProperty("_bgmSlider").objectReferenceValue = _bgm;
        so.FindProperty("_sfxSlider").objectReferenceValue = _sfx;
        so.ApplyModifiedProperties();

        _root.SetActive(true);
        return _controller;
    }

    /// <summary>
    /// Minimal slider with a backing value range; no layout work is needed
    /// because the dispatch under test is the value change event.
    /// </summary>
    /// <param name="name">Node name.</param>
    /// <param name="parent">Panel transform.</param>
    /// <returns>The slider component.</returns>
    private static UnityEngine.UI.Slider CreateSlider(string name, Transform parent)
    {
        var go = new GameObject("Slider_" + name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var slider = go.AddComponent<UnityEngine.UI.Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        return slider;
    }

    [Test]
    public void Panel_IsHiddenOnWake_AndShownByOpen()
    {
        SettingsMenuController controller = CreateController();

        // Awake hides the toggled group; the controller itself stays running.
        Transform panel = _root.transform.Find("Panel");
        Assert.IsFalse(panel.gameObject.activeSelf, "Panel must start hidden.");
        Assert.IsTrue(controller.enabled, "Controller must stay enabled while hidden.");

        controller.Open();
        Assert.IsTrue(panel.gameObject.activeSelf, "Open shows the panel.");
    }

    [Test]
    public void BackButton_HidesThePanel()
    {
        SettingsMenuController controller = CreateController();
        controller.Open();

        _back.onClick.Invoke();

        Assert.IsFalse(_root.transform.Find("Panel").gameObject.activeSelf,
            "The back button routes to Close and hides the panel.");
    }

    [Test]
    public void SliderChange_PersistsVolume()
    {
        CreateController();

        _master.value = 0.25f;
        _bgm.value = 0.5f;
        _sfx.value = 0.75f;

        Assert.AreEqual(0.25f, PlayerPrefs.GetFloat(SettingsMenuController.MasterKeyForTests, -1f), 0.001f,
            "Master change writes the preference immediately.");
        Assert.AreEqual(0.5f, PlayerPrefs.GetFloat(SettingsMenuController.BgmKeyForTests, -1f), 0.001f,
            "BGM change writes the preference immediately.");
        Assert.AreEqual(0.75f, PlayerPrefs.GetFloat(SettingsMenuController.SfxKeyForTests, -1f), 0.001f,
            "SFX change writes the preference immediately.");
    }

    [Test]
    public void Reopen_RestoresSavedVolumesWithoutRebroadcasting()
    {
        // First session moves the sliders around; the saved values are the
        // last written ones.
        SettingsMenuController first = CreateController();
        first.Open();
        _master.value = 0.4f;
        first.Close();
        Object.DestroyImmediate(_root);
        _root = null;

        // Second session (fresh controller over the same player prefs) must
        // load the saved value silently: SetWithoutNotify means no change
        // event fired during Awake can re-save a stale default.
        SettingsMenuController second = CreateController();

        Assert.AreEqual(0.4f, _master.value, 0.001f,
            "Awake restores the persisted slider value.");
        Assert.AreEqual(0.4f, PlayerPrefs.GetFloat(SettingsMenuController.MasterKeyForTests, -1f), 0.001f,
            "Loading does not rewrite the save.");
    }
}
