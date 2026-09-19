using UnityEngine;

/// <summary>
/// Plays the run's background music, continuously across scenes.
///
/// The track is one object that outlives every scene load, so the menu music
/// does not restart when the run begins - the alternative, one player per
/// scene, would cut the track at each load and need the scenes kept in sync
/// about which track belongs where.
///
/// Volume comes from <see cref="AudioBus"/> rather than the slider directly, so
/// the settings panel keeps owning persistence and this component keeps owning
/// playback; a change made mid-track is heard immediately. The master volume is
/// deliberately not applied here - <see cref="AudioListener.volume"/> already
/// scales everything downstream.
/// </summary>
public class MusicPlayer : MonoBehaviour
{
    /// <summary>The one player alive at a time; a duplicate scene-placed copy removes itself.</summary>
    private static MusicPlayer _instance;

    [Header("Track")]
    [Tooltip("Music to play. Required: without it there is nothing to loop.")]
    [SerializeField] private AudioClip _clip;
    [Tooltip("Extra scale on top of the settings' music volume, for balancing this track against future ones.")]
    [Range(0f, 2f)][SerializeField] private float _volumeScale = 1f;
    [Tooltip("Seconds the track takes to reach full volume; 0 starts at once.")]
    [SerializeField] private float _fadeInSeconds = 1f;

    /// <summary>Source the track plays through, created by this component.</summary>
    private AudioSource _source;

    /// <summary>Seconds of fade elapsed so far.</summary>
    private float _fadeElapsed;

    /// <summary>True while the fade-in is still running.</summary>
    private bool _fading;

    /// <summary>The track's target volume: the settings' music volume scaled for this clip.</summary>
    private float TargetVolume => AudioBus.BgmVolume * Mathf.Max(0f, _volumeScale);

    /// <summary>
    /// Rejects a player with no track, the same way the project's other
    /// configurable components do: disabled on awake means inert and loud rather
    /// than a silent scene.
    /// </summary>
    private void Awake()
    {
        if (_clip == null)
        {
            Debug.LogError($"MusicPlayer on '{name}' has no clip assigned and will play nothing.", this);
            enabled = false;
            return;
        }

        if (_instance != null && _instance != this)
        {
            // A second copy (a scene that was launched directly, or a duplicate
            // placed while the first is still alive) steps aside so two tracks
            // never overlap.
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        BuildSource();
    }

    /// <summary>
    /// Clears the singleton slot so a later scene - or a test - can start a new
    /// player, and stops the source on the way out.
    /// </summary>
    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    /// <summary>
    /// Follows volume changes made while the track plays, so the settings slider
    /// is heard at once.
    /// </summary>
    private void OnEnable()
    {
        AudioBus.BgmVolumeChanged += OnBgmVolumeChanged;
    }

    /// <summary>
    /// Drops the subscription; the bus is static and outlives this object.
    /// </summary>
    private void OnDisable()
    {
        AudioBus.BgmVolumeChanged -= OnBgmVolumeChanged;
    }

    /// <summary>
    /// Creates the source, starts the loop and begins the fade.
    /// </summary>
    private void BuildSource()
    {
        _source = gameObject.AddComponent<AudioSource>();
        _source.clip = _clip;
        _source.loop = true;
        _source.playOnAwake = false;
        _source.spatialBlend = 0f;
        _source.volume = _fadeInSeconds > 0f ? 0f : TargetVolume;

        _fadeElapsed = 0f;
        _fading = _fadeInSeconds > 0f;
        _source.Play();
    }

    /// <summary>
    /// Advances the fade-in. Unscaled time, so the music does not stall when the
    /// game pauses for the upgrade choice (timeScale 0) mid-fade.
    /// </summary>
    private void Update()
    {
        if (!_fading || _source == null)
        {
            return;
        }

        _fadeElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(_fadeElapsed / Mathf.Max(_fadeInSeconds, 0.0001f));
        _source.volume = TargetVolume * progress;

        if (progress >= 1f)
        {
            _fading = false;
            _source.volume = TargetVolume;
        }
    }

    /// <summary>
    /// Applies a music volume change to the playing track; while the fade is
    /// still running the target is simply remembered, and the fade picks it up.
    /// </summary>
    /// <param name="volume">New volume in 0..1.</param>
    private void OnBgmVolumeChanged(float volume)
    {
        if (_source == null || _fading)
        {
            return;
        }

        _source.volume = TargetVolume;
    }
}
