using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the held-gun grip: re-aiming at the nearest threat
/// through the spatial grid, the vertical flip when crossing to the far side,
/// and the muzzle flash burning out on its own clock.
/// </summary>
public class PlayerWeaponGripTests
{
    /// <summary>Grid cell size for these tests; small keeps queries tight.</summary>
    private const float CellSize = 2f;

    /// <summary>Horizontal offset (units) of the muzzle relative to the grip.</summary>
    private const float MuzzleOffset = 0.45f;

    private GameObject _managerObject;
    private GameObject _gripObject;
    private readonly List<GameObject> _enemies = new List<GameObject>();
    private readonly List<Sprite> _createdFrames = new List<Sprite>();
    private readonly List<Object> _tracked = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        _managerObject = new GameObject("EnemyManager");
        var manager = _managerObject.AddComponent<EnemyManager>();
        var so = new UnityEditor.SerializedObject(manager);
        so.FindProperty("_cellSize").floatValue = CellSize;
        so.ApplyModifiedProperties();
    }

    [TearDown]
    public void TearDown()
    {
        if (_managerObject != null)
        {
            Object.DestroyImmediate(_managerObject);
        }
        if (_gripObject != null)
        {
            Object.DestroyImmediate(_gripObject);
        }
        foreach (var e in _enemies)
        {
            if (e != null)
            {
                Object.DestroyImmediate(e);
            }
        }
        _enemies.Clear();
        foreach (var frame in _createdFrames)
        {
            if (frame != null)
            {
                Object.DestroyImmediate(frame);
            }
        }
        _createdFrames.Clear();
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
    /// Creates a grip (inactive first, wired, then activated - the project's
    /// standard arming pattern) with a muzzle point and a flash child using
    /// three runtime-created frames.
    /// </summary>
    private PlayerWeaponGrip CreateGrip()
    {
        _gripObject = new GameObject("WeaponGrip");
        _gripObject.transform.position = Vector3.zero;
        _gripObject.SetActive(false);

        return BuildGrip(_gripObject);
    }

    /// <summary>
    /// Creates the grip as a child of an existing (inactive) body, following
    /// the same inactive-then-armed order.
    /// </summary>
    /// <param name="body">Inactive player body.</param>
    /// <returns>The armed grip component.</returns>
    private PlayerWeaponGrip CreateGripUnder(Transform body)
    {
        _gripObject = new GameObject("WeaponGrip");
        _gripObject.transform.SetParent(body, false);
        _gripObject.transform.localPosition = Vector3.zero;

        return BuildGrip(_gripObject);
    }

    /// <summary>
    /// Shared body of both factory paths: renderer, muzzle point, flash child
    /// with three runtime frames, serialized wiring, then activation.
    /// </summary>
    /// <param name="gripObject">The grip GameObject to fill.</param>
    /// <returns>The armed grip component.</returns>
    private PlayerWeaponGrip BuildGrip(GameObject gripRoot)
    {
        var renderer = gripRoot.AddComponent<SpriteRenderer>();
        renderer.sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Weapon/Free - Gun.png");

        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(gripRoot.transform, false);
        muzzle.localPosition = new Vector3(MuzzleOffset, 0f, 0f);

        var flashObject = new GameObject("MuzzleFlash");
        flashObject.transform.SetParent(gripRoot.transform, false);
        flashObject.transform.localPosition = new Vector3(MuzzleOffset, 0f, 0f);
        var flashRenderer = flashObject.AddComponent<SpriteRenderer>();

        var frames = new Sprite[3];
        for (int i = 0; i < 3; i++)
        {
            var frame = Sprite.Create(new Texture2D(4, 4), new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 48f);
            _createdFrames.Add(frame);
            frames[i] = frame;
        }

        var grip = gripRoot.AddComponent<PlayerWeaponGrip>();
        var so = new UnityEditor.SerializedObject(grip);
        so.FindProperty("_muzzle").objectReferenceValue = muzzle;
        so.FindProperty("_flash").objectReferenceValue = flashRenderer;
        var array = so.FindProperty("_flashFrames");
        array.arraySize = 3;
        for (int i = 0; i < 3; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = framesFrame(frames, i);
        }
        so.ApplyModifiedProperties();

        gripRoot.SetActive(true);
        return grip;
    }

    /// <summary>
    /// Returns the i-th of the created frames; keeps the wiring loop readable.
    /// </summary>
    /// <param name="frames">Runtime frames created for this fixture.</param>
    /// <param name="index">Frame slot.</param>
    /// <returns>The frame sprite.</returns>
    private static Sprite framesFrame(Sprite[] frames, int index)
    {
        return frames[index];
    }

    /// <summary>
    /// Creates a registered enemy whose only job is to be the nearest target.
    /// </summary>
    private Enemy SpawnEnemy(Vector2 position)
    {
        var go = new GameObject("Target");
        go.SetActive(false);
        go.transform.position = position;
        var enemy = go.AddComponent<Enemy>();
        TestData.SetObjectReference(enemy, "_data", TestData.CreateEnemyData(maxHealth: 3));
        go.SetActive(true);
        _enemies.Add(go);
        return enemy;
    }

    [UnityTest]
    public IEnumerator Grip_AimsAtTheNearestEnemy()
    {
        PlayerWeaponGrip grip = CreateGrip();
        SpawnEnemy(new Vector2(0f, 6f)); // straight up

        yield return null;
        yield return null;

        float angle = grip.transform.eulerAngles.z;
        Assert.AreEqual(90f, angle, 1f, "An enemy straight above must turn the barrel up.");
    }

    [UnityTest]
    public IEnumerator Grip_FlipsTheSpriteWhenAimingLeft()
    {
        PlayerWeaponGrip grip = CreateGrip();
        SpawnEnemy(new Vector2(-6f, 0f)); // straight left

        yield return null;
        yield return null;

        SpriteRenderer renderer = grip.GetComponent<SpriteRenderer>();
        Assert.IsTrue(renderer.flipY, "Aiming left must flip the right-facing gun sprite vertically.");
    }

    [UnityTest]
    public IEnumerator Grip_AimsForwardWhenNoEnemyIsAlive()
    {
        PlayerWeaponGrip grip = CreateGrip(); // nothing spawned

        yield return null;
        yield return null;

        Assert.AreEqual(0f, grip.transform.eulerAngles.z, 1f,
            "With no threats the barrel must relax to its authored right-facing pose.");
    }

    [UnityTest]
    public IEnumerator Grip_IgnoresEnemiesBeyondTheWeaponRing()
    {
        // The shooter is assembled in the project's arming order: inactive
        // body, weapon wired, child grip built under it, then activate - so
        // both the attack's Awake and the grip's Awake see their real parents.
        var shooter = new GameObject("Player");
        shooter.SetActive(false);
        var attack = shooter.AddComponent<PlayerAutoAttack>();
        var weapon = TestData.CreateWeaponData(damage: 1, range: 2f);
        _tracked.Add(weapon);
        var aso = new UnityEditor.SerializedObject(attack);
        aso.FindProperty("_weapon").objectReferenceValue = weapon;
        aso.ApplyModifiedProperties();

        PlayerWeaponGrip grip = CreateGripUnder(shooter.transform);

        // An enemy far beyond the 2-unit ring, then one just inside it.
        SpawnEnemy(new Vector2(9f, 0f));
        shooter.SetActive(true);
        yield return null;
        yield return null;

        Assert.AreEqual(0f, grip.transform.eulerAngles.z, 1f,
            "An enemy outside the weapon's ring must not be tracked: firing will not reach it.");

        SpawnEnemy(new Vector2(1.5f, 0f)); // inside the ring, straight ahead
        yield return null;
        yield return null;

        Assert.AreEqual(0f, grip.transform.eulerAngles.z, 1f,
            "An inside-ring enemy straight ahead still aims forward (0 degrees).");
    }

    [UnityTest]
    public IEnumerator MuzzleFlash_BurnsThroughItsWindow_ThenHides()
    {
        PlayerWeaponGrip grip = CreateGrip();
        yield return null;

        grip.PlayMuzzleFlash();

        // The grip's own renderer is the gun; the flash is the named child.
        Transform flashChild = grip.transform.Find("MuzzleFlash");
        Assert.IsNotNull(flashChild, "The flash child must exist.");
        SpriteRenderer flash = flashChild.GetComponent<SpriteRenderer>();

        // Burning: the flash is visible right after the trigger, before the
        // 0.09 s window is over.
        Assert.IsTrue(flash.enabled, "Right after firing the flash must be visible.");

        yield return WaitForGameTime(0.2f);

        Assert.IsFalse(flash.enabled, "After the window the flash must be hidden again.");
    }
}
