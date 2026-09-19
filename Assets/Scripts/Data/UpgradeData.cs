using UnityEngine;

/// <summary>
/// One upgrade the player can be offered on level-up: what it is called, what it
/// does, and by how much.
///
/// Assets are the whole upgrade catalogue - adding a new kind of upgrade is a new
/// asset in the pool, not a new branch in the choice panel.
/// </summary>
[CreateAssetMenu(fileName = "UpgradeData", menuName = "My project/Data/Upgrade")]
public class UpgradeData : ScriptableObject
{
    [Header("Presentation")]
    [Tooltip("Name shown on the choice button.")]
    [SerializeField] private string _displayName = "Upgrade";
    [Tooltip("One line explaining what the upgrade does, shown under the name.")]
    [SerializeField] private string _description = "";

    [Header("Effect")]
    [Tooltip("Which stat this upgrade raises.")]
    [SerializeField] private UpgradeStat _stat = UpgradeStat.Damage;
    [Tooltip("Amount added to the stat, or multiplied into it for fire rate.")]
    [SerializeField] private float _amount = 1f;

    /// <summary>Name shown on the choice button.</summary>
    public string DisplayName => _displayName;

    /// <summary>One line explaining what the upgrade does.</summary>
    public string Description => _description;

    /// <summary>Which stat this upgrade raises.</summary>
    public UpgradeStat Stat => _stat;

    /// <summary>Amount added to the stat, or multiplied into it for fire rate.</summary>
    public float Amount => _amount;
}
