using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns enemies on a timer at points just outside the camera view, so the
/// player always has something to fight without enemies popping in on screen.
///
/// Which enemy appears is a weighted list of prefabs rather than a single one, so
/// adding a monster type is a matter of dropping a prefab into the array and
/// giving it a weight - no code change. Each prefab carries its own balance values
/// through its <see cref="EnemyData"/> asset.
///
/// Cadence is a single serialized interval: lowering it later is the intended
/// difficulty curve, and because the timer accumulates real delta time the rate
/// stays correct regardless of frame rate.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    /// <summary>Parked-enemy capacity hint per type; each pool still grows to meet demand.</summary>
    private const int PoolCapacity = 64;

    /// <summary>Most enemies kept parked per type; beyond it a dead enemy is destroyed instead.</summary>
    private const int PoolMaxSize = 512;

    /// <summary>
    /// One enemy type in the spawn table: the prefab to instantiate and its share
    /// of the spawns relative to the other entries.
    ///
    /// Each entry owns the pool its enemies come from, so one type's cap and
    /// container stay independent of the others'. The pool is a plain field rather
    /// than a serialized one because it holds live scene objects: it must not be
    /// copied by the Inspector or survive a reload.
    /// </summary>
    [Serializable]
    private class SpawnEntry
    {
        [Tooltip("Enemy prefab to instantiate.")]
        [SerializeField] private Enemy _prefab;
        [Tooltip("Relative share of spawns: a weight of 2 against a weight of 1 makes this type appear twice as often.")]
        [SerializeField] private float _weight = 1f;

        /// <summary>Pool this entry's enemies come from, created on first use.</summary>
        private ComponentPool<Enemy> _pool;

        /// <summary>Parent of this entry's parked enemies.</summary>
        private Transform _container;

        /// <summary>Enemy prefab to instantiate.</summary>
        public Enemy Prefab => _prefab;

        /// <summary>Relative share of spawns; non-positive weights are never picked.</summary>
        public float Weight => _weight;

        /// <summary>
        /// Returns this entry's pool, building it on first use under the given owner.
        ///
        /// Built lazily rather than in <c>Awake</c> because the table can be filled in
        /// after the spawner wakes - which is what the tests do - and because a prefab
        /// can be reassigned in the Inspector. The trade-off is that a prefab swapped
        /// while the game runs keeps serving the pool built for the old one.
        /// </summary>
        /// <param name="owner">Component the pool's container is parented to.</param>
        /// <param name="capacity">Capacity hint for the parked stack.</param>
        /// <param name="maxSize">Most enemies kept parked.</param>
        /// <returns>The pool for this entry.</returns>
        public ComponentPool<Enemy> PoolUnder(Transform owner, int capacity, int maxSize)
        {
            if (_pool == null)
            {
                _container = new GameObject($"Pool_{_prefab.name}").transform;
                _container.SetParent(owner, worldPositionStays: false);
                _pool = new ComponentPool<Enemy>(_prefab, _container, capacity, maxSize);
            }

            return _pool;
        }
    }

    [Header("Spawning")]
    [Tooltip("Enemy types to spawn and their relative weights. An empty list spawns nothing.")]
    [SerializeField] private SpawnEntry[] _spawnEntries;
    [Tooltip("Seconds between spawns. Lower it to ramp difficulty up.")]
    [SerializeField] private float _spawnInterval = 0.5f;
    [Tooltip("Hard cap on live enemies; spawning pauses while the registry is at or above it.")]
    [SerializeField] private int _maxAlive = 300;

    [Header("Placement")]
    [Tooltip("How far outside the viewport enemies appear, as a fraction of the viewport size per axis.")]
    [SerializeField] private float _viewportMargin = 0.15f;
    [Tooltip("World z of the gameplay plane, used to project the viewport correctly.")]
    [SerializeField] private float _spawnPlaneZ = 0f;

    /// <summary>Units the wave mode keeps a spawn away from every wall, so nothing lands inside a wall.</summary>
    private const float WaveSpawnInset = 0.5f;

    /// <summary>
    /// The difficulty tier currently applied, or null for the spawner's own
    /// serialized table. Assigned through <see cref="ApplyWave"/>; the serialized
    /// fields remain the fallback so bare scenes and tests behave as before.
    /// </summary>
    private WaveTierData _tier;

    /// <summary>
    /// Enemies left to spawn this wave. Negative means "no budget" - the
    /// spawner keeps producing on its cadence, which is the old endless
    /// behaviour used by bare scenes and the spawner's own tests.
    /// </summary>
    private int _spawnBudget = -1;

    /// <summary>
    /// Pools for wave-mode spawns, one per enemy prefab. Prefabs never move
    /// pools - a tier's table changes between prefabs, it never swaps a prefab
    /// under an existing pool - so a dictionary key is a stable pool identity.
    /// </summary>
    private readonly Dictionary<Enemy, ComponentPool<Enemy>> _tierPools =
        new Dictionary<Enemy, ComponentPool<Enemy>>();

    /// <summary>Seconds between spawns while a tier drives the spawner.</summary>
    private float TierInterval =>
        _tier != null && _tier.SpawnInterval > 0f ? _tier.SpawnInterval : _spawnInterval;

    /// <summary>
    /// Live enemy cap while a tier drives the spawner; a tier cap of zero falls
    /// back to the spawner's own cap.
    /// </summary>
    private int TierMaxAlive => _tier != null && _tier.MaxAlive > 0 ? _tier.MaxAlive : _maxAlive;

    /// <summary>
    /// Enemies still to spawn this wave; -1 when the spawner runs without a
    /// budget.
    /// </summary>
    public int SpawnBudgetRemaining => _spawnBudget;

    /// <summary>
    /// True when the wave's whole spawn budget has been handed out, which is the
    /// cue for the wave controller to wait for the field to be cleared.
    /// </summary>
    public bool HasFinishedSpawning => _spawnBudget == 0;

    /// <summary>
    /// Applies a difficulty tier and the wave's spawn budget, and drops the
    /// spawn lattice timer so the new cadence starts clean.
    ///
    /// The budget is what makes a wave finite: the spawner produces exactly this
    /// many enemies and then stops, so clearing the field is a real objective
    /// rather than a race against an endless stream. No pool is touched: pools
    /// are per prefab and created lazily, so a new tier's types build their own
    /// pools on first use and unchanged types keep the pool they already had.
    /// </summary>
    /// <param name="tier">Difficulty tier to apply; null returns the spawner to its serialized table.</param>
    /// <param name="budget">Enemies to spawn this wave; negative keeps spawning forever.</param>
    public void ApplyWave(WaveTierData tier, int budget)
    {
        _tier = tier;

        // A tier with nothing to spawn has a spent budget from the start: the
        // wave controller can then clear the wave immediately instead of waiting
        // on a spawner that will never hand anything out.
        bool emptyTable = tier != null && (tier.SpawnEntries == null || tier.SpawnEntries.Length == 0);
        _spawnBudget = emptyTable ? 0 : budget;
        _timer = 0f;
    }

    /// <summary>
    /// Upper bound on spawns per frame. Without it a tiny interval, or a huge
    /// delta time after a stall, would spin the catch-up loop and freeze the
    /// editor; the surplus backlog is dropped instead.
    /// </summary>
    private const int MaxSpawnsPerFrame = 16;

    private Camera _camera;
    private EnemyManager _manager;
    private float _timer;

    /// <summary>
    /// Caches the camera once. Camera.main is a tagged lookup, so it must never
    /// run per spawn.
    ///
    /// The manager is deliberately NOT cached here: when the spawner and the
    /// manager live on the same GameObject, Unity does not guarantee which Awake
    /// runs first, so reading EnemyManager.Instance here can legally observe null
    /// and would then stay null forever. It is resolved in <see cref="Update"/>
    /// instead.
    /// </summary>
    private void Awake()
    {
        _camera = Camera.main;
    }

    /// <summary>
    /// Fills in references that were not available yet when Awake ran. Cheap
    /// enough for every frame: both lookups only happen while a field is still
    /// unset.
    /// </summary>
    private void EnsureReferences()
    {
        if (_camera == null)
        {
            _camera = Camera.main;
        }
        if (_manager == null)
        {
            _manager = EnemyManager.Instance;
        }
    }

    /// <summary>
    /// Accumulates time and spawns one enemy per elapsed interval. While the live
    /// cap is reached the backlog is dropped rather than banked, so a wave of
    /// deaths does not instantly dump a burst of replacements.
    ///
    /// In wave mode the loop also stops at the wave's spawn budget: once the
    /// budget is handed out the spawner goes quiet, which is what turns the rest
    /// of the wave into a clean-up rather than an endless trickle.
    /// </summary>
    private void Update()
    {
        EnsureReferences();

        if (_camera == null || _manager == null || HasFinishedSpawning)
        {
            return;
        }

        float interval = TierInterval;
        int maxAlive = TierMaxAlive;
        _timer += Time.deltaTime;

        int spawned = 0;
        while (_timer >= interval && spawned < MaxSpawnsPerFrame && !HasFinishedSpawning)
        {
            _timer -= interval;

            if (_manager.ActiveCount >= maxAlive)
            {
                _timer = 0f;
                break;
            }

            Enemy enemy = SpawnOne();
            if (enemy == null)
            {
                // A wave whose table yields nothing (or is exhausted) must not
                // spin this loop: drop the backlog and wait for the next wave.
                _timer = 0f;
                break;
            }

            if (_spawnBudget > 0)
            {
                _spawnBudget--;
            }
            spawned++;
        }
    }

    /// <summary>
    /// Picks the weighted entry of the tier's spawn table, mirroring the
    /// serialized-table draw so both paths stay one rule apart from a designer's
    /// point of view. Weights are re-summed per draw: the list is a handful of
    /// entries and a tier asset can be retuned between spawns.
    /// </summary>
    /// <returns>The picked tier entry, or null when the tier spawns nothing.</returns>
    private WaveTierData.EnemySpawnEntry PickTierEntry()
    {
        WaveTierData.EnemySpawnEntry[] entries = _tier.SpawnEntries;
        if (entries == null || entries.Length == 0)
        {
            return null;
        }

        float total = 0f;
        for (int i = 0; i < entries.Length; i++)
        {
            total += Mathf.Max(0f, entries[i].Weight);
        }

        float roll = UnityEngine.Random.value * total;
        for (int i = 0; i < entries.Length; i++)
        {
            float weight = Mathf.Max(0f, entries[i].Weight);
            if (weight <= 0f)
            {
                continue;
            }

            roll -= weight;
            if (roll <= 0f && weight > 0f)
            {
                return entries[i];
            }
        }

        // Weight sum zero (or a float drift to the end) falls back to the last
        // weighted entry, which is what the inline total would have chosen.
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            if (entries[i].Weight > 0f)
            {
                return entries[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Creates one enemy from the applied tier, using the per-prefab pool the
    /// wave mode owns. Placement matches the serialized path (ring just outside
    /// the view, then clamped so no enemy lands in a wall) and the registry is
    /// told where the enemy landed for the same reasons as there.
    /// </summary>
    /// <returns>The spawned enemy, or null when the tier spawns nothing.</returns>
    private Enemy SpawnTierEnemy()
    {
        WaveTierData.EnemySpawnEntry entry = PickTierEntry();
        if (entry == null || entry.Prefab == null)
        {
            return null;
        }

        ViewRect view = SpawnArea.GetViewRect(_camera, _spawnPlaneZ);
        Vector2 margin = new Vector2(view.HalfExtents.x * 2f, view.HalfExtents.y * 2f) * _viewportMargin;
        Vector2 point = SpawnArea.PickOutsidePoint(
            view.Center, view.HalfExtents, margin.x, margin.y, UnityEngine.Random.value);
        point = StageBounds.ClampInside(point, WaveSpawnInset);

        if (!_tierPools.TryGetValue(entry.Prefab, out ComponentPool<Enemy> pool))
        {
            // One container per enemy type keeps parked enemies apart, like the
            // serialized table's per-entry containers.
            Transform container = new GameObject($"Pool_{entry.Prefab.name}").transform;
            container.SetParent(transform, worldPositionStays: false);
            pool = new ComponentPool<Enemy>(entry.Prefab, container, PoolCapacity, PoolMaxSize);
            _tierPools.Add(entry.Prefab, pool);
        }

        Enemy enemy = pool.Get();
        if (enemy.TryGetComponent(out EnemyAI ai))
        {
            ai.PlaceAt(point);
        }
        else
        {
            enemy.transform.position = point;
        }

        _manager?.RefreshPosition(enemy);
        return enemy;
    }

    /// <summary>
    /// Draws the spawn-table entry for one spawn.
    ///
    /// It returns the index rather than the prefab because the entry also owns the
    /// pool its enemies come from, and choosing the two separately could pair a
    /// prefab with another entry's pool.
    ///
    /// The total weight is summed per draw rather than cached: the table holds a
    /// handful of entries, a draw happens once per spawn, and a cache would go
    /// stale the moment the weights were edited in the Inspector.
    /// </summary>
    /// <returns>Index of the chosen entry, or -1 when the table is empty or all weights are zero.</returns>
    private int PickEntryIndex()
    {
        if (_spawnEntries == null || _spawnEntries.Length == 0)
        {
            return -1;
        }

        float total = 0f;
        for (int i = 0; i < _spawnEntries.Length; i++)
        {
            total += Mathf.Max(0f, _spawnEntries[i].Weight);
        }

        if (total <= 0f)
        {
            return -1;
        }

        float roll = UnityEngine.Random.value * total;
        for (int i = 0; i < _spawnEntries.Length; i++)
        {
            float weight = Mathf.Max(0f, _spawnEntries[i].Weight);
            if (weight <= 0f)
            {
                continue;
            }

            roll -= weight;
            if (roll <= 0f)
            {
                return i;
            }
        }

        // Only reachable through floating-point drift in the running total, so the
        // last entry that can be picked is the correct fallback.
        for (int i = _spawnEntries.Length - 1; i >= 0; i--)
        {
            if (_spawnEntries[i].Weight > 0f)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Creates one enemy on the ring just outside the current view and makes sure the
    /// registry knows where it landed.
    ///
    /// The enemy comes from its spawn-table entry's pool, so a recycled one wakes up
    /// where it was parked rather than at the origin. Both cases still need
    /// <see cref="EnemyAI.PlaceAt"/>: a body created by <c>Instantiate</c> starts with
    /// a stale interpolator sample, a reused one starts with the pose it died in, and
    /// either would draw the sprite sliding in from somewhere else on the first
    /// physics step. Placement therefore always goes through the same call.
    ///
    /// Public, and returning the enemy, so a caller that needs a precise enemy count
    /// can reuse the weighted pick and the ring placement instead of reimplementing
    /// them - the performance harness does exactly that, and it must build its load
    /// from the same code the game does or the numbers describe a different game.
    ///
    /// The live cap and the per-frame budget both live in <see cref="Update"/>, so a
    /// direct caller deliberately bypasses them; it is responsible for its own
    /// pacing. References are resolved here as well, because a caller may run before
    /// the first Update ever has.
    /// </summary>
    /// <returns>The spawned enemy, or null when the spawn table yields no prefab.</returns>
    public Enemy SpawnOne()
    {
        EnsureReferences();

        // A tier drives its own spawn path; without one the spawner behaves
        // exactly as in its original serialized-table form.
        if (_tier != null)
        {
            return SpawnTierEnemy();
        }

        int index = PickEntryIndex();
        if (index < 0)
        {
            return null;
        }

        SpawnEntry entry = _spawnEntries[index];
        if (entry.Prefab == null)
        {
            return null;
        }

        ViewRect view = SpawnArea.GetViewRect(_camera, _spawnPlaneZ);
        Vector2 margin = new Vector2(view.HalfExtents.x * 2f, view.HalfExtents.y * 2f) * _viewportMargin;
        Vector2 point = SpawnArea.PickOutsidePoint(
            view.Center, view.HalfExtents, margin.x, margin.y, UnityEngine.Random.value);

        Enemy enemy = entry.PoolUnder(transform, PoolCapacity, PoolMaxSize).Get();

        EnemyAI ai = enemy.GetComponent<EnemyAI>();
        if (ai != null)
        {
            ai.PlaceAt(point);
        }
        else
        {
            // Prefabs without the chase behaviour (test stand-ins) still need to
            // end up on the ring.
            enemy.transform.position = point;
        }

        // Placement moved the enemy, and registration happened when the pool
        // activated it, so the registry has to be told where it really is.
        _manager?.RefreshPosition(enemy);
        return enemy;
    }
}
