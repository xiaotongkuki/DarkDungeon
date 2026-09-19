using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the impact spark: visible at once, hidden after the
/// burn, pooled reuse across plays, and the scene-level no-op when the scene
/// carries no spawner.
/// </summary>
public class HitFxTests
{
    /// <summary>Burn time the effect prefab is armed with, in seconds.</summary>
    private const float Seconds = 0.15f;

    /// <summary>Parent object whose teardown also reclaims the pooled container.</summary>
    private GameObject _hostObject;

    /// <summary>Runtime objects this fixture created and must release itself.</summary>
    private readonly List<Object> _tracked = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        if (_hostObject != null)
        {
            Object.DestroyImmediate(_hostObject);
        }
        foreach (var o in _tracked)
        {
            if (o != null)
            {
                Object.DestroyImmediate(o);
            }
        }
        _tracked.Clear();
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

    /// <summary>
    /// Builds a spawner whose effect prefab is a plain runtime object: the pool
    /// instantiates that source directly, so no disk asset is needed. The
    /// prefab's frames are runtime sprites, tracked for teardown.
    /// </summary>
    private void CreateSpawner()
    {
        // Built inactive so the spawner's Awake sees its wired prefab rather
        //than running on a half-wired host - the same arming order the other
        // player/enemy components use.
        _hostObject = new GameObject("HitFxHost");
        _hostObject.SetActive(false);

        var effectSource = new GameObject("HitFxSource");
        effectSource.SetActive(false);
        effectSource.AddComponent<SpriteRenderer>();
        var effect = effectSource.AddComponent<HitFx>();
        var so = new UnityEditor.SerializedObject(effect);
        var frames = so.FindProperty("_frames");
        frames.arraySize = 3;
        for (int i = 0; i < frames.arraySize; i++)
        {
            var sprite = Sprite.Create(
                new Texture2D(4, 4), new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 32f);
            _tracked.Add(sprite);
            frames.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
        }
        so.ApplyModifiedProperties();

        var spawnerObject = new GameObject("HitFxSpawner");
        spawnerObject.transform.SetParent(_hostObject.transform, false);
        var spawner = spawnerObject.AddComponent<HitFxSpawner>();
        var spawnerSo = new UnityEditor.SerializedObject(spawner);
        spawnerSo.FindProperty("_prefab").objectReferenceValue = effect;
        spawnerSo.ApplyModifiedProperties();

        // The source stays alive (inactive): the prefab slot holds a real
        // component reference, and hosting it under the same teardown is the
        // simplest lifetime for a runtime asset.
        effectSource.transform.SetParent(_hostObject.transform, false);
        effectSource.name = "HitFxPrefabSource";
        _tracked.Add(effectSource);

        _hostObject.SetActive(true);
    }

    [Test]
    public void Play_WithoutASpawner_IsANoOp()
    {
        Assert.DoesNotThrow(() => HitFxSpawner.Play(Vector2.zero),
            "A scene without a spawner must tolerate shot impacts quietly.");
    }

    [UnityTest]
    public IEnumerator PlayedEffect_ShowsAtOnce_ThenParksWhenBurned()
    {
        CreateSpawner();

        HitFxSpawner.Play(new Vector2(3f, 2f));
        yield return null;

        HitFx effect = Object.FindObjectOfType<HitFx>();
        Assert.IsNotNull(effect, "One play must leave exactly one effect in the scene.");
        Assert.IsTrue(effect.gameObject.activeSelf, "A just-played effect must be active.");
        Assert.IsTrue(effect.GetComponent<SpriteRenderer>().enabled,
            "Its spark must be visible while burning.");

        yield return WaitForGameTime(Seconds + 0.4f);

        // A pooled retire parks the instance inactive under the pool container,
        // so "spent and reclaimed" reads as inactive-self.
        Assert.IsFalse(effect.gameObject.activeSelf,
            "After the burn the effect must retire back into its pool.");
        Assert.AreEqual("Pool_HitFx", effect.transform.parent.name,
            "Spent effects must live in their pool container for teardown reclaim.");
    }

    [UnityTest]
    public IEnumerator ASecondPlay_ReusesTheSameInstance()
    {
        CreateSpawner();

        HitFxSpawner.Play(new Vector2(1f, 1f));
        yield return null;
        HitFx first = Object.FindObjectOfType<HitFx>();

        yield return WaitForGameTime(Seconds + 0.4f); // first is fully parked

        HitFxSpawner.Play(new Vector2(2f, 2f));
        yield return null;

        // The prefab source itself is also a HitFx component (kept alive for
        // the asset slot), so search active ones only; there must be exactly
        // one live effect and it must be the same instance as the first play.
        HitFx active = Object.FindObjectOfType<HitFx>();
        Assert.IsNotNull(active, "The second play must leave a live effect.");
        Assert.AreEqual(new Vector2(2f, 2f), (Vector2)active.transform.position,
            "The live effect must sit where the second play armed it.");
        Assert.AreSame(first, active, "The second play must reuse the first's instance.");
    }
}
