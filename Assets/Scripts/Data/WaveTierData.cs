using System;
using UnityEngine;

/// <summary>
/// One difficulty tier of the endless run, stored as a project asset.
///
/// A tier answers two questions: what fills the arena (which enemies, how
/// often, how many alive at once) and how much furniture the wave carries
/// (cover pieces and traps). The run cycles through the tiers as waves pass, so
/// tuning difficulty is editing a handful of assets rather than a formula.
///
/// Per-wave state - the spawn budget, the kills counted - lives on the wave
/// controller, never here: an asset is shared and read-only at runtime.
/// </summary>
[CreateAssetMenu(fileName = "WaveTierData", menuName = "My project/Data/Wave Tier")]
public class WaveTierData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display name of the tier, for tooling and debugging.")]
    [SerializeField] private string _tierName = "Tier";

    [Header("Enemies")]
    [Tooltip("Enemy types and their relative weights. An empty list spawns nothing.")]
    [SerializeField] private EnemySpawnEntry[] _spawnEntries;
    [Tooltip("Seconds between spawns while this tier is active. Lower means denser.")]
    [SerializeField] private float _spawnInterval = 0.6f;
    [Tooltip("Hard cap on live enemies while this tier is active.")]
    [SerializeField] private int _maxAlive = 150;

    [Header("Props")]
    [Tooltip("Cover pieces this tier places each wave.")]
    [SerializeField] private int _obstacleCount = 10;
    [Tooltip("Traps this tier places each wave.")]
    [SerializeField] private int _hazardCount = 5;

    /// <summary>One enemy type of a tier's spawn table.</summary>
    [Serializable]
    public class EnemySpawnEntry
    {
        [Tooltip("Enemy prefab to instantiate.")]
        [SerializeField] private Enemy _prefab;
        [Tooltip("Relative share of spawns; a weight of 2 against 1 appears twice as often.")]
        [SerializeField] private float _weight = 1f;

        /// <summary>Enemy prefab to instantiate.</summary>
        public Enemy Prefab => _prefab;

        /// <summary>Relative share of spawns; non-positive weights are never picked.</summary>
        public float Weight => _weight;

        /// <summary>Builds an entry in code, for tests and generated tiers.</summary>
        /// <param name="prefab">Enemy prefab to instantiate.</param>
        /// <param name="weight">Relative share of spawns.</param>
        public EnemySpawnEntry(Enemy prefab, float weight)
        {
            _prefab = prefab;
            _weight = weight;
        }
    }

    /// <summary>Builds a tier in code (tests); assets go through the Create menu.</summary>
    public WaveTierData(string tierName, EnemySpawnEntry[] spawnEntries, float spawnInterval,
        int maxAlive, int obstacleCount, int hazardCount)
    {
        _tierName = tierName;
        _spawnEntries = spawnEntries;
        _spawnInterval = spawnInterval;
        _maxAlive = maxAlive;
        _obstacleCount = obstacleCount;
        _hazardCount = hazardCount;
    }

    /// <summary>Display name of the tier.</summary>
    public string TierName => _tierName;

    /// <summary>Enemy spawn table; may be empty.</summary>
    public EnemySpawnEntry[] SpawnEntries => _spawnEntries;

    /// <summary>Seconds between spawns while this tier is active.</summary>
    public float SpawnInterval => _spawnInterval;

    /// <summary>Hard cap on live enemies while this tier is active.</summary>
    public int MaxAlive => _maxAlive;

    /// <summary>Cover pieces placed each wave in this tier.</summary>
    public int ObstacleCount => _obstacleCount;

    /// <summary>Traps placed each wave in this tier.</summary>
    public int HazardCount => _hazardCount;
}
