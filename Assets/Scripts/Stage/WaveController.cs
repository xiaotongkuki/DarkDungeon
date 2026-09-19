using System.Collections;
using UnityEngine;

/// <summary>
/// Runs the endless wave loop: build a layout, hand out a fixed number of
/// enemies, wait for the field to be cleared, open the portal, and do it again
/// one wave harder.
///
/// A wave is finite by construction. The spawner is given a budget, so it stops
/// on its own once the wave's enemies exist; the wave ends when the field is
/// empty, which is what makes the portal a reward for finishing rather than a
/// timer. Difficulty comes from discrete tiers cycled by wave number, and the
/// wave's enemy count grows by a fixed step, so a run escalates predictably
/// without a formula to tune.
///
/// The switch to the next wave is an in-scene rebuild behind a fade rather than
/// a scene reload: the pools survive, the walls are untouched, and the known
/// time-scale traps of a reload never apply.
/// </summary>
public class WaveController : MonoBehaviour
{
    /// <summary>Progress of the active wave.</summary>
    private enum WaveState
    {
        /// <summary>No wave is running yet.</summary>
        Idle,
        /// <summary>Enemies are being handed out and killed.</summary>
        Fighting,
        /// <summary>The wave is cleared; the portal waits for the player.</summary>
        PortalOpen,
        /// <summary>The screen is covered and the next wave is being built.</summary>
        Transitioning
    }

    [Header("Progression")]
    [Tooltip("Enemies the first wave spawns.")]
    [SerializeField] private int _firstWaveEnemies = 10;
    [Tooltip("Extra enemies every following wave.")]
    [SerializeField] private int _enemiesPerWave = 5;
    [Tooltip("Waves each difficulty tier lasts before the next tier takes over.")]
    [SerializeField] private int _wavesPerTier = 5;
    [Tooltip("Difficulty tiers in order; the run cycles through them.")]
    [SerializeField] private WaveTierData[] _tiers;

    [Header("References")]
    [Tooltip("Builds the floor for each wave.")]
    [SerializeField] private TerrainGenerator _terrain;
    [Tooltip("Scatters cover and traps for each wave.")]
    [SerializeField] private PropPlacer _props;
    [Tooltip("Spawns the wave's enemies; accepts a tier and a budget.")]
    [SerializeField] private EnemySpawner _spawner;
    [Tooltip("Recycles leftover ground gems between waves.")]
    [SerializeField] private GemSpawner _gemSpawner;
    [Tooltip("Exit portal, shown when the wave is cleared.")]
    [SerializeField] private StagePortal _portal;
    [Tooltip("Full-screen fade that hides the rebuild.")]
    [SerializeField] private StageFade _fade;
    [Tooltip("Player transform, placed back on the map centre each wave.")]
    [SerializeField] private Transform _player;

    /// <summary>One-based number of the wave being played.</summary>
    private int _wave;

    /// <summary>Current progress of the wave.</summary>
    private WaveState _state;

    /// <summary>Seed for this run's layouts; every wave derives its own from it.</summary>
    private int _runSeed;

    /// <summary>Wave number the HUD should show.</summary>
    public int Wave => _wave;

    /// <summary>
    /// Rejects a controller with missing references, like every other
    /// configurable component: disabled-on-awake never reaches OnEnable, so a
    /// broken controller is inert and loud rather than half-running a wave.
    /// </summary>
    private void Awake()
    {
        if (_terrain == null || _props == null || _spawner == null || _gemSpawner == null ||
            _portal == null || _fade == null || _player == null)
        {
            Debug.LogError($"WaveController on '{name}' is missing a reference and will not run.", this);
            enabled = false;
            return;
        }

        if (_tiers == null || _tiers.Length == 0)
        {
            Debug.LogError($"WaveController on '{name}' has no difficulty tiers and will not run.", this);
            enabled = false;
            return;
        }

        _state = WaveState.Idle;
        HidePortal();
    }

    /// <summary>
    /// Starts the run on wave one. Done in Start so the bus subscribers (HUD,
    /// gem drops) are already listening for the wave announcement.
    /// </summary>
    private void Start()
    {
        _runSeed = Random.Range(int.MinValue, int.MaxValue);
        BeginWave(1);
    }

    /// <summary>
    /// Drops both subscriptions; the bus is static and outlives this scene.
    /// </summary>
    private void OnEnable()
    {
        EventBus.PortalEntered += OnPortalEntered;
    }

    /// <summary>
    /// Drops the subscription.
    /// </summary>
    private void OnDisable()
    {
        EventBus.PortalEntered -= OnPortalEntered;
    }

    /// <summary>
    /// Watches the field while fighting: the wave is cleared once the spawner
    /// has handed out its whole budget and nothing is left alive. Polling one
    /// registry count is cheaper and more robust than tracking kills, which
    /// would drift the moment an enemy died to something other than the player.
    /// </summary>
    private void Update()
    {
        if (_state != WaveState.Fighting)
        {
            return;
        }

        if (!_spawner.HasFinishedSpawning)
        {
            return;
        }

        EnemyManager manager = EnemyManager.Instance;
        if (manager == null || manager.ActiveCount == 0)
        {
            OpenPortal();
        }
    }

    /// <summary>
    /// Builds one wave: new layout, player back on the centre, spawner armed
    /// with the tier's table and the wave's budget, portal hidden.
    /// </summary>
    /// <param name="wave">One-based wave number to build.</param>
    private void BeginWave(int wave)
    {
        _wave = wave;

        int seed = _runSeed ^ (wave * 397);
        WaveTierData tier = TierFor(wave);
        _terrain.Generate(seed);
        _props.Place(seed, tier.ObstacleCount, tier.HazardCount);

        PlacePlayer();
        HidePortal();

        _spawner.ApplyWave(tier, BudgetFor(wave));
        _spawner.enabled = true;

        _state = WaveState.Fighting;
        EventBus.RaiseWaveStarted(wave);
    }

    /// <summary>
    /// Puts the player back on the map centre with no leftover speed, so a new
    /// wave opens from a still position rather than from a dive carried over the
    /// fade.
    /// </summary>
    private void PlacePlayer()
    {
        _player.position = new Vector3(0f, 0f, _player.position.z);
        if (_player.TryGetComponent(out Rigidbody2D body))
        {
            body.velocity = Vector2.zero;
        }
    }

    /// <summary>
    /// Hides and disarms the portal.
    /// </summary>
    private void HidePortal()
    {
        _portal.gameObject.SetActive(false);
        _portal.Disarm();
    }

    /// <summary>
    /// The wave's enemy count: a fixed first wave plus a fixed step per wave, so
    /// escalation is arithmetic a designer can predict.
    /// </summary>
    /// <param name="wave">One-based wave number.</param>
    /// <returns>Enemies to spawn this wave.</returns>
    private int BudgetFor(int wave)
    {
        return Mathf.Max(1, _firstWaveEnemies + (wave - 1) * _enemiesPerWave);
    }

    /// <summary>
    /// The tier for a wave: tiers last a fixed number of waves and the run
    /// cycles back to the first once the last has had its turn.
    /// </summary>
    /// <param name="wave">One-based wave number.</param>
    /// <returns>Tier asset to spawn from.</returns>
    private WaveTierData TierFor(int wave)
    {
        int perTier = Mathf.Max(1, _wavesPerTier);
        int index = ((wave - 1) / perTier) % _tiers.Length;
        return _tiers[index];
    }

    /// <summary>
    /// The field is clear: announce the wave and open the portal on the map
    /// centre.
    /// </summary>
    private void OpenPortal()
    {
        _state = WaveState.PortalOpen;
        _portal.transform.position = new Vector3(0f, 0f, _portal.transform.position.z);
        _portal.gameObject.SetActive(true);
        _portal.Arm();
        EventBus.RaiseWaveCleared(_wave);
    }

    /// <summary>
    /// Reacts to the portal: only a portal in the open state starts the next
    /// wave, so a stale event cannot skip one.
    /// </summary>
    private void OnPortalEntered()
    {
        if (_state != WaveState.PortalOpen)
        {
            return;
        }

        _state = WaveState.Transitioning;
        StartCoroutine(NextWave());
    }

    /// <summary>
    /// The rebuild behind the fade: cover, recycle leftover gems, build the next
    /// wave, reveal. Enemies need no sweeping here - a wave only ends when the
    /// field is empty - but leftover gems are swept so a player cannot bank the
    /// last wave's loot from the next one's floor.
    /// </summary>
    private IEnumerator NextWave()
    {
        _spawner.enabled = false;
        _portal.Disarm();
        _portal.gameObject.SetActive(false);

        yield return _fade.Cover();

        EnemyManager.Instance?.DespawnAll();
        _gemSpawner.ClearGroundGems();

        _state = WaveState.Idle;
        BeginWave(_wave + 1);

        yield return _fade.Reveal();
    }
}
