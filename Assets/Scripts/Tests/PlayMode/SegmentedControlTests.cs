using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Play-mode tests for the tab control: single selection through the toggle
/// group, value-change notifications, default first-tab selection on wake and
/// page visibility switching.
/// </summary>
public class SegmentedControlTests
{
    private GameObject _root;
    private UnityEngine.UI.Toggle[] _toggles;
    private GameObject[] _pages;
    private SegmentedControl _control;
    private int _lastEvent;
    private bool _eventFired;

    [SetUp]
    public void SetUp()
    {
        // Fixture instances are reused across tests in this runner: reset the
        // per-test state every time.
        _lastEvent = -99;
        _eventFired = false;
        _root = null;
        _toggles = null;
        _pages = null;
        _control = null;
    }

    [TearDown]
    public void TearDown()
    {
        if (_root != null)
        {
            Object.DestroyImmediate(_root);
        }
    }

    /// <summary>
    /// Builds a three-tab control with paired pages, inactive first - the
    /// project arming order - then enables it so Awake arms the wiring. The
    /// value-changed event is captured through a C# subscription.
    /// </summary>
    private SegmentedControl CreateControl()
    {
        _root = new GameObject("TabsRoot");
        _root.SetActive(false);

        _toggles = new UnityEngine.UI.Toggle[3];
        for (int i = 0; i < 3; i++)
        {
            var go = new UnityEngine.GameObject("Tab_" + i, typeof(RectTransform));
            go.transform.SetParent(_root.transform, false);
            UnityEngine.UI.Image img = go.AddComponent<UnityEngine.UI.Image>();
            UnityEngine.UI.Toggle toggle = go.AddComponent<UnityEngine.UI.Toggle>();
            toggle.targetGraphic = img;
            toggle.isOn = false;
            _toggles[i] = toggle;
        }

        _pages = new GameObject[3];
        for (int i = 0; i < 3; i++)
        {
            var page = new UnityEngine.GameObject("Page_" + i);
            page.transform.SetParent(_root.transform, false);
            _pages[i] = page;
        }

        _control = _root.AddComponent<SegmentedControl>();
        var so = new UnityEditor.SerializedObject(_control);
        so.FindProperty("_buttons").arraySize = 3;
        so.FindProperty("_pages").arraySize = 3;
        for (int i = 0; i < 3; i++)
        {
            so.FindProperty("_buttons").GetArrayElementAtIndex(i).objectReferenceValue = _toggles[i];
            so.FindProperty("_pages").GetArrayElementAtIndex(i).objectReferenceValue = _pages[i];
        }
        so.ApplyModifiedProperties();

        _control.onValueChanged.AddListener(OnEventFired);
        _root.SetActive(true);
        return _control;
    }

    /// <summary>
    /// Probe handler: records the newest selection index.
    /// </summary>
    /// <param name="index">New selection index.</param>
    private void OnEventFired(int index)
    {
        _lastEvent = index;
        _eventFired = true;
    }

    [Test]
    public void Wake_DefaultsToFirstTabAndPage()
    {
        SegmentedControl control = CreateControl();

        Assert.AreEqual(0, control.SelectedIndex, "A fresh control selects tab 0.");
        Assert.IsTrue(_toggles[0].isOn, "First tab reflects the selection.");
        for (int i = 1; i < 3; i++)
        {
            Assert.IsFalse(_toggles[i].isOn, $"Tab {i} must start off.");
            Assert.IsFalse(_pages[i].activeSelf, $"Page {i} must start hidden.");
        }
        Assert.IsTrue(_pages[0].activeSelf, "Page 0 must start visible.");
    }

    [Test]
    public void ClickingAnotherTab_SwitchesSelectionAndPages()
    {
        SegmentedControl control = CreateControl();

        _toggles[1].isOn = true;

        Assert.AreEqual(1, control.SelectedIndex, "Selecting tab 1 moves the index.");
        for (int i = 0; i < 3; i++)
        {
            Assert.AreEqual(i == 1, _pages[i].activeSelf, $"Page {i} visibility must match the switch.");
        }
        Assert.IsTrue(_eventFired, "The switch raises the value-changed event.");
        Assert.AreEqual(1, _lastEvent, "The event carries the new index.");
    }

    [Test]
    public void Group_EnforcesSingleSelection()
    {
        SegmentedControl control = CreateControl();

        _toggles[2].isOn = true;
        _toggles[0].isOn = true;

        Assert.AreEqual(0, control.SelectedIndex);
        for (int i = 0; i < 3; i++)
        {
            Assert.AreEqual(i == 0, _toggles[i].isOn, $"Tab {i} must follow the exclusive switch.");
        }
    }

    [Test]
    public void SetSelected_RoutesThroughTheSamePath()
    {
        SegmentedControl control = CreateControl();

        control.SetSelected(2);

        Assert.AreEqual(2, control.SelectedIndex);
        Assert.IsTrue(_pages[2].activeSelf, "SetSelected shows page 2.");
        Assert.IsTrue(_toggles[2].isOn, "SetSelected turns tab 2 on (the group switches the rest off).");
        for (int i = 0; i < 3; i++)
        {
            if (i != 2)
            {
                Assert.IsFalse(_pages[i].activeSelf, $"Page {i} must be hidden after the switch.");
            }
        }
        Assert.IsTrue(_eventFired, "SetSelected raises the same event as a click.");
    }
}
