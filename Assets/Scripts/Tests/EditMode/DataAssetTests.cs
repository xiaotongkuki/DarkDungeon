using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pins the wiring between the balance assets and the prefabs that consume them.
///
/// The data-driven design moves tuning values out of code and into assets, so a
/// mistake there - an asset deleted, a prefab's reference cleared, a demo type
/// that stopped being distinct - would otherwise only surface as altered gameplay.
/// These tests fail loudly instead, in the same spirit as <see cref="GameLayersTests"/>.
/// </summary>
public class DataAssetTests
{
    private const string GruntDataPath = "Assets/Data/Enemies/Enemy_Grunt.asset";
    private const string FastDataPath = "Assets/Data/Enemies/Enemy_Fast.asset";
    private const string WeaponPath = "Assets/Data/Weapons/Weapon_Default.asset";
    private const string PlayerPath = "Assets/Data/Player/Player.asset";
    private const string LevelCurvePath = "Assets/Data/Levels/LevelCurve.asset";
    private const string UpgradePoolPath = "Assets/Data/Upgrades/UpgradePool.asset";
    private const string GemPrefabPath = "Assets/Prefabs/Gem.prefab";
    private const string EnemyPrefabPath = "Assets/Prefabs/Enemy.prefab";
    private const string FastPrefabPath = "Assets/Prefabs/Enemy_Fast.prefab";

    [Test]
    public void BalanceAssetsExist()
    {
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<EnemyData>(GruntDataPath),
            "The grunt's balance asset is missing.");
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<EnemyData>(FastDataPath),
            "The fast enemy's balance asset is missing.");
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<WeaponData>(WeaponPath),
            "The default weapon asset is missing.");
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<PlayerData>(PlayerPath),
            "The player's balance asset is missing.");
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<LevelCurveData>(LevelCurvePath),
            "The level curve is missing; without it nothing can level up.");
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UpgradePoolData>(UpgradePoolPath),
            "The upgrade pool is missing; without it a level-up offers nothing.");
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(GemPrefabPath),
            "The gem prefab is missing; kills would drop nothing.");
    }

    [Test]
    public void LevelCurveRisesWithEveryLevel()
    {
        LevelCurveData curve = AssetDatabase.LoadAssetAtPath<LevelCurveData>(LevelCurvePath);

        Assert.Greater(curve.MaxLevel, 1, "A one-level curve can never level up.");

        int previous = 0;
        for (int level = 1; level < curve.MaxLevel; level++)
        {
            int required = curve.RequiredForNextLevel(level);
            Assert.Greater(required, 0, $"Level {level} must cost something.");
            Assert.Greater(required, previous,
                "Each level must cost more than the last, or progression stalls.");
            previous = required;
        }

        Assert.AreEqual(0, curve.RequiredForNextLevel(curve.MaxLevel),
            "Past the last threshold the curve must report nothing left to buy.");
    }

    [Test]
    public void UpgradePoolOffersEnoughChoices()
    {
        UpgradePoolData pool = AssetDatabase.LoadAssetAtPath<UpgradePoolData>(UpgradePoolPath);

        Assert.GreaterOrEqual(pool.Count, 3,
            "The choice panel offers three upgrades, so the pool needs at least three.");

        for (int i = 0; i < pool.Count; i++)
        {
            UpgradeData upgrade = pool.Get(i);
            Assert.IsNotNull(upgrade, $"Upgrade {i} is a missing reference.");
            Assert.IsNotEmpty(upgrade.DisplayName, $"Upgrade {i} has no name to show.");
            Assert.AreNotEqual(0f, upgrade.Amount, $"Upgrade {i} would change nothing.");
        }
    }

    [Test]
    public void TheSecondEnemyTypeIsWorthMoreExperience()
    {
        // The frail type is the one worth chasing, which is what makes the drop
        // worth walking over.
        EnemyData grunt = AssetDatabase.LoadAssetAtPath<EnemyData>(GruntDataPath);
        EnemyData fast = AssetDatabase.LoadAssetAtPath<EnemyData>(FastDataPath);

        Assert.Greater(grunt.XpValue, 0, "Every kill must be worth something.");
        Assert.Greater(fast.XpValue, grunt.XpValue, "The frailer type must pay better.");
    }

    [Test]
    public void EnemyPrefabsCarryTheirData()
    {
        Assert.AreSame(AssetDatabase.LoadAssetAtPath<EnemyData>(GruntDataPath), DataOf(EnemyPrefabPath),
            "Enemy.prefab must point at the grunt's balance asset, or it will refuse to act.");
        Assert.AreSame(AssetDatabase.LoadAssetAtPath<EnemyData>(FastDataPath), DataOf(FastPrefabPath),
            "Enemy_Fast.prefab must point at the fast enemy's balance asset.");
    }

    [Test]
    public void TheSecondEnemyTypeIsFasterAndFrailerThanTheGrunt()
    {
        // This is the whole point of the second type: it proves a new monster is a
        // new asset plus a prefab, not a new branch in code.
        EnemyData grunt = AssetDatabase.LoadAssetAtPath<EnemyData>(GruntDataPath);
        EnemyData fast = AssetDatabase.LoadAssetAtPath<EnemyData>(FastDataPath);

        Assert.Less(fast.MaxHealth, grunt.MaxHealth, "The fast type is meant to die in fewer hits.");
        Assert.Greater(fast.MoveSpeed, grunt.MoveSpeed, "The fast type is meant to close in quicker.");
    }

    [Test]
    public void WeaponPointsAtAShotPrefab()
    {
        WeaponData weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(WeaponPath);

        Assert.IsNotNull(weapon.ProjectilePrefab,
            "A weapon with no shot prefab fires nothing at all.");
        Assert.Greater(weapon.ProjectileSpeed, 0f, "A shot that cannot travel cannot hit anything.");
        Assert.Greater(weapon.FireInterval, 0f, "A zero interval would fire every frame.");
    }

    [Test]
    public void PlayerAssetIsPlayable()
    {
        PlayerData data = AssetDatabase.LoadAssetAtPath<PlayerData>(PlayerPath);

        Assert.Greater(data.MaxHealth, 0, "The player must survive at least one hit.");
        Assert.Greater(data.MaxSpeed, 0f, "The player must be able to move.");
        Assert.Greater(data.InvulnerabilitySeconds, 0f,
            "Without an invulnerability window a crowd would empty the pool in one frame.");
    }

    /// <summary>
    /// Reads the balance asset a prefab's enemy component points at.
    /// </summary>
    /// <param name="prefabPath">Prefab asset path.</param>
    /// <returns>The referenced asset, or null when the prefab or reference is missing.</returns>
    private static EnemyData DataOf(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.IsNotNull(prefab, $"Prefab '{prefabPath}' is missing.");

        Enemy enemy = prefab.GetComponent<Enemy>();
        Assert.IsNotNull(enemy, $"Prefab '{prefabPath}' has no Enemy component.");

        return enemy.Data;
    }
}
