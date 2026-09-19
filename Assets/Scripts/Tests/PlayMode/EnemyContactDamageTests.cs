using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for enemy contact damage: overlapping a damageable target hurts
/// it, the target's invulnerability window governs the rate, and contacts that
/// carry no damageable component are ignored.
/// </summary>
public class EnemyContactDamageTests
{
    /// <summary>Pool size used by these tests.</summary>
    private const int PlayerMaxHealth = 100;

    /// <summary>Invulnerability window used by these tests, in seconds.</summary>
    private const float Invulnerability = 0.3f;

    private GameObject _managerObject;
    private GameObject _playerObject;
    private GameObject _enemyObject;
    private PlayerHealth _playerHealth;

    [SetUp]
    public void SetUp()
    {
        _managerObject = new GameObject("EnemyManager");
        _managerObject.AddComponent<EnemyManager>();

        // The player mirrors the real setup: dynamic body, solid collider, on the
        // Player layer. Built inactive so the data asset can be assigned before
        // Awake runs: the component rejects a missing asset.
        _playerObject = new GameObject("Player");
        _playerObject.SetActive(false);
        _playerObject.layer = GameLayers.Player;
        var playerBody = _playerObject.AddComponent<Rigidbody2D>();
        playerBody.gravityScale = 0f;
        playerBody.freezeRotation = true;
        _playerObject.AddComponent<CircleCollider2D>().radius = 0.45f;
        _playerHealth = _playerObject.AddComponent<PlayerHealth>();

        // A generous pool with a short invulnerability window, so the rate of damage
        // is measurable without the pool running dry mid-test.
        TestData.SetObjectReference(_playerHealth, "_data",
            TestData.CreatePlayerData(maxHealth: PlayerMaxHealth, invulnerabilitySeconds: Invulnerability));
        _playerObject.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        if (_enemyObject != null)
        {
            Object.DestroyImmediate(_enemyObject);
        }
        Object.DestroyImmediate(_playerObject);
        Object.DestroyImmediate(_managerObject);
    }

    /// <summary>
    /// Creates a stationary enemy on the enemy layer whose trigger collider
    /// overlaps the given position, mirroring the real prefab's physics setup.
    ///
    /// Built inactive so the data asset can be assigned before Awake runs: both the
    /// enemy and its contact behaviour reject a missing asset.
    /// </summary>
    /// <param name="position">World position to spawn at.</param>
    /// <param name="damage">Contact damage the enemy deals.</param>
    /// <returns>The created enemy object.</returns>
    private GameObject SpawnContactEnemy(Vector2 position, int damage)
    {
        var go = new GameObject("Enemy");
        go.SetActive(false);
        go.layer = GameLayers.Enemy;
        go.transform.position = position;

        var body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;

        var collider = go.AddComponent<CircleCollider2D>();
        collider.radius = 0.45f;
        collider.isTrigger = true;

        Enemy enemy = go.AddComponent<Enemy>();
        go.AddComponent<EnemyContactDamage>();
        TestData.SetObjectReference(enemy, "_data", TestData.CreateEnemyData(contactDamage: damage));

        go.SetActive(true);

        _enemyObject = go;
        return go;
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
    public IEnumerator OverlappingEnemyDamagesThePlayer()
    {
        SpawnContactEnemy(new Vector2(0.3f, 0f), damage: 2);

        yield return WaitForGameTime(0.2f);

        Assert.Less(_playerHealth.CurrentHealth, PlayerMaxHealth,
            "A touching enemy must deal contact damage.");
    }

    [UnityTest]
    public IEnumerator ContactDamageRespectsTheInvulnerabilityWindow()
    {
        // The enemy stays in contact for many physics steps, so without the target's
        // invulnerability window the pool would drain every step.
        SpawnContactEnemy(new Vector2(0.3f, 0f), damage: 1);

        yield return WaitForGameTime(Invulnerability * 2f + 0.1f);

        int lost = PlayerMaxHealth - _playerHealth.CurrentHealth;
        Assert.GreaterOrEqual(lost, 1, "Contact must hurt at all.");
        Assert.LessOrEqual(lost, 3,
            "The rate must be governed by the invulnerability window, not by the physics rate.");
    }

    [UnityTest]
    public IEnumerator ContactWithSomethingUndamageableIsIgnored()
    {
        // Parked well away from the player, so the enemy can only ever touch this
        // object and the player's health says nothing about that contact.
        var scenery = new GameObject("Undamageable");
        scenery.layer = GameLayers.Player; // same layer, so the pair still generates contacts
        scenery.transform.position = new Vector3(10f, 0f, 0f);
        var body = scenery.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        scenery.AddComponent<CircleCollider2D>().radius = 0.45f;

        GameObject enemy = SpawnContactEnemy(new Vector2(10.3f, 0f), damage: 5);

        yield return WaitForGameTime(0.2f);

        Assert.IsTrue(enemy != null, "A contact without a damageable target must be a no-op.");
        Assert.AreEqual(PlayerMaxHealth, _playerHealth.CurrentHealth,
            "The player is ten units away and must not be hurt by that contact.");

        Object.DestroyImmediate(scenery);
    }
}
