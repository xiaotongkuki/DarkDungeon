using UnityEngine;

/// <summary>
/// Scene-wide access point for the player's transform.
///
/// Enemies are prefab instances and a prefab asset cannot reference a scene
/// object, so the player reference has to be resolved at runtime. Instead of
/// every enemy searching for the player (FindWithTag allocates and is easy to get
/// wrong), the player registers itself here once and consumers read
/// <see cref="Target"/>. Auto-attack and UI can reuse it later.
/// </summary>
public class PlayerLocator : MonoBehaviour
{
    /// <summary>The active locator, claimed in Awake and released on destroy.</summary>
    public static PlayerLocator Instance { get; private set; }

    /// <summary>Transform of the player object; non-null while an instance exists.</summary>
    public Transform Target => transform;

    /// <summary>
    /// Claims the singleton slot; a second locator disables itself so every lookup
    /// resolves to the same player.
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"Duplicate PlayerLocator on '{name}' removed; keeping '{Instance.name}'.", this);
            Destroy(this);
            return;
        }

        Instance = this;
    }

    /// <summary>
    /// Releases the singleton slot so a reloaded scene, or a test teardown, can
    /// register a new player.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
