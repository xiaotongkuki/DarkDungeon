using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The ESC pause menu: a wooden panel over the frozen game with "继续游戏" and
/// "回到主菜单".
///
/// Idle-driven pause games through <see cref="Time.timeScale"/>, which stops
/// movement, firing, spawning and every other gameplay loop that scales with
/// game time. UI (canvas rendering, button clicks) keeps running, so the
/// panel stays fully interactive while the world is frozen; the input system
/// itself is frame-rate driven and also keeps firing, which is how ESC
/// unpacks the pause again.
///
/// The panel is hidden in Awake but the component stays enabled, so it is
/// always listening for ESC - the project keeps panels visible-and-toggled
/// rather than dead objects.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Root of the motion panel shown while the game is paused.")]
    [SerializeField] private GameObject _panel;
    [Tooltip("Button that resumes the game.")]
    [SerializeField] private UnityEngine.UI.Button _resumeButton;
    [Tooltip("Button that returns to the main menu.")]
    [SerializeField] private UnityEngine.UI.Button _menuButton;
    [Tooltip("Optional button that opens the settings panel.")]
    [SerializeField] private UnityEngine.UI.Button _settingsButton;
    [Tooltip("Optional settings menu in the same scene; closed whenever the pause resolves.")]
    [SerializeField] private SettingsMenuController _settingsPanel;

    private const int StartSceneBuildIndex = 0;

    private PlayerControls _controls;
    private bool _paused;

    /// <summary>
    /// Validates wiring and connects the two buttons' clicks to the resume and
    /// menu paths, so the prefab is self-contained: nothing outside needs to
    /// remember to hook OnClick.
    /// </summary>
    private void Awake()
    {
        if (_panel == null || _resumeButton == null || _menuButton == null)
        {
            Debug.LogError($"PauseMenuController on '{name}' is missing panel or button references and will not respond to ESC.", this);
            enabled = false;
            return;
        }

        _resumeButton.onClick.AddListener(Resume);
        _menuButton.onClick.AddListener(BackToMenu);
        if (_settingsButton != null)
        {
            _settingsButton.onClick.AddListener(OpenSettings);
        }
        _panel.SetActive(false);
    }

    /// <summary>
    /// Owns a dedicated controls wrapper: each component instantiates its own
    /// (the asset is read-only), enabling again in OnEnable and dropping it in
    /// OnDisable, the same symmetry the input reader uses.
    /// </summary>
    private void OnEnable()
    {
        _controls = new PlayerControls();
        _controls.Gameplay.Escape.performed += OnEscapePerformed;
        _controls.Enable();
    }

    /// <summary>
    /// Drops the input wrapper; a disabled component must never leave the ESC
    /// hook alive.
    /// </summary>
    private void OnDisable()
    {
        _controls.Gameplay.Escape.performed -= OnEscapePerformed;
        _controls.Disable();
        _controls.Dispose();
        _controls = null;

        // A component disabled in the middle of the freeze (death, scene
        // switch) must never leave the next loop starved of time.
        if (_paused)
        {
            _paused = false;
            Time.timeScale = 1f;
        }
    }    /// <summary>
    /// Defensive restore: if the scene (or the play session) ends while
    /// paused, the next gameplay must not inherit a zero clock - the
    /// timeScale trap the project documented for scene reloads.
    /// </summary>
    private void OnDestroy()
    {
        Time.timeScale = 1f;
    }

    /// <summary>
    /// Toggles the pause on the ESC key; a second presses while paused
    /// resolves to a resume, which is the behaviour players expect.
    /// </summary>
    /// <param name="context">Supplied by the input system; unused.</param>
    private void OnEscapePerformed(InputAction.CallbackContext context)
    {
        if (_paused)
        {
            Resume();
        }
        else
        {
            Pause();
        }
    }

    /// <summary>
    /// Freezes gameplay and shows the panel.
    /// </summary>
    private void Pause()
    {
        _paused = true;
        Time.timeScale = 0f;
        _panel.SetActive(true);
    }

    /// <summary>
    /// Restores the clock and hides the panel. Invoked by the button's click
    /// as well, so the resumed state never differs from a fresh enable. Any
    /// open settings overlay is closed with the pause.
    /// </summary>
    private void Resume()
    {
        _paused = false;
        Time.timeScale = 1f;
        if (_settingsPanel != null)
        {
            _settingsPanel.Close();
        }
        _panel.SetActive(false);
    }

    /// <summary>
    /// Opens the settings panel over the frozen game; closing stays with the
    /// settings controller itself (its back button) or the next resolve.
    /// </summary>
    private void OpenSettings()
    {
        if (_settingsPanel != null)
        {
            _settingsPanel.Open();
        }
    }

    /// <summary>
    /// Loads the start menu after restoring the timescale, because a scene
    /// reload does not reset the clock and would otherwise strand the next
    /// run frozen.
    /// </summary>
    private void BackToMenu()
    {
        Time.timeScale = 1f;
        _paused = false;
        UnityEngine.SceneManagement.SceneManager.LoadScene(StartSceneBuildIndex);
    }
}
