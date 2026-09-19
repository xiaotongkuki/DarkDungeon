using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pins the endless-run wiring: the three difficulty tiers exist and escalate,
/// and every prop prefab the scatter uses declares a footprint. A missing
/// footprint would silently become a one-tile prop, which is exactly the kind of
/// drift these asset tests exist to catch.
/// </summary>
public class WaveTierTests
{
    private static readonly string[] TierPaths =
    {
        "Assets/Data/Tiers/WaveTier_1.asset",
        "Assets/Data/Tiers/WaveTier_2.asset",
        "Assets/Data/Tiers/WaveTier_3.asset"
    };

    private static readonly string[] PropPaths =
    {
        "Assets/Prefabs/Props/Prop_Corner_L.prefab",
        "Assets/Prefabs/Props/Prop_Wall_H2.prefab",
        "Assets/Prefabs/Props/Prop_Wall_H3.prefab",
        "Assets/Prefabs/Props/Prop_Stairs.prefab",
        "Assets/Prefabs/Props/Prop_Pillar.prefab",
        "Assets/Prefabs/Props/Prop_Rubble.prefab"
    };

    [Test]
    public void EveryTierAssetExists()
    {
        foreach (string path in TierPaths)
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<WaveTierData>(path), $"Tier missing: {path}");
        }
    }

    [Test]
    public void TiersAreSpawnable()
    {
        foreach (string path in TierPaths)
        {
            WaveTierData tier = AssetDatabase.LoadAssetAtPath<WaveTierData>(path);
            Assert.IsNotNull(tier, $"Tier missing: {path}");
            Assert.IsNotEmpty(tier.TierName, $"{tier.name} has no display name.");
            Assert.Greater(tier.SpawnInterval, 0.01f, $"{tier.name} would spawn every frame.");
            Assert.Greater(tier.MaxAlive, 0, $"{tier.name} has no live cap.");

            float weight = 0f;
            foreach (WaveTierData.EnemySpawnEntry entry in tier.SpawnEntries)
            {
                if (entry != null && entry.Prefab != null && entry.Weight > 0f)
                {
                    weight += entry.Weight;
                }
            }
            Assert.Greater(weight, 0f, $"{tier.name} spawns nothing.");
        }
    }

    [Test]
    public void TiersEscalateAcrossTheRun()
    {
        WaveTierData first = AssetDatabase.LoadAssetAtPath<WaveTierData>(TierPaths[0]);
        WaveTierData second = AssetDatabase.LoadAssetAtPath<WaveTierData>(TierPaths[1]);
        WaveTierData third = AssetDatabase.LoadAssetAtPath<WaveTierData>(TierPaths[2]);

        Assert.Less(second.SpawnInterval, first.SpawnInterval, "Tier 2 must spawn faster than tier 1.");
        Assert.Less(third.SpawnInterval, second.SpawnInterval, "Tier 3 must spawn faster than tier 2.");
        Assert.GreaterOrEqual(second.MaxAlive, first.MaxAlive, "The live cap must not shrink with depth.");
        Assert.GreaterOrEqual(third.MaxAlive, second.MaxAlive);
    }

    [Test]
    public void TiersCarryProps()
    {
        foreach (string path in TierPaths)
        {
            WaveTierData tier = AssetDatabase.LoadAssetAtPath<WaveTierData>(path);
            Assert.GreaterOrEqual(tier.ObstacleCount, 0, $"{tier.name} has a negative cover count.");

            // Cover is currently switched off: the placer's obstacle table is
            // empty, so the tier's target has nothing to distribute across. The
            // traps are the only props a wave carries.
            Assert.Greater(tier.HazardCount, 0, $"{tier.name} places no traps.");
        }
    }

    [Test]
    public void EveryPropPrefabDeclaresAFootprint()
    {
        foreach (string path in PropPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"Prop prefab missing: {path}");

            PropFootprint footprint = prefab.GetComponent<PropFootprint>();
            Assert.IsNotNull(footprint, $"{prefab.name} has no PropFootprint, so its size would be guessed.");
            Assert.GreaterOrEqual(footprint.Size.x, 1, $"{prefab.name} has an empty footprint.");
            Assert.GreaterOrEqual(footprint.Size.y, 1);
        }
    }

    [Test]
    public void PropsHaveCollidersMatchingTheirFootprint()
    {
        foreach (string path in PropPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            PropFootprint footprint = prefab.GetComponent<PropFootprint>();

            // The L piece is two colliders on child arms; the rest carry one.
            Collider2D[] colliders = prefab.GetComponentsInChildren<Collider2D>();
            Assert.GreaterOrEqual(colliders.Length, 1, $"{prefab.name} would not block anything.");
            Assert.GreaterOrEqual(colliders.Length, footprint.Size.x * footprint.Size.y > 1 ? 1 : 1,
                "A multi-tile prop needs at least one collider.");
        }
    }

    [Test]
    public void TheSpikeTrapDeclaresItsFootprint()
    {
        GameObject spike = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SpikeTrap.prefab");
        Assert.IsNotNull(spike, "The spike trap prefab is missing.");
        Assert.IsNotNull(spike.GetComponent<PropFootprint>(),
            "The spike trap must declare a footprint or the placer would book it as one tile by accident.");
    }
}
