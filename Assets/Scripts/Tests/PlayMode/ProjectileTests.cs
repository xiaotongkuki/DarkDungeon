using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for shots: straight flight, impact damage, retiring on impact
/// or lifetime, and ignoring enemies that are not actually in the way.
/// </summary>
public class ProjectileTests
{
    /// <summary>Cell size for these tests; small cells keep the grid honest.</summary>
    private const float CellSize = 2f;

    /// <summary>Hit radius used by most tests, in world units.</summary>
    private const float HitRadius = 0.35f;

    private GameObject _managerObject;
    private EnemyManager _manager;
    private readonly List<GameObject> _enemyObjects = new List<GameObject>();
    private readonly List<GameObject> _shotObjects = new List<GameObject>();

    [SetUp]
    public void SetUp()
    {
        _managerObject = new GameObject("EnemyManager");
        _manager = _managerObject.AddComponent<EnemyManager>();
        SetFloat(_manager, "_cellSize", CellSize);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < _shotObjects.Count; i++)
        {
            if (_shotObjects[i] != null)
            {
                Object.DestroyImmediate(_shotObjects[i]);
            }
        }
        _shotObjects.Clear();

        for (int i = 0; i < _enemyObjects.Count; i++)
        {
            if (_enemyObjects[i] != null)
            {
                Object.DestroyImmediate(_enemyObjects[i]);
            }
        }
        _enemyObjects.Clear();

        Object.DestroyImmediate(_managerObject);
    }

    /// <summary>
    /// Creates a stationary enemy. No <see cref="EnemyAI"/> is added, so the enemy
    /// stays exactly where it was put.
    /// </summary>
    /// <param name="position">World position.</param>
    /// <param name="health">Hits the enemy survives.</param>
    /// <returns>The created enemy.</returns>
    private Enemy SpawnEnemy(Vector2 position, int health = 3)
    {
        // Built inactive so the data asset can be assigned before Awake runs.
        var go = new GameObject("Enemy");
        go.SetActive(false);
        go.transform.position = position;
        Enemy enemy = go.AddComponent<Enemy>();
        TestData.SetObjectReference(enemy, "_data", TestData.CreateEnemyData(maxHealth: health));
        go.SetActive(true);

        _enemyObjects.Add(go);
        return enemy;
    }

    /// <summary>
    /// Creates a shot at the given position and sends it on its way. The flight
    /// values live on a weapon asset now, so one is built for the shot here.
    /// </summary>
    /// <param name="position">Spawn position.</param>
    /// <param name="direction">Travel direction.</param>
    /// <param name="speed">Speed in units per second.</param>
    /// <param name="lifetime">Seconds before it retires itself.</param>
    /// <param name="damage">Hits it removes on impact.</param>
    /// <returns>The created projectile.</returns>
    private Projectile SpawnShot(Vector2 position, Vector2 direction, float speed,
        float lifetime, int damage)
    {
        var go = new GameObject("Shot");
        go.transform.position = position;
        Projectile shot = go.AddComponent<Projectile>();

        WeaponData weapon = TestData.CreateWeaponData(damage: damage, projectileSpeed: speed,
            projectileLifetime: lifetime, projectileHitRadius: HitRadius);

        shot.Launch(direction, weapon);

        _shotObjects.Add(go);
        return shot;
    }

    /// <summary>Writes a private float field exactly as the Inspector would.</summary>
    /// <param name="target">Object owning the field.</param>
    /// <param name="fieldName">Serialized field name.</param>
    /// <param name="value">Value to assign.</param>
    private static void SetFloat(Object target, string fieldName, float value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).floatValue = value;
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Yields until the given amount of game time has passed, regardless of how
    /// many frames that takes on the current machine and editor focus state.
    /// </summary>
    /// <param name="seconds">Game-time duration to wait for.</param>
    private static IEnumerator WaitForGameTime(float seconds)
    {
        float deadline = Time.time + seconds;
        while (Time.time < deadline)
        {
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator MovesAlongItsLaunchDirection()
    {
        Projectile shot = SpawnShot(Vector2.zero, Vector2.right, speed: 10f, lifetime: 5f, damage: 1);

        yield return WaitForGameTime(0.3f);

        Vector3 travelled = shot.transform.position;
        Assert.Greater(travelled.x, 2f, "The shot must travel along its launch direction.");
        Assert.AreEqual(0f, travelled.y, 0.01f, "It must not drift sideways.");
    }

    [Test]
    public void Launch_OrientsTheSpriteAlongTheFlightDirection()
    {
        // The projectile art points right; flying up must rotate it 90 degrees.
        Projectile upShot = SpawnShot(Vector2.zero, Vector2.up, speed: 10f, lifetime: 5f, damage: 1);
        Assert.AreEqual(90f, upShot.transform.eulerAngles.z, 0.5f,
            "An upward flight must turn the right-facing art up.");

        Projectile diagonalShot = SpawnShot(Vector2.zero, new Vector2(1f, 1f), speed: 10f, lifetime: 5f, damage: 1);
        Assert.AreEqual(45f, diagonalShot.transform.eulerAngles.z, 0.5f,
            "A 45-degree flight must rotate the art to 45 degrees.");
    }

    [UnityTest]
    public IEnumerator DamagesTheEnemyItReaches()
    {
        Enemy enemy = SpawnEnemy(new Vector2(2f, 0f), health: 3);

        SpawnShot(Vector2.zero, Vector2.right, speed: 20f, lifetime: 5f, damage: 1);

        yield return WaitForGameTime(0.4f);

        Assert.AreEqual(2, enemy.CurrentHealth, "The shot must take one hit off the enemy.");
    }

    [UnityTest]
    public IEnumerator KillsAnEnemyAndLeavesTheGrid()
    {
        Enemy enemy = SpawnEnemy(new Vector2(2f, 0f), health: 1);

        SpawnShot(Vector2.zero, Vector2.right, speed: 20f, lifetime: 5f, damage: 1);

        yield return WaitForGameTime(0.4f);

        Assert.IsFalse(enemy.IsAlive, "A single hit must kill a one-health enemy.");
        Assert.AreEqual(0, _manager.ActiveCount, "The dead enemy must leave the registry.");
    }

    [UnityTest]
    public IEnumerator RetiresItselfAfterHitting()
    {
        SpawnEnemy(new Vector2(2f, 0f), health: 3);
        Projectile shot = SpawnShot(Vector2.zero, Vector2.right, speed: 20f, lifetime: 5f, damage: 1);

        yield return WaitForGameTime(0.4f);

        Assert.IsTrue(shot == null, "A shot that lands must retire instead of flying on.");
    }

    [UnityTest]
    public IEnumerator RetiresItselfAfterItsLifetime_WhenItHitsNothing()
    {
        Projectile shot = SpawnShot(Vector2.zero, Vector2.right, speed: 1f, lifetime: 0.1f, damage: 1);

        yield return WaitForGameTime(0.4f);

        Assert.IsTrue(shot == null, "A shot that hits nothing must not leak; the lifetime reclaims it.");
    }

    [UnityTest]
    public IEnumerator IgnoresAnEnemyOutsideItsHitRadius()
    {
        // Three units off the flight path: well outside the hit radius, so the shot
        // must fly past untouched.
        Enemy enemy = SpawnEnemy(new Vector2(2f, 3f), health: 3);
        Projectile shot = SpawnShot(Vector2.zero, Vector2.right, speed: 20f, lifetime: 0.3f, damage: 1);

        yield return WaitForGameTime(0.4f);

        Assert.AreEqual(3, enemy.CurrentHealth, "A near miss must not deal damage.");
        Assert.IsTrue(shot == null, "The shot still retires on its lifetime.");
    }

    [Test]
    public void ScalesTheShotByItsOwnDamageOverTheWeaponBaseline()
    {
        // Baseline weapon damage is 1, so each point above it grows the shot
        // by the serialized per-damage factor; the baseline shot itself is 1x.
        WeaponData weapon = TestData.CreateWeaponData(damage: 1, projectileSpeed: 10f, projectileLifetime: 5f,
            visualScalePerDamage: 0.5f);
        var go = new GameObject("Shot");
        _shotObjects.Add(go);
        Projectile shot = go.AddComponent<Projectile>();

        shot.Launch(Vector2.right, weapon, damage: 1);
        Assert.AreEqual(1f, shot.transform.localScale.x, 0.01f,
            "A shot at the weapon's own damage must keep the prefab's size.");

        shot.Launch(Vector2.right, weapon, damage: 3);
        Assert.AreEqual(2f, shot.transform.localScale.x, 0.01f,
            "Two points above the baseline at 0.5 per point must double the shot.");
        Assert.AreEqual(2f, shot.transform.localScale.y, 0.01f,
            "The growth must be uniform, not stretched along the heading.");
    }

    [Test]
    public void Relaunch_ResetsTheScaleInsteadOfStacking()
    {
        // The pool reuses shot objects, so the scale of the previous life must
        // not leak into the next one: every launch assigns the scale anew.
        WeaponData weapon = TestData.CreateWeaponData(damage: 1, projectileSpeed: 10f, projectileLifetime: 5f,
            visualScalePerDamage: 0.5f, maxVisualScale: 2.5f);
        var go = new GameObject("Shot");
        _shotObjects.Add(go);
        Projectile shot = go.AddComponent<Projectile>();

        shot.Launch(Vector2.right, weapon, damage: 3);
        shot.Launch(Vector2.right, weapon, damage: 5);
        Assert.AreEqual(2.5f, shot.transform.localScale.x, 0.01f,
            "A relaunched, bigger shot must be re-assigned, not grown again; the cap applies.");

        shot.Launch(Vector2.right, weapon, damage: 1);
        Assert.AreEqual(1f, shot.transform.localScale.x, 0.01f,
            "A relaunched baseline shot must shrink back to the prefab's size.");

        shot.Launch(Vector2.zero, weapon, damage: 3);
        Assert.AreEqual(2f, shot.transform.localScale.x, 0.01f,
            "Even a degenerate direction must still arm the scale for the new flight.");
    }
}
