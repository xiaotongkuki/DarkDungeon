using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the monster animation driver: the Moving flag driven by
/// recovered motion, the side flip on horizontal travel, and the state rewind
/// that makes pooled reuse safe.
///
/// The chase itself is not simulated: the test walks the body's transform the
/// way the kinematic mover does, which is exactly the motion the driver reads.
/// </summary>
public class EnemyAnimatorDriverTests
{
    /// <summary>Units to walk per simulated frame while "moving".</summary>
    private const float WalkStep = 0.06f;

    private GameObject _enemy;
    private Animator _animator;

    [SetUp]
    public void SetUp()
    {
        // The real prefab: its data asset and its animator controller come with
        // it, which is the setup a live spawn uses.
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy.prefab");
        Assert.IsNotNull(prefab, "Enemy.prefab must exist for this test.");

        _enemy = Object.Instantiate(prefab);
        _animator = _enemy.GetComponent<Animator>();
        Assert.IsNotNull(_animator, "The spawned enemy must carry its Animator.");
        Assert.IsNotNull(_animator.runtimeAnimatorController, "The controller must be assigned on the prefab.");
    }

    [TearDown]
    public void TearDown()
    {
        if (_enemy != null)
        {
            Object.DestroyImmediate(_enemy);
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

    [UnityTest]
    public IEnumerator WalkingBody_SwitchesToRun_AndFlipsWhenHeadingLeft()
    {
        // First sampled frame only seeds the position; walk from the next one.
        yield return null;

        for (int i = 0; i < 12; i++)
        {
            _enemy.transform.position += Vector3.right * WalkStep;
            yield return null;
        }

        Assert.IsTrue(_animator.GetBool("Moving"),
            "A body advancing each frame must reach the Moving state.");

        for (int i = 0; i < 12; i++)
        {
            _enemy.transform.position += Vector3.left * WalkStep * 3f;
            yield return null;
        }

        SpriteRenderer renderer = _enemy.GetComponent<SpriteRenderer>();
        Assert.IsTrue(renderer.flipX, "Heading left must flip the right-facing pose.");
    }

    [UnityTest]
    public IEnumerator StandingStill_ReturnsToIdle()
    {
        yield return null;

        for (int i = 0; i < 10; i++)
        {
            _enemy.transform.position += Vector3.right * WalkStep;
            yield return null;
        }

        for (int f = 0; f < 15; f++)
        {
            yield return null;
        }

        Assert.IsFalse(_animator.GetBool("Moving"),
            "A body that stops advancing must drop back to Idle.");
    }

    [UnityTest]
    public IEnumerator PooledReuse_Rewinds_AnimatorState()
    {
        yield return null;
        for (int i = 0; i < 10; i++)
        {
            _enemy.transform.position += Vector3.right * WalkStep;
            yield return null;
        }
        Assert.IsTrue(_animator.GetBool("Moving"));

        // A pooled retire: deactivate, then hand back out.
        _enemy.SetActive(false);
        _enemy.SetActive(true);
        yield return null;

        Assert.IsFalse(_animator.GetBool("Moving"),
            "Reuse must start from Idle, not the previous life's Run state.");
    }
}
