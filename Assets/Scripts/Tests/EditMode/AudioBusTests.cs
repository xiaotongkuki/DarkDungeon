using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Covers the audio bus: the saved volumes it hands out, the clamping and
/// no-op-on-unchanged rules, and the change announcements a playing track
/// follows. The bus is static, so every test starts from a known state and a
/// known set of preferences.
/// </summary>
public class AudioBusTests
{
    /// <summary>
    /// Clears the saved volumes and resynchronises the bus, so the fixture's
    /// static state and the preferences cannot leak between tests.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        PlayerPrefs.DeleteKey(AudioBus.BgmVolumeKey);
        PlayerPrefs.DeleteKey(AudioBus.SfxVolumeKey);
        AudioBus.ReloadFromPreferences();
    }

    [TearDown]
    public void TearDown()
    {
        PlayerPrefs.DeleteKey(AudioBus.BgmVolumeKey);
        PlayerPrefs.DeleteKey(AudioBus.SfxVolumeKey);
        AudioBus.ReloadFromPreferences();
    }

    [Test]
    public void WithoutSavedValues_BothVolumesAreFull()
    {
        Assert.AreEqual(1f, AudioBus.BgmVolume, 0.001f);
        Assert.AreEqual(1f, AudioBus.SfxVolume, 0.001f);
    }

    [Test]
    public void ReloadFromPreferences_PicksUpSavedValues()
    {
        PlayerPrefs.SetFloat(AudioBus.BgmVolumeKey, 0.35f);
        PlayerPrefs.SetFloat(AudioBus.SfxVolumeKey, 0.6f);

        AudioBus.ReloadFromPreferences();

        Assert.AreEqual(0.35f, AudioBus.BgmVolume, 0.001f);
        Assert.AreEqual(0.6f, AudioBus.SfxVolume, 0.001f);
    }

    [Test]
    public void ReloadFromPreferences_ClampsNonsenseValues()
    {
        PlayerPrefs.SetFloat(AudioBus.BgmVolumeKey, 4f);
        PlayerPrefs.SetFloat(AudioBus.SfxVolumeKey, -2f);

        AudioBus.ReloadFromPreferences();

        Assert.AreEqual(1f, AudioBus.BgmVolume, 0.001f, "A saved value above one clamps down.");
        Assert.AreEqual(0f, AudioBus.SfxVolume, 0.001f, "A saved value below zero clamps up.");
    }

    [Test]
    public void SetBgmVolume_AnnouncesTheNewValue()
    {
        float heard = -1f;
        System.Action<float> listener = value => heard = value;
        AudioBus.BgmVolumeChanged += listener;
        try
        {
            AudioBus.SetBgmVolume(0.25f);
        }
        finally
        {
            AudioBus.BgmVolumeChanged -= listener;
        }

        Assert.AreEqual(0.25f, heard, 0.001f);
        Assert.AreEqual(0.25f, AudioBus.BgmVolume, 0.001f);
    }

    [Test]
    public void SetBgmVolume_SameValueAgain_StaysSilent()
    {
        AudioBus.SetBgmVolume(0.5f);

        int calls = 0;
        System.Action<float> listener = value => calls++;
        AudioBus.BgmVolumeChanged += listener;
        try
        {
            AudioBus.SetBgmVolume(0.5f);
        }
        finally
        {
            AudioBus.BgmVolumeChanged -= listener;
        }

        Assert.AreEqual(0, calls, "Re-setting the same volume must not spam subscribers.");
    }

    [Test]
    public void SetBgmVolume_ClampsWhatItStoresAndAnnounces()
    {
        // Start from a value the clamp will actually move away from: setting a
        // value that is already current is deliberately silent.
        AudioBus.SetBgmVolume(0.5f);

        float heard = -1f;
        System.Action<float> listener = value => heard = value;
        AudioBus.BgmVolumeChanged += listener;
        try
        {
            AudioBus.SetBgmVolume(3f);
        }
        finally
        {
            AudioBus.BgmVolumeChanged -= listener;
        }

        Assert.AreEqual(1f, heard, 0.001f, "An oversized value announces as the clamped one.");
        Assert.AreEqual(1f, AudioBus.BgmVolume, 0.001f);
    }

    [Test]
    public void SetSfxVolume_AnnouncesIndependentlyOfTheMusic()
    {
        int bgmCalls = 0;
        int sfxCalls = 0;
        System.Action<float> bgm = value => bgmCalls++;
        System.Action<float> sfx = value => sfxCalls++;
        AudioBus.BgmVolumeChanged += bgm;
        AudioBus.SfxVolumeChanged += sfx;
        try
        {
            AudioBus.SetSfxVolume(0.1f);
        }
        finally
        {
            AudioBus.BgmVolumeChanged -= bgm;
            AudioBus.SfxVolumeChanged -= sfx;
        }

        Assert.AreEqual(1, sfxCalls, "The effects volume announces on its own channel.");
        Assert.AreEqual(0, bgmCalls, "Changing effects must not disturb the music.");
        Assert.AreEqual(0.1f, AudioBus.SfxVolume, 0.001f);
    }

    [Test]
    public void ReloadFromPreferences_StaysSilent()
    {
        int calls = 0;
        System.Action<float> listener = value => calls++;
        AudioBus.BgmVolumeChanged += listener;
        try
        {
            PlayerPrefs.SetFloat(AudioBus.BgmVolumeKey, 0.2f);
            AudioBus.ReloadFromPreferences();
        }
        finally
        {
            AudioBus.BgmVolumeChanged -= listener;
        }

        Assert.AreEqual(0, calls, "A resynchronisation is not a user action and must not announce.");
        Assert.AreEqual(0.2f, AudioBus.BgmVolume, 0.001f);
    }
}
