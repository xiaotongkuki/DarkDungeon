using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Play-mode tests for the game-over flow: the panel stays hidden until the player
/// dies, then reports the score the run ended with.
/// </summary>
public class GameOverControllerTests
{
    private GameObject _flowObject;
    private GameObject _panelObject;
    private GameObject _finalScoreObject;
    private GameObject _buttonObject;

    [TearDown]
    public void TearDown()
    {
        if (_flowObject != null)
        {
            Object.DestroyImmediate(_flowObject);
        }
        if (_panelObject != null)
        {
            Object.DestroyImmediate(_panelObject);
        }
        if (_finalScoreObject != null)
        {
            Object.DestroyImmediate(_finalScoreObject);
        }
        if (_buttonObject != null)
        {
            Object.DestroyImmediate(_buttonObject);
        }
    }

    /// <summary>
    /// Yields until the given amount of game time has passed, regardless of how
    /// many frames that takes on the current machine and editor focus state.
    /// </summary>
    /// <param name="seconds">Game-time duration to wait for.</param>
    private static IEnumerator WaitForGameTime(float seconds)
    {
        float deadline = Time.time + seconds;
        while (Time.time < deadline)
        {
            yield return null;
        }
    }

    /// <summary>
    /// Builds the flow with a panel, a final-score label and a restart button.
    ///
    /// Built inactive so the references can be assigned before Awake runs, which is
    /// where the controller validates them and hides the panel.
    /// </summary>
    /// <returns>The final-score label the panel writes into.</returns>
    private Text CreateFlow()
    {
        _flowObject = new GameObject("GameFlow", typeof(RectTransform));
        _flowObject.SetActive(false);

        _panelObject = new GameObject("Panel", typeof(RectTransform));
        _panelObject.transform.SetParent(_flowObject.transform);

        _finalScoreObject = new GameObject("FinalScore", typeof(RectTransform));
        _finalScoreObject.transform.SetParent(_panelObject.transform);
        Text finalScore = _finalScoreObject.AddComponent<Text>();

        _buttonObject = new GameObject("RestartButton", typeof(RectTransform));
        _buttonObject.transform.SetParent(_panelObject.transform);
        Button restart = _buttonObject.AddComponent<Button>();

        GameOverController controller = _flowObject.AddComponent<GameOverController>();
        TestData.SetObjectReference(controller, "_panel", _panelObject);
        TestData.SetObjectReference(controller, "_finalScoreText", finalScore);
        TestData.SetObjectReference(controller, "_restartButton", restart);
        // These two fixtures assert the legacy same-frame reveal; the production
        // delay waits for the player's death animation, so tests opt out here and
        // exercise the delayed path explicitly in the dedicated fixture below.
        TestData.SetFloat(controller, "_showDelaySeconds", 0f);

        _flowObject.SetActive(true);
        return finalScore;
    }

    [Test]
    public void PanelStaysHiddenUntilThePlayerDies()
    {
        Text finalScore = CreateFlow();

        Assert.IsFalse(_panelObject.activeSelf, "The panel must be hidden while the run is alive.");

        EventBus.RaiseScoreChanged(5);
        EventBus.RaisePlayerDied();

        Assert.IsTrue(_panelObject.activeSelf, "The player's death must bring the panel up.");
        Assert.AreEqual("最终击杀: 5", finalScore.text,
            "The panel must report the score the run ended with.");
    }

    [Test]
    public void PanelReportsZeroWhenNothingWasKilled()
    {
        Text finalScore = CreateFlow();

        EventBus.RaisePlayerDied();

        Assert.AreEqual("最终击杀: 0", finalScore.text,
            "A run that scored nothing must still report a number.");
    }

    [UnityTest]
    public IEnumerator PanelWaitsForTheDeathAnimationBeforeShowing()
    {
        CreateFlow();
        TestData.SetFloat(_flowObject.GetComponent<GameOverController>(), "_showDelaySeconds", 0.2f);

        EventBus.RaisePlayerDied();

        Assert.IsFalse(_panelObject.activeSelf,
            "The panel must stay down during the player's death animation window.");

        yield return WaitForGameTime(0.35f);

        Assert.IsTrue(_panelObject.activeSelf,
            "After the animation window the panel must come up without any further event.");
    }
}
