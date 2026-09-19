using System;
using UnityEngine;

/// <summary>
/// The one place runtime audio reads its volume from.
///
/// The settings panel has always persisted three volumes to PlayerPrefs, but
/// only the master one had somewhere to go (<see cref="AudioListener.volume"/>);
/// the music and effects values were written and never read, because no audio
/// existed yet. This bus is that missing consumer: it loads the saved values
/// once, hands them out to whoever plays sound, and announces changes so a
/// playing track follows the slider immediately instead of at the next restart.
///
/// Static, like the event bus: volumes are global, hold no scene objects, and
/// must survive a scene change - the music does.
/// </summary>
public static class AudioBus
{
    /// <summary>Preferences key for the background music volume.</summary>
    public const string BgmVolumeKey = "settings.bgmVolume";

    /// <summary>Preferences key for the sound effects volume.</summary>
    public const string SfxVolumeKey = "settings.sfxVolume";

    /// <summary>Volume used when nothing has been saved yet (full).</summary>
    public const float DefaultVolume = 1f;

    /// <summary>Raised after the music volume changes, carrying the new value in 0..1.</summary>
    public static event Action<float> BgmVolumeChanged;

    /// <summary>Raised after the effects volume changes, carrying the new value in 0..1.</summary>
    public static event Action<float> SfxVolumeChanged;

    private static float _bgmVolume = ReadSaved(BgmVolumeKey);
    private static float _sfxVolume = ReadSaved(SfxVolumeKey);

    /// <summary>Current background music volume, 0..1.</summary>
    public static float BgmVolume => _bgmVolume;

    /// <summary>Current sound effects volume, 0..1.</summary>
    public static float SfxVolume => _sfxVolume;

    /// <summary>
    /// Sets the music volume and announces it. Values are clamped, and a value
    /// equal to the current one is ignored so a slider dragged back and forth
    /// cannot spam subscribers.
    /// </summary>
    /// <param name="value">Volume in 0..1.</param>
    public static void SetBgmVolume(float value)
    {
        float clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(clamped, _bgmVolume))
        {
            return;
        }

        _bgmVolume = clamped;
        BgmVolumeChanged?.Invoke(clamped);
    }

    /// <summary>
    /// Sets the effects volume and announces it, with the same clamping and
    /// no-op-on-unchanged behaviour as the music side.
    /// </summary>
    /// <param name="value">Volume in 0..1.</param>
    public static void SetSfxVolume(float value)
    {
        float clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(clamped, _sfxVolume))
        {
            return;
        }

        _sfxVolume = clamped;
        SfxVolumeChanged?.Invoke(clamped);
    }

    /// <summary>
    /// Re-reads both volumes from PlayerPrefs without announcing anything.
    ///
    /// Called when a settings panel opens so a value edited elsewhere is picked
    /// up, and by tests, whose fixture reuse would otherwise leak the previous
    /// test's volumes into the next one. It stays silent on purpose: this is a
    /// resynchronisation, not a user action, and subscribers re-reading the
    /// property is what they should do.
    /// </summary>
    public static void ReloadFromPreferences()
    {
        _bgmVolume = ReadSaved(BgmVolumeKey);
        _sfxVolume = ReadSaved(SfxVolumeKey);
    }

    /// <summary>
    /// Reads one saved volume, defaulting to full when the key is absent.
    /// </summary>
    /// <param name="key">Preferences key.</param>
    /// <returns>Saved volume in 0..1.</returns>
    private static float ReadSaved(string key)
    {
        return Mathf.Clamp01(PlayerPrefs.GetFloat(key, DefaultVolume));
    }
}
