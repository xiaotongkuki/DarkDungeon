using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Play-mode tests for the pause panel: ESC freezes the clock and shows the
/// board, resume restores the clock and hides it, and the buttons route to
/// the same safe paths.
/// </summary>
public class PauseMenuTests
{
    private GameObject _root;

    [TearDown]
    public void TearDown()
    {
        // whatever the test did to the clock, the next run needs it running.
        Time.timeScale = 1f;
        if (_root != null)
        {
            Object.DestroyImmediate(_root);
        }
    }

    /// <summary>
    /// Builds an owner with its panel and two wired buttons, inactive first -
    /// the project arming order. Board hidden by the controller's own Awake.
    /// </summary>
    /// <returns>The enabled controller.</returns>
    private PauseMenuController CreateController()
    {
        _root = new GameObject("PauseRoot");
        _root.SetActive(false);

        var panel = new GameObject("Panel");
        panel.transform.SetParent(_root.transform, false);

        var resume = new GameObject("Resume", typeof(RectTransform));
        resume.transform.SetParent(panel.transform, false);
        UnityEngine.UI.Button resumeButton = resume.AddComponent<UnityEngine.UI.Button>();

        var menu = new GameObject("Menu", typeof(RectTransform));
        menu.transform.SetParent(panel.transform, false);
        UnityEngine.UI.Button menuButton = menu.AddComponent<UnityEngine.UI.Button>();

        PauseMenuController controller = _root.AddComponent<PauseMenuController>();
        var so = new SerializedObject(controller);
        so.FindProperty("_panel").objectReferenceValue = panel;
        so.FindProperty("_resumeButton").objectReferenceValue = resumeButton;
        so.FindProperty("_menuButton").objectReferenceValue = menuButton;
        so.ApplyModifiedProperties();

        _root.SetActive(true);
        return controller;
    }

    /// <summary>
    /// Fires the private ESC entry point directly: simulating the key through
    /// the input system would need a focused play window, but the contract
    /// under test ("what happens on ESC") is this method.
    /// </summary>
    /// <param name="controller">The enabled controller.</param>
    private static void FireEscape(PauseMenuController controller)
    {
        var method = typeof(PauseMenuController).GetMethod("OnEscapePerformed",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(controller, new object[1]
        {
            default(UnityEngine.InputSystem.InputAction.CallbackContext)
        });
    }

    [Test]
    public void Escape_FreezesAndShowsTheBoard()
    {
        PauseMenuController controller = CreateController();

        FireEscape(controller);

        Assert.AreEqual(0f, Time.timeScale, "Escape must freeze gameplay through the time scale.");
        Transform panel = controller.transform.Find("Panel");
        Assert.IsTrue(panel.gameObject.activeSelf, "The board must be visible while frozen.");
    }

    [Test]
    public void EscapeAgain_Resumes()
    {
        PauseMenuController controller = CreateController();
        FireEscape(controller);
        FireEscape(controller);

        Assert.AreEqual(1f, Time.timeScale, "A second ESC must hand the clock back.");
        Assert.IsFalse(controller.transform.Find("Panel").gameObject.activeSelf,
            "The board must hide again on resume.");
    }

    [Test]
    public void ResumeButton_RestoresTheClock()
    {
        PauseMenuController controller = CreateController();
        FireEscape(controller);

        Transform panel = controller.transform.Find("Panel");
        panel.Find("Resume").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();

        Assert.AreEqual(1f, Time.timeScale, "The wired resume button must unfreeze the game.");
        Assert.IsFalse(panel.gameObject.activeSelf, "The board must hide on resume.");
    }

    [Test]
    public void Disable_RestoresTheFrozenClock()
    {
        PauseMenuController controller = CreateController();
        FireEscape(controller);

        controller.enabled = false;

        Assert.AreEqual(1f, Time.timeScale, "Losing the controller (death, scene change) must not leave a frozen clock.");
    }
}
