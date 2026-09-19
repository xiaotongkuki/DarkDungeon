using UnityEngine;

/// <summary>
/// The upgrades the level-up choice panel may offer.
///
/// One pool asset rather than a hard-coded list, so the catalogue can grow or be
/// swapped (a different pool per game mode, for instance) without code.
/// </summary>
[CreateAssetMenu(fileName = "UpgradePoolData", menuName = "My project/Data/Upgrade Pool")]
public class UpgradePoolData : ScriptableObject
{
    [Tooltip("Upgrades the panel draws from. Needs at least as many entries as the panel offers choices.")]
    [SerializeField] private UpgradeData[] _upgrades;

    /// <summary>Number of upgrades in the pool.</summary>
    public int Count => _upgrades != null ? _upgrades.Length : 0;

    /// <summary>
    /// Reads one entry by index.
    /// </summary>
    /// <param name="index">Zero-based index into the pool.</param>
    /// <returns>The upgrade, or null when the index is out of range.</returns>
    public UpgradeData Get(int index)
    {
        if (_upgrades == null || index < 0 || index >= _upgrades.Length)
        {
            return null;
        }

        return _upgrades[index];
    }
}
