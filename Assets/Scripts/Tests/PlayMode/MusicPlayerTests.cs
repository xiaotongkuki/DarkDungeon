using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the music player: it builds a looping 2D source, plays
/// at the volume the audio bus reports, follows changes made while playing, and
/// refuses to run without a track. A generated clip is used instead of the real
/// asset so the tests do not depend on the project's music file.
/// </summary>
public class MusicPlayerTests
{
    private GameObject _playerObject;
    private AudioClip _clip;

    /// <summary>
    /// Builds a short generated clip and a player object, with the bus left at a
    /// known volume.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        PlayerPrefs.DeleteKey(AudioBus.BgmVolumeKey);
        PlayerPrefs.DeleteKey(AudioBus.SfxVolumeKey);
        AudioBus.ReloadFromPreferences();

        // A second of silence: enough to be a valid clip, cheap to hold.
        _clip = AudioClip.Create("TestMusic", 44100, 1, 44100, false);

        _playerObject = new GameObject("MusicPlayer");
        _playerObject.SetActive(false);
        MusicPlayer player = _playerObject.AddComponent<MusicPlayer>();
        SetRef(player, "_clip", _clip);
        SetFloat(player, "_fadeInSeconds", 0f);
        _playerObject.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_playerObject);
        Object.DestroyImmediate(_clip);
        PlayerPrefs.DeleteKey(AudioBus.BgmVolumeKey);
        PlayerPrefs.DeleteKey(AudioBus.SfxVolumeKey);
        AudioBus.ReloadFromPreferences();
    }

    [UnityTest]
    public IEnumerator Player_BuildsALoopingSourceAtTheBusVolume()
    {
        yield return null;

        AudioSource source = _playerObject.GetComponent<AudioSource>();
        Assert.IsNotNull(source, "The player must create its own source.");
        Assert.IsTrue(source.loop, "Background music must loop.");
        Assert.AreEqual(0f, source.spatialBlend, 0.001f, "Music is not positional.");
        Assert.AreEqual(1f, source.volume, 0.01f, "Full bus volume plays at full volume.");
    }

    [UnityTest]
    public IEnumerator Player_FollowsVolumeChangesWhilePlaying()
    {
        yield return null;

        AudioSource source = _playerObject.GetComponent<AudioSource>();
        Assert.IsNotNull(source);

        AudioBus.SetBgmVolume(0.4f);
        yield return null;

        Assert.AreEqual(0.4f, source.volume, 0.01f, "A slider change must reach the playing track at once.");
    }

    [UnityTest]
    public IEnumerator Player_AppliesTheSavedVolumeOnStart()
    {
        // Rebuild the player with a saved volume in place.
        Object.DestroyImmediate(_playerObject);
        PlayerPrefs.SetFloat(AudioBus.BgmVolumeKey, 0.3f);
        AudioBus.ReloadFromPreferences();

        _playerObject = new GameObject("MusicPlayer");
        _playerObject.SetActive(false);
        MusicPlayer player = _playerObject.AddComponent<MusicPlayer>();
        SetRef(player, "_clip", _clip);
        SetFloat(player, "_fadeInSeconds", 0f);
        _playerObject.SetActive(true);

        yield return null;

        AudioSource source = _playerObject.GetComponent<AudioSource>();
        Assert.IsNotNull(source);
        Assert.AreEqual(0.3f, source.volume, 0.01f, "The saved music volume must be honoured on start.");
    }

    [UnityTest]
    public IEnumerator Player_VolumeScaleBalancesTheTrack()
    {
        Object.DestroyImmediate(_playerObject);

        _playerObject = new GameObject("MusicPlayer");
        _playerObject.SetActive(false);
        MusicPlayer player = _playerObject.AddComponent<MusicPlayer>();
        SetRef(player, "_clip", _clip);
        SetFloat(player, "_fadeInSeconds", 0f);
        SetFloat(player, "_volumeScale", 0.5f);
        _playerObject.SetActive(true);

        yield return null;

        AudioSource source = _playerObject.GetComponent<AudioSource>();
        Assert.IsNotNull(source);
        Assert.AreEqual(0.5f, source.volume, 0.01f, "The per-track scale multiplies the settings volume.");
    }

    [UnityTest]
    public IEnumerator Player_SurvivesASceneLoad()
    {
        yield return null;

        // DontDestroyOnLoad is what keeps the track running across a scene
        // change; the object must report itself as no longer belonging to the
        // active scene.
        Assert.IsTrue(_playerObject.scene.name == "DontDestroyOnLoad" ||
                      _playerObject.scene.name != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            "The player must outlive its scene, or the music would stop on every load.");
    }

    [Test]
    public void Player_WithoutAClip_IsInert()
    {
        GameObject bare = new GameObject("BareMusicPlayer");
        bare.SetActive(false);
        bare.AddComponent<MusicPlayer>();
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no clip assigned"));
        bare.SetActive(true);

        Assert.IsNull(bare.GetComponent<AudioSource>(), "A player with no track must not build a source.");
        Object.DestroyImmediate(bare);
    }

    /// <summary>Writes a serialized object reference.</summary>
    /// <param name="target">Object owning the field.</param>
    /// <param name="fieldName">Serialized field name.</param>
    /// <param name="value">Reference to assign.</param>
    private static void SetRef(Object target, string fieldName, Object value)
    {
        UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(target);
        so.FindProperty(fieldName).objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }

    /// <summary>Writes a serialized float field.</summary>
    /// <param name="target">Object owning the field.</param>
    /// <param name="fieldName">Serialized field name.</param>
    /// <param name="value">Value to assign.</param>
    private static void SetFloat(Object target, string fieldName, float value)
    {
        UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(target);
        so.FindProperty(fieldName).floatValue = value;
        so.ApplyModifiedProperties();
    }
}
