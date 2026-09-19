using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds throwaway balance assets for play-mode tests.
///
/// Balance values live in ScriptableObject assets now, so a test that used to poke
/// a serialized field on a component instead creates an instance here and assigns
/// it to the component. Instances come from ScriptableObject.CreateInstance, so
/// they carry the class defaults, are never saved, and never touch the project's
/// real assets.
///
/// The setters are exposed because a test sometimes has to retune an asset after
/// the fixture has already been built (for example to make a shot travel).
/// </summary>
internal static class TestData
{
    /// <summary>
    /// Creates an enemy data asset.
    /// </summary>
    /// <param name="maxHealth">Hits the enemy survives.</param>
    /// <param name="moveSpeed">Chase speed in units per second.</param>
    /// <param name="speedVariance">
    /// Speed spread as a fraction of the speed. Defaults to 0 so distance
    /// assertions are not at the mercy of a random roll.
    /// </param>
    /// <param name="contactDamage">Hits dealt on contact.</param>
    /// <param name="xpValue">Experience the dropped gem is worth.</param>
    /// <returns>The created asset.</returns>
    public static EnemyData CreateEnemyData(int maxHealth = 3, float moveSpeed = 1.8f,
        float speedVariance = 0f, int contactDamage = 1, int xpValue = 1)
    {
        var data = ScriptableObject.CreateInstance<EnemyData>();
        SetInt(data, "_maxHealth", maxHealth);
        SetFloat(data, "_moveSpeed", moveSpeed);
        SetFloat(data, "_speedVariance", speedVariance);
        SetInt(data, "_contactDamage", contactDamage);
        SetInt(data, "_xpValue", xpValue);
        return data;
    }

    /// <summary>
    /// Creates a weapon data asset.
    /// </summary>
    /// <param name="projectilePrefab">Shot prefab the weapon instantiates.</param>
    /// <param name="fireInterval">Seconds between shots.</param>
    /// <param name="range">Targeting radius in units.</param>
    /// <param name="damage">Hits each shot removes.</param>
    /// <param name="projectileSpeed">Shot speed in units per second.</param>
    /// <param name="projectileLifetime">Seconds before a shot retires itself.</param>
    /// <param name="projectileHitRadius">Distance within which an enemy counts as struck.</param>
    /// <param name="visualScalePerDamage">Scale growth per point of damage above the weapon's own.</param>
    /// <param name="maxVisualScale">Upper bound for the damage-driven visual scale.</param>
    /// <returns>The created asset.</returns>
    public static WeaponData CreateWeaponData(Projectile projectilePrefab = null, float fireInterval = 0.5f,
        float range = 8f, int damage = 1, float projectileSpeed = 12f, float projectileLifetime = 1f,
        float projectileHitRadius = 0.35f, float visualScalePerDamage = 0.25f, float maxVisualScale = 2.5f)
    {
        var data = ScriptableObject.CreateInstance<WeaponData>();
        SetObjectReference(data, "_projectilePrefab", projectilePrefab);
        SetFloat(data, "_fireInterval", fireInterval);
        SetFloat(data, "_range", range);
        SetInt(data, "_damage", damage);
        SetFloat(data, "_projectileSpeed", projectileSpeed);
        SetFloat(data, "_projectileLifetime", projectileLifetime);
        SetFloat(data, "_projectileHitRadius", projectileHitRadius);
        SetFloat(data, "_visualScalePerDamage", visualScalePerDamage);
        SetFloat(data, "_maxVisualScale", maxVisualScale);
        return data;
    }

    /// <summary>
    /// Creates a player data asset. The defaults are the shipped ones, which is
    /// what the movement tests assert against.
    /// </summary>
    /// <param name="maxHealth">Hits the player survives.</param>
    /// <param name="invulnerabilitySeconds">Immunity window after a hit, in seconds.</param>
    /// <param name="maxSpeed">Top speed in units per second.</param>
    /// <param name="acceleration">Acceleration in units per second squared.</param>
    /// <param name="deceleration">Deceleration in units per second squared.</param>
    /// <param name="turnBoost">Brake multiplier when reversing.</param>
    /// <param name="rollSpeed">Speed in units per second while rolling.</param>
    /// <param name="rollSeconds">Length of one roll, in seconds.</param>
    /// <returns>The created asset.</returns>
    public static PlayerData CreatePlayerData(int maxHealth = 10, float invulnerabilitySeconds = 0.5f,
        float maxSpeed = 8f, float acceleration = 40f, float deceleration = 60f, float turnBoost = 3f,
        float rollSpeed = 14f, float rollSeconds = 0.33f)
    {
        var data = ScriptableObject.CreateInstance<PlayerData>();
        SetInt(data, "_maxHealth", maxHealth);
        SetFloat(data, "_invulnerabilitySeconds", invulnerabilitySeconds);
        SetFloat(data, "_maxSpeed", maxSpeed);
        SetFloat(data, "_acceleration", acceleration);
        SetFloat(data, "_deceleration", deceleration);
        SetFloat(data, "_turnBoost", turnBoost);
        SetFloat(data, "_rollSpeed", rollSpeed);
        SetFloat(data, "_rollSeconds", rollSeconds);
        return data;
    }

    /// <summary>
    /// Creates a level curve asset from explicit thresholds.
    /// </summary>
    /// <param name="xpRequired">
    /// Experience to go from level 1 to 2, then 2 to 3, and so on.
    /// </param>
    /// <returns>The created asset.</returns>
    public static LevelCurveData CreateLevelCurveData(params int[] xpRequired)
    {
        var data = ScriptableObject.CreateInstance<LevelCurveData>();
        var so = new SerializedObject(data);
        SerializedProperty array = so.FindProperty("_xpRequired");
        array.arraySize = xpRequired.Length;

        for (int i = 0; i < xpRequired.Length; i++)
        {
            array.GetArrayElementAtIndex(i).intValue = xpRequired[i];
        }

        so.ApplyModifiedProperties();
        return data;
    }

    /// <summary>
    /// Creates a single upgrade asset.
    /// </summary>
    /// <param name="stat">Stat the upgrade raises.</param>
    /// <param name="amount">Amount added, or multiplied in for fire rate.</param>
    /// <param name="displayName">Name shown on the choice button.</param>
    /// <returns>The created asset.</returns>
    public static UpgradeData CreateUpgradeData(UpgradeStat stat, float amount, string displayName = "Test Upgrade")
    {
        var data = ScriptableObject.CreateInstance<UpgradeData>();
        SetString(data, "_displayName", displayName);
        SetString(data, "_description", "test");
        SetInt(data, "_stat", (int)stat);
        SetFloat(data, "_amount", amount);
        return data;
    }

    /// <summary>
    /// Creates an upgrade pool asset holding the given upgrades.
    /// </summary>
    /// <param name="upgrades">Upgrades the pool offers.</param>
    /// <returns>The created asset.</returns>
    public static UpgradePoolData CreateUpgradePoolData(params UpgradeData[] upgrades)
    {
        var data = ScriptableObject.CreateInstance<UpgradePoolData>();
        var so = new SerializedObject(data);
        SerializedProperty array = so.FindProperty("_upgrades");
        array.arraySize = upgrades.Length;

        for (int i = 0; i < upgrades.Length; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = upgrades[i];
        }

        so.ApplyModifiedProperties();
        return data;
    }

    /// <summary>Writes a private string field exactly as the Inspector would.</summary>
    /// <param name="target">Object owning the field.</param>
    /// <param name="fieldName">Serialized field name.</param>
    /// <param name="value">Value to assign.</param>
    public static void SetString(Object target, string fieldName, string value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).stringValue = value;
        so.ApplyModifiedProperties();
    }

    /// <summary>Writes a private float field exactly as the Inspector would.</summary>
    /// <param name="target">Object owning the field.</param>
    /// <param name="fieldName">Serialized field name.</param>
    /// <param name="value">Value to assign.</param>
    public static void SetFloat(Object target, string fieldName, float value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).floatValue = value;
        so.ApplyModifiedProperties();
    }

    /// <summary>Writes a private int field exactly as the Inspector would.</summary>
    /// <param name="target">Object owning the field.</param>
    /// <param name="fieldName">Serialized field name.</param>
    /// <param name="value">Value to assign.</param>
    public static void SetInt(Object target, string fieldName, int value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).intValue = value;
        so.ApplyModifiedProperties();
    }

    /// <summary>Writes a private object reference exactly as the Inspector would.</summary>
    /// <param name="target">Object owning the field.</param>
    /// <param name="fieldName">Serialized field name.</param>
    /// <param name="value">Reference to assign; null clears it.</param>
    public static void SetObjectReference(Object target, string fieldName, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }
}
