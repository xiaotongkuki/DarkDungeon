using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the player locator singleton: it exposes the player
/// transform, steps aside for a duplicate, and releases its slot on teardown so a
/// later scene or test can register a new player.
/// </summary>
public class PlayerLocatorTests
{
    private GameObject _playerObject;

    [TearDown]
    public void TearDown()
    {
        if (_playerObject != null)
        {
            Object.DestroyImmediate(_playerObject);
        }

        // Safety net in case a test left a different locator registered.
        if (PlayerLocator.Instance != null)
        {
            Object.DestroyImmediate(PlayerLocator.Instance.gameObject);
        }
    }

    [Test]
    public void Target_ExposesThePlayerTransform()
    {
        _playerObject = new GameObject("Player");
        PlayerLocator locator = _playerObject.AddComponent<PlayerLocator>();

        Assert.AreSame(locator, PlayerLocator.Instance);
        Assert.AreSame(_playerObject.transform, locator.Target);
    }

    [Test]
    public void DestroyingTheLocator_ReleasesTheSingletonSlot()
    {
        _playerObject = new GameObject("Player");
        _playerObject.AddComponent<PlayerLocator>();
        Assert.IsTrue(PlayerLocator.Instance != null);

        Object.DestroyImmediate(_playerObject);
        _playerObject = null;

        Assert.IsTrue(PlayerLocator.Instance == null, "Destroying the player must release the slot.");
    }

    [UnityTest]
    public IEnumerator Duplicate_DefersToTheFirstInstance()
    {
        _playerObject = new GameObject("Player");
        PlayerLocator first = _playerObject.AddComponent<PlayerLocator>();

        var duplicateObject = new GameObject("PlayerDuplicate");
        PlayerLocator duplicate = duplicateObject.AddComponent<PlayerLocator>();

        Assert.AreSame(first, PlayerLocator.Instance, "The first locator must keep the singleton slot.");

        yield return null;

        Assert.IsTrue(duplicate == null, "The duplicate component must remove itself.");

        Object.DestroyImmediate(duplicateObject);
    }
}
