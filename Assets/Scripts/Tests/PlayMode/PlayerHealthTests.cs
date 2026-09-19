using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the player's health pool: damage, the invulnerability
/// window that keeps a crowd from killing in one frame, death, and respawn on
/// re-enable.
/// </summary>
public class PlayerHealthTests
{
    /// <summary>Pool size used by these tests.</summary>
    private const int MaxHealth = 3;

    /// <summary>Invulnerability window used by these tests, in seconds.</summary>
    private const float Invulnerability = 0.2f;

    private GameObject _playerObject;
    private PlayerHealth _health;

    [SetUp]
    public void SetUp()
    {
        // Built inactive so the data asset can be assigned before Awake runs: the
        // component rejects a missing asset, and AddComponent on a live object would
        // log that error and fail the test.
        _playerObject = new GameObject("Player");
        _playerObject.SetActive(false);
        _health = _playerObject.AddComponent<PlayerHealth>();
        TestData.SetObjectReference(_health, "_data",
            TestData.CreatePlayerData(maxHealth: MaxHealth, invulnerabilitySeconds: Invulnerability));
        _playerObject.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        if (_playerObject != null)
        {
            Object.DestroyImmediate(_playerObject);
        }
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

    [Test]
    public void StartsAliveWithAFullPool()
    {
        Assert.IsTrue(_health.IsAlive);
        Assert.AreEqual(MaxHealth, _health.CurrentHealth);
        Assert.IsFalse(_health.IsInvulnerable);
    }

    [Test]
    public void TakesDamage()
    {
        _health.TakeDamage(1);

        Assert.AreEqual(MaxHealth - 1, _health.CurrentHealth);
        Assert.IsTrue(_health.IsAlive);
    }

    [Test]
    public void IgnoresNonPositiveDamage()
    {
        _health.TakeDamage(0);
        _health.TakeDamage(-5);

        Assert.AreEqual(MaxHealth, _health.CurrentHealth);
    }

    [Test]
    public void IgnoresRepeatedDamageDuringTheInvulnerabilityWindow()
    {
        // A crowd delivers many hits in the same frame; only the first may land.
        _health.TakeDamage(1);
        _health.TakeDamage(1);
        _health.TakeDamage(1);

        Assert.AreEqual(MaxHealth - 1, _health.CurrentHealth,
            "Hits inside the invulnerability window must not stack.");
    }

    [UnityTest]
    public IEnumerator AcceptsDamageAgainOnceInvulnerabilityExpires()
    {
        _health.TakeDamage(1);

        yield return WaitForGameTime(Invulnerability + 0.05f);

        _health.TakeDamage(1);

        Assert.AreEqual(MaxHealth - 2, _health.CurrentHealth);
    }

    [UnityTest]
    public IEnumerator LethalDamageKillsAndDeactivatesThePlayer()
    {
        for (int i = 0; i < MaxHealth; i++)
        {
            _health.TakeDamage(1);
            yield return WaitForGameTime(Invulnerability + 0.05f);
        }

        Assert.IsFalse(_health.IsAlive);
        Assert.IsFalse(_playerObject.activeSelf,
            "Dying deactivates the player, which is the placeholder game-over state.");
    }

    [Test]
    public void DeadPlayerIgnoresFurtherDamage()
    {
        _health.TakeDamage(MaxHealth);

        Assert.IsFalse(_health.IsAlive);

        _health.TakeDamage(1);

        Assert.AreEqual(0, _health.CurrentHealth, "A dead pool must not go negative or revive.");
    }

    [Test]
    public void ReEnablingRespawnsWithAFullPool()
    {
        _health.TakeDamage(MaxHealth);
        Assert.IsFalse(_health.IsAlive);

        _playerObject.SetActive(true);

        Assert.IsTrue(_health.IsAlive, "Re-enabling the player is the respawn path.");
        Assert.AreEqual(MaxHealth, _health.CurrentHealth);
    }

    [UnityTest]
    public IEnumerator APlayerWithAnAnimator_StaysOnScreenForTheDeathPresentation()
    {
        // The fake players in this class have no Animator, so they exercise the
        // instant path. This one carries a working Animator, which is exactly the
        // production Player.prefab setup, and must get its presentation window:
        // active right after the fatal hit, gone only once the window is over.
        GameObject staged = new GameObject("PlayerStaged");
        staged.SetActive(false);
        PlayerHealth stagedHealth = staged.AddComponent<PlayerHealth>();
        TestData.SetObjectReference(stagedHealth, "_data",
            TestData.CreatePlayerData(maxHealth: MaxHealth, invulnerabilitySeconds: Invulnerability));
        TestData.SetFloat(stagedHealth, "_deathPresentationSeconds", 0.25f);

        Animator animator = staged.AddComponent<Animator>();
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/Animator/Player/Player.controller");
        Assert.IsNotNull(controller, "The Player animator controller must be present for this test.");
        animator.runtimeAnimatorController = controller;

        staged.SetActive(true);

        stagedHealth.TakeDamage(MaxHealth);

        // The death is announced immediately, but the body stays for the window.
        Assert.IsFalse(stagedHealth.IsAlive);
        Assert.IsTrue(staged.gameObject.activeSelf,
            "A staged death keeps the body active for the death animation instead of cutting it.");

        yield return WaitForGameTime(0.5f);

        Assert.IsFalse(staged.gameObject.activeSelf,
            "Once the presentation window is over, the body deactivates as before.");

        Object.DestroyImmediate(staged);
    }
}
