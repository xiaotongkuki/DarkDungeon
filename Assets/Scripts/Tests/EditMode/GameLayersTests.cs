using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pins the project's layer scheme and collision matrix.
///
/// Both live in Project Settings rather than in code, so without these tests a
/// change made in the editor would only surface as altered gameplay.
///
/// The layer <i>names</i> are deliberately not asserted: in this project
/// TagManager.asset gets rewritten from a stale in-memory copy within seconds
/// (observed repeatedly), so "Player" and "Enemy" keep disappearing from the
/// editor's layer dropdowns. The indices and the collision matrix persist, and the
/// indices are all that gameplay code depends on.
/// </summary>
public class GameLayersTests
{
    [Test]
    public void MasksMatchTheirLayerIndices()
    {
        Assert.AreEqual(1 << GameLayers.Player, GameLayers.PlayerMask.value);
        Assert.AreEqual(1 << GameLayers.Enemy, GameLayers.EnemyMask.value);
    }

    [Test]
    public void EnemyPrefabSitsOnTheEnemyLayer()
    {
        // The prefab's layer is what decides which collision pairs its contacts
        // generate, so it has to agree with the constants the code reasons about.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy.prefab");

        Assert.IsNotNull(prefab, "The enemy prefab must exist for the game to spawn anything.");
        Assert.AreEqual(GameLayers.Enemy, prefab.layer,
            "The enemy prefab moved off the Enemy layer.");
    }

    [Test]
    public void EnemiesIgnoreEachOtherAndScenery()
    {
        Assert.IsTrue(Physics2D.GetIgnoreLayerCollision(GameLayers.Enemy, GameLayers.Enemy),
            "Enemies are meant to overlap instead of shoving each other.");
        Assert.IsTrue(Physics2D.GetIgnoreLayerCollision(GameLayers.Enemy, GameLayers.Scenery),
            "Enemies are meant to walk through walls and obstacles.");
    }

    [Test]
    public void EnemiesCollideWithThePlayer()
    {
        Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(GameLayers.Enemy, GameLayers.Player),
            "Enemy/player contact is what deals contact damage, so the pair must collide.");
    }

    [Test]
    public void PlayerCollidesWithScenery()
    {
        Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(GameLayers.Player, GameLayers.Scenery),
            "The player must still be stopped by walls.");
    }
}
