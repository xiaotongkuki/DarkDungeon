using UnityEngine;

/// <summary>
/// Settings panel: volume sliders persisted with PlayerPrefs plus a back
/// button. Opened from the start menu's settings button (and openable from the
/// pause menu later by assigning an open-button reference).
///
/// Follows the pause menu's panel pattern: the controller lives on the root
/// and stays enabled; the child <see cref="_panel"/> group (shade + board) is
/// what is toggled, so the controller never disables itself by hiding.
///
/// This panel owns persistence; <see cref="AudioBus"/> owns the live values.
/// Master volume is applied straight to <see cref="AudioListener.volume"/>, and
/// the music and effects sliders push into the bus, which is what a playing
/// track listens to.
/// </summary>
public class SettingsMenuController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Shade + board group toggled to show or hide the settings.")]
    [SerializeField] private GameObject _panel;
    [Tooltip("Optional button that opens the panel (start menu's settings).")]
    [SerializeField] private UnityEngine.UI.Button _openButton;
    [Tooltip("Button that closes the panel and returns to the caller.")]
    [SerializeField] private UnityEngine.UI.Button _backButton;
    [Tooltip("Optional master volume slider (0..1).")]
    [SerializeField] private UnityEngine.UI.Slider _masterSlider;
    [Tooltip("Optional background music volume slider (0..1).")]
    [SerializeField] private UnityEngine.UI.Slider _bgmSlider;
    [Tooltip("Optional sound effects volume slider (0..1).")]
    [SerializeField] private UnityEngine.UI.Slider _sfxSlider;

    private const string MasterVolumeKey = "settings.masterVolume";
    private const string BgmVolumeKey = "settings.bgmVolume";
    private const string SfxVolumeKey = "settings.sfxVolume";

    /// <summary>Volume applied when no saved value exists yet (1 = full).</summary>
    private const float DefaultVolume = 1f;

    /// <summary>
    /// Validates wiring, hides the panel, connects the back and optional open
    /// buttons and binds the sliders to their persistence paths. Sliders are
    /// pre-loaded from PlayerPrefs before any listener is attached so the
    /// initial assignment never writes a "default" value over the save.
    /// </summary>
    private void Awake()
    {
        if (_panel == null || _backButton == null)
        {
            Debug.LogError($"SettingsMenuController on '{name}' is missing its panel or back button and will not open.", this);
            enabled = false;
            return;
        }

        _backButton.onClick.AddListener(Close);
        if (_openButton != null)
        {
            _openButton.onClick.AddListener(Open);
        }

        if (_masterSlider != null)
        {
            _masterSlider.SetValueWithoutNotify(LoadVolume(MasterVolumeKey));
            ApplyMasterVolume();
            _masterSlider.onValueChanged.AddListener(OnMasterChanged);
        }

        if (_bgmSlider != null)
        {
            _bgmSlider.SetValueWithoutNotify(LoadVolume(BgmVolumeKey));
            AudioBus.SetBgmVolume(LoadVolume(BgmVolumeKey));
            _bgmSlider.onValueChanged.AddListener(OnBgmChanged);
        }

        if (_sfxSlider != null)
        {
            _sfxSlider.SetValueWithoutNotify(LoadVolume(SfxVolumeKey));
            AudioBus.SetSfxVolume(LoadVolume(SfxVolumeKey));
            _sfxSlider.onValueChanged.AddListener(OnSfxChanged);
        }

        _panel.SetActive(false);
    }

    /// <summary>
    /// Detaches listeners symmetrically; PlayerPrefs writes are final values,
    /// not subscriptions, so nothing needs to be undone there.
    /// </summary>
    private void OnDisable()
    {
        if (_backButton != null)
        {
            _backButton.onClick.RemoveListener(Close);
        }
        if (_openButton != null)
        {
            _openButton.onClick.RemoveListener(Open);
        }
        if (_masterSlider != null)
        {
            _masterSlider.onValueChanged.RemoveListener(OnMasterChanged);
        }
        if (_bgmSlider != null)
        {
            _bgmSlider.onValueChanged.RemoveListener(OnBgmChanged);
        }
        if (_sfxSlider != null)
        {
            _sfxSlider.onValueChanged.RemoveListener(OnSfxChanged);
        }
    }

    /// <summary>
    /// Shows the panel. Called by the open button's click; public so future
    /// callers (pause menu) can route the same path.
    /// </summary>
    public void Open()
    {
        _panel.SetActive(true);
    }

    /// <summary>
    /// Hides the panel; values stay persisted, so reopening restores them.
    /// </summary>
    public void Close()
    {
        _panel.SetActive(false);
    }

    /// <summary>
    /// Reads a saved volume from PlayerPrefs, defaulting to full blast.
    /// </summary>
    /// <param name="key">Preferences key.</param>
    /// <returns>Saved volume (0..1) or the default.</returns>
    private static float LoadVolume(string key)
    {
        return PlayerPrefs.GetFloat(key, DefaultVolume);
    }

    /// <summary>
    /// Persists the master slider and mirrors it to the global audio listener.
    /// </summary>
    /// <param name="value">Slider value (0..1).</param>
    private void OnMasterChanged(float value)
    {
        PlayerPrefs.SetFloat(MasterVolumeKey, value);
        ApplyMasterVolume();
    }

    /// <summary>
    /// Persists the BGM slider and pushes it to the audio bus, which a playing
    /// track listens to.
    /// </summary>
    /// <param name="value">Slider value (0..1).</param>
    private void OnBgmChanged(float value)
    {
        PlayerPrefs.SetFloat(BgmVolumeKey, value);
        AudioBus.SetBgmVolume(value);
    }

    /// <summary>
    /// Persists the SFX slider and pushes it to the audio bus, ready for the
    /// first effect that plays.
    /// </summary>
    /// <param name="value">Slider value (0..1).</param>
    private void OnSfxChanged(float value)
    {
        PlayerPrefs.SetFloat(SfxVolumeKey, value);
        AudioBus.SetSfxVolume(value);
    }

    /// <summary>
    /// Applies the saved master volume to the listener without touching the
    /// slider (avoids re-entrant change events).
    /// </summary>
    private void ApplyMasterVolume()
    {
        AudioListener.volume = LoadVolume(MasterVolumeKey);
    }

    // Exposed key names for tests and the future audio bus integration.
    public const string MasterKeyForTests = MasterVolumeKey;
    public const string BgmKeyForTests = BgmVolumeKey;
    public const string SfxKeyForTests = SfxVolumeKey;
}
