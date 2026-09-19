using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The game-over flow: listens for the player's death, shows a panel with the
/// final score, and restarts the run from a button.
///
/// It lives on an always-active object and only toggles the panel, because a
/// component on an inactive panel would never receive OnEnable and therefore could
/// never hear the death it exists to react to.
///
/// The panel is hidden in Awake rather than in the scene so it stays visible and
/// editable in the editor.
/// </summary>
public class GameOverController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Panel shown on death; hidden until then.")]
    [SerializeField] private GameObject _panel;
    [Tooltip("Label that shows the score the run ended with.")]
    [SerializeField] private Text _finalScoreText;
    [Tooltip("Button that starts a fresh run.")]
    [SerializeField] private Button _restartButton;

    [Header("Timing")]
    [Tooltip("Seconds between the death announcement and the panel appearing, left as room for the player's " +
             "death animation. The death announcement itself still fires instantly; only this panel waits. " +
             "0 or negative shows the panel on the event frame as before.")]
    [SerializeField] private float _showDelaySeconds = 1.45f;

    /// <summary>Format used for the final score; the placeholder is the score.</summary>
    private const string FinalScoreFormat = "最终击杀: {0}";

    /// <summary>Score the run ended with, kept from the last announcement.</summary>
    private int _score;

    /// <summary>
    /// Hides the panel, wires the restart button and rejects missing references.
    /// </summary>
    private void Awake()
    {
        if (_panel == null || _finalScoreText == null || _restartButton == null)
        {
            Debug.LogError($"GameOverController on '{name}' is missing a reference and will not show the panel.", this);
            enabled = false;
            return;
        }

        _panel.SetActive(false);
        _restartButton.onClick.AddListener(Restart);
    }

    /// <summary>
    /// Removes the button listener, so a destroyed controller cannot be called
    /// through a button that outlives it.
    /// </summary>
    private void OnDestroy()
    {
        if (_restartButton != null)
        {
            _restartButton.onClick.RemoveListener(Restart);
        }
    }

    /// <summary>
    /// Starts listening for the two facts this flow needs: the score, so the panel
    /// can report it, and the player's death, so the panel can appear.
    /// </summary>
    private void OnEnable()
    {
        if (_panel == null)
        {
            return;
        }

        EventBus.PlayerDied += OnPlayerDied;
        EventBus.ScoreChanged += OnScoreChanged;
    }

    /// <summary>
    /// Drops both subscriptions; the bus is static and outlives this scene.
    /// </summary>
    private void OnDisable()
    {
        EventBus.PlayerDied -= OnPlayerDied;
        EventBus.ScoreChanged -= OnScoreChanged;
    }

    /// <summary>
    /// Remembers the running score so the panel can report it later.
    /// </summary>
    /// <param name="score">Player's current total score.</param>
    private void OnScoreChanged(int score)
    {
        _score = score;
    }

    /// <summary>
    /// Shows the panel with the score the run ended on. The panel waits for the
    /// show delay first, so the death animation plays against an open screen; a
    /// zero delay keeps the same-frame behaviour for tests and headless setups.
    /// </summary>
    private void ShowFinal()
    {
        _finalScoreText.text = string.Format(FinalScoreFormat, _score);

        if (_showDelaySeconds > 0f)
        {
            StartCoroutine(ShowAfterDelay());
            return;
        }

        _panel.SetActive(true);
    }

    /// <summary>
    /// Stages the panel reveal on death with the score the run ended on. An
    /// endless run has no victory to celebrate: death is the only ending.
    /// </summary>
    private void OnPlayerDied()
    {
        ShowFinal();
    }

    /// <summary>
    /// Delayed half of the delayed reveal: fills nothing further, just flips the
    /// panel on. A controller disabled (or destroyed) by a scene change before the
    /// wait ends simply never shows its panel - the coroutine dies with the
    /// component, which is the correct outcome for a scene that is already gone.
    /// </summary>
    private System.Collections.IEnumerator ShowAfterDelay()
    {
        yield return new WaitForSeconds(_showDelaySeconds);
        _panel.SetActive(true);
    }

    /// <summary>
    /// Starts a fresh run by reloading the current scene. Public so a button can
    /// call it from the Inspector as well as from the runtime listener.
    ///
    /// The time scale is restored first: it is a global setting that a scene reload
    /// does not reset, so restarting out of a paused state (the level-up choice
    /// panel pauses the run) would otherwise reload into a frozen game.
    /// </summary>
    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
