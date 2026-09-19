using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the upgrade bonuses: each stat folds in the way its upgrade
/// says, applying nothing is a no-op, a fresh run starts unmodified, and a damage
/// bonus really reaches the shot that gets fired.
/// </summary>
public class PlayerStatModifiersTests
{
    private GameObject _playerObject;
    private PlayerStatModifiers _modifiers;
    private UpgradeData _damage;
    private UpgradeData _fireRate;
    private UpgradeData _moveSpeed;
    private UpgradeData _maxHealth;
    private UpgradeData _magnet;
    private int _statsChangedCount;

    [SetUp]
    public void SetUp()
    {
        // The runner reuses one fixture instance, so the counter must be cleared.
        _statsChangedCount = 0;

        _playerObject = new GameObject("Player");
        _modifiers = _playerObject.AddComponent<PlayerStatModifiers>();

        _damage = TestData.CreateUpgradeData(UpgradeStat.Damage, 1f);
        _fireRate = TestData.CreateUpgradeData(UpgradeStat.FireRate, 0.85f);
        _moveSpeed = TestData.CreateUpgradeData(UpgradeStat.MoveSpeed, 0.8f);
        _maxHealth = TestData.CreateUpgradeData(UpgradeStat.MaxHealth, 2f);
        _magnet = TestData.CreateUpgradeData(UpgradeStat.MagnetRadius, 1.5f);

        EventBus.StatsChanged += OnStatsChanged;
    }

    [TearDown]
    public void TearDown()
    {
        EventBus.StatsChanged -= OnStatsChanged;

        Object.DestroyImmediate(_damage);
        Object.DestroyImmediate(_fireRate);
        Object.DestroyImmediate(_moveSpeed);
        Object.DestroyImmediate(_maxHealth);
        Object.DestroyImmediate(_magnet);
        Object.DestroyImmediate(_playerObject);
    }

    /// <summary>Records an upgrade landing.</summary>
    private void OnStatsChanged()
    {
        _statsChangedCount++;
    }

    [Test]
    public void StartsUnmodified()
    {
        Assert.AreEqual(0f, _modifiers.DamageBonus);
        Assert.AreEqual(1f, _modifiers.FireIntervalScale, "A neutral fire rate is a multiplier of one.");
        Assert.AreEqual(0f, _modifiers.MoveSpeedBonus);
        Assert.AreEqual(0f, _modifiers.MaxHealthBonus);
        Assert.AreEqual(0f, _modifiers.MagnetRadiusBonus);
    }

    [Test]
    public void EveryStat_FoldsInItsOwnWay()
    {
        _modifiers.Apply(_damage);
        _modifiers.Apply(_fireRate);
        _modifiers.Apply(_moveSpeed);
        _modifiers.Apply(_maxHealth);
        _modifiers.Apply(_magnet);

        Assert.AreEqual(1f, _modifiers.DamageBonus);
        Assert.AreEqual(0.85f, _modifiers.FireIntervalScale, 0.0001f);
        Assert.AreEqual(0.8f, _modifiers.MoveSpeedBonus, 0.0001f);
        Assert.AreEqual(2f, _modifiers.MaxHealthBonus);
        Assert.AreEqual(1.5f, _modifiers.MagnetRadiusBonus, 0.0001f);
        Assert.AreEqual(5, _statsChangedCount, "Each upgrade must tell the systems to re-read their values.");
    }

    [Test]
    public void FireRate_CompoundsRatherThanAdding()
    {
        _modifiers.Apply(_fireRate);
        _modifiers.Apply(_fireRate);

        Assert.AreEqual(0.7225f, _modifiers.FireIntervalScale, 0.0001f,
            "Two 15% upgrades must compound; subtracting intervals would eventually reach zero.");
    }

    [Test]
    public void ApplyingNothing_IsIgnored()
    {
        _modifiers.Apply(null);

        Assert.AreEqual(0f, _modifiers.DamageBonus);
        Assert.AreEqual(0, _statsChangedCount, "A null upgrade must not announce a change.");
    }

    [Test]
    public void ReEnabling_StartsAFreshRun()
    {
        _modifiers.Apply(_damage);
        Assert.AreEqual(1f, _modifiers.DamageBonus, "Sanity check: the bonus must have landed.");

        _playerObject.SetActive(false);
        _playerObject.SetActive(true);

        Assert.AreEqual(0f, _modifiers.DamageBonus, "A new run must start unmodified.");
    }

    [UnityTest]
    public IEnumerator DamageBonus_ReachesTheShot()
    {
        // Integration: the bonus has to change what a shot actually does. The weapon
        // is authored at zero damage, so any damage dealt can only have come from
        // the upgrade.
        var managerObject = new GameObject("EnemyManager");
        managerObject.AddComponent<EnemyManager>();

        var enemyObject = new GameObject("Enemy");
        enemyObject.SetActive(false);
        Enemy enemy = enemyObject.AddComponent<Enemy>();
        TestData.SetObjectReference(enemy, "_data", TestData.CreateEnemyData(maxHealth: 3));
        enemyObject.SetActive(true);

        WeaponData weapon = TestData.CreateWeaponData(damage: 0, projectileSpeed: 0f, projectileLifetime: 100f);
        var shotObject = new GameObject("Shot");
        Projectile shot = shotObject.AddComponent<Projectile>();

        try
        {
            _modifiers.Apply(_damage);
            _modifiers.Apply(_damage);

            int damage = Mathf.Max(1, Mathf.RoundToInt(weapon.Damage + _modifiers.DamageBonus));
            shot.Launch(Vector2.up, weapon, damage);

            // Parked on the enemy, so the shot's next update resolves the hit.
            shot.transform.position = enemy.transform.position;

            yield return null;

            Assert.AreEqual(1, enemy.CurrentHealth,
                "Two damage upgrades must take two hits off a three-health enemy.");
        }
        finally
        {
            Object.DestroyImmediate(shotObject);
            Object.DestroyImmediate(weapon);
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(managerObject);
        }
    }
}
