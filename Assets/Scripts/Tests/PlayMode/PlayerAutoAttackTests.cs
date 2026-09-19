using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for auto attack: it must fire on its cadence at the closest
/// enemy in range, stay silent with nothing to shoot, and survive being set up in
/// any order.
/// </summary>
public class PlayerAutoAttackTests
{
    /// <summary>Cell size for these tests; small cells keep the grid honest.</summary>
    private const float CellSize = 2f;

    private GameObject _managerObject;
    private GameObject _playerObject;
    private GameObject _shotPrefabObject;
    private Projectile _shotPrefab;
    private WeaponData _weapon;
    private EnemyManager _manager;
    private PlayerAutoAttack _attack;
    private readonly List<GameObject> _enemyObjects = new List<GameObject>();

    [SetUp]
    public void SetUp()
    {
        _managerObject = new GameObject("EnemyManager");
        _manager = _managerObject.AddComponent<EnemyManager>();
        SetFloat(_manager, "_cellSize", CellSize);

        // Runtime stand-in for the shot prefab. It carries no flight values of its
        // own: those come from the weapon asset, which is why it stays idle until
        // something launches it.
        _shotPrefabObject = new GameObject("ShotPrefab");
        _shotPrefab = _shotPrefabObject.AddComponent<Projectile>();

        // Lifetime is effectively infinite so the number of live shots equals the
        // number of shots fired, which is what the cadence assertions measure; speed
        // is zero so the shots stay out of the way.
        _weapon = TestData.CreateWeaponData(_shotPrefab, fireInterval: 0.1f, range: 8f, damage: 1,
            projectileSpeed: 0f, projectileLifetime: 100000f);

        // Built inactive so the weapon asset can be assigned before Awake runs: the
        // component rejects a missing asset, and AddComponent on a live object would
        // log that error and fail the test.
        _playerObject = new GameObject("Player");
        _playerObject.SetActive(false);
        _playerObject.transform.position = Vector3.zero;
        _attack = _playerObject.AddComponent<PlayerAutoAttack>();
        TestData.SetObjectReference(_attack, "_weapon", _weapon);
        _playerObject.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Projectile shot in Object.FindObjectsOfType<Projectile>())
        {
            if (shot != null)
            {
                Object.DestroyImmediate(shot.gameObject);
            }
        }

        for (int i = 0; i < _enemyObjects.Count; i++)
        {
            if (_enemyObjects[i] != null)
            {
                Object.DestroyImmediate(_enemyObjects[i]);
            }
        }
        _enemyObjects.Clear();

        Object.DestroyImmediate(_playerObject);
        Object.DestroyImmediate(_shotPrefabObject);
        Object.DestroyImmediate(_managerObject);
    }

    /// <summary>
    /// Creates a stationary enemy at the given position. No <see cref="EnemyAI"/> is
    /// added, so the enemy stays exactly where it was put.
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

    /// <summary>Counts live shots, excluding the instantiate source.</summary>
    /// <returns>Number of shots in flight.</returns>
    private int CountShots()
    {
        int count = 0;
        foreach (Projectile shot in Object.FindObjectsOfType<Projectile>())
        {
            if (shot != _shotPrefab)
            {
                count++;
            }
        }
        return count;
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
    public IEnumerator Fires_WhenAnEnemyIsInRange()
    {
        SpawnEnemy(new Vector2(3f, 0f));

        yield return WaitForGameTime(0.35f);

        Assert.GreaterOrEqual(CountShots(), 1, "An enemy in range must draw fire.");
    }

    [UnityTest]
    public IEnumerator DoesNotFire_WithoutEnemies()
    {
        yield return WaitForGameTime(0.35f);

        Assert.AreEqual(0, CountShots(), "There is nothing to shoot at.");
    }

    [UnityTest]
    public IEnumerator DoesNotFire_WhenTheEnemyIsOutOfRange()
    {
        // Range is 8, so this enemy is well outside it.
        SpawnEnemy(new Vector2(20f, 0f));

        yield return WaitForGameTime(0.35f);

        Assert.AreEqual(0, CountShots(), "Out-of-range enemies must not be targeted.");
    }

    [UnityTest]
    public IEnumerator FiresOnItsCadence()
    {
        SpawnEnemy(new Vector2(3f, 0f));

        yield return WaitForGameTime(1f);

        // 1 second at a 0.1 s cadence is about 10 shots; the bounds stay loose so a
        // slow frame cannot flake the assertion.
        int shots = CountShots();
        Assert.GreaterOrEqual(shots, 7, "The attack must keep firing on its cadence.");
        Assert.LessOrEqual(shots, 12, "The attack must not fire faster than its cadence.");
    }

    [UnityTest]
    public IEnumerator FiresAtTheClosestEnemy()
    {
        // Unlike the cadence tests, which park the shots so the live count equals the
        // number of shots fired, this test needs them to actually travel and land.
        SetFloat(_weapon, "_projectileSpeed", 12f);

        // The nearer enemy is along +Y, the further one along +X, so which one takes
        // damage says which one was targeted. Asserting on damage rather than on a
        // kill keeps the test about targeting, not about lethality.
        Enemy near = SpawnEnemy(new Vector2(0f, 3f));
        Enemy far = SpawnEnemy(new Vector2(6f, 0f));

        yield return WaitForGameTime(0.5f);

        Assert.Less(near.CurrentHealth, 3, "The closest enemy must be the one shot at.");
        Assert.AreEqual(3, far.CurrentHealth, "The further enemy must be left alone.");
    }

    [UnityTest]
    public IEnumerator FiresAfterTheManagerAppearsLater()
    {
        // Rebuild the scene without a manager: the attack must stay quiet instead of
        // throwing, and must pick the manager up once it exists.
        Object.DestroyImmediate(_managerObject);
        _managerObject = null;

        yield return WaitForGameTime(0.25f);
        Assert.AreEqual(0, CountShots(), "Without a registry there is no targeting and no firing.");

        _managerObject = new GameObject("EnemyManager");
        _manager = _managerObject.AddComponent<EnemyManager>();
        SetFloat(_manager, "_cellSize", CellSize);

        // The enemy has to arrive after the manager: an enemy enabled with no
        // registry logs a warning and never registers.
        SpawnEnemy(new Vector2(3f, 0f));

        yield return WaitForGameTime(0.35f);

        Assert.GreaterOrEqual(CountShots(), 1, "The attack must pick up a manager that appears later.");
    }

    [UnityTest]
    public IEnumerator DoesNotFire_WithoutAShotPrefab()
    {
        TestData.SetObjectReference(_weapon, "_projectilePrefab", null);
        SpawnEnemy(new Vector2(3f, 0f));

        yield return WaitForGameTime(0.35f);

        Assert.AreEqual(0, CountShots(), "A missing prefab is a configuration error, not a crash.");
    }

    [UnityTest]
    public IEnumerator Attack_RecyclesRetiredShotsInsteadOfCreatingNewOnes()
    {
        // A lifetime barely longer than the firing interval means each shot retires
        // almost immediately, so a second of firing says whether the attacker reaches
        // for a pool or for Instantiate.
        WeaponData shortLived = TestData.CreateWeaponData(_shotPrefab, fireInterval: 0.05f, range: 8f,
            damage: 1, projectileSpeed: 0f, projectileLifetime: 0.05f);
        TestData.SetObjectReference(_attack, "_weapon", shortLived);

        // Enough health that the target outlives the measurement.
        SpawnEnemy(new Vector2(2f, 0f), health: 1000);

        // Every distinct shot instance seen over the window. Recycling keeps this close
        // to the number of shots alive at once; creating one per shot would put it at
        // the number of shots fired.
        var distinct = new HashSet<int>();
        float deadline = Time.time + 1f;
        while (Time.time < deadline)
        {
            foreach (Projectile shot in Object.FindObjectsOfType<Projectile>())
            {
                if (shot != _shotPrefab)
                {
                    distinct.Add(shot.GetInstanceID());
                }
            }

            yield return null;
        }

        Assert.Greater(distinct.Count, 0, "The attack must have fired for recycling to be judged.");
        Assert.LessOrEqual(distinct.Count, 4,
            $"Firing for a second produced {distinct.Count} distinct shots; retired shots must be recycled.");

        Object.DestroyImmediate(shortLived);
    }
}
