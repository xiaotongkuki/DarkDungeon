using UnityEngine;

/// <summary>
/// The player's accumulated upgrade bonuses.
///
/// Every gameplay system reads its base value from a data asset and adds the
/// matching bonus from here. That keeps upgrades out of the assets (which are
/// shared and must stay read-only) and keeps each system from having to know about
/// the upgrade system at all.
///
/// Optional component: a player without it simply gets no bonuses, which is what
/// lets the individual systems stay testable on their own.
/// </summary>
public class PlayerStatModifiers : MonoBehaviour
{
    /// <summary>Flat bonus to each shot's damage.</summary>
    public float DamageBonus { get; private set; }

    /// <summary>Multiplier on the weapon's fire interval; below 1 fires faster.</summary>
    public float FireIntervalScale { get; private set; } = 1f;

    /// <summary>Flat bonus to top movement speed, in units per second.</summary>
    public float MoveSpeedBonus { get; private set; }

    /// <summary>Flat bonus to the health pool.</summary>
    public float MaxHealthBonus { get; private set; }

    /// <summary>Flat bonus to the gem magnet radius, in units.</summary>
    public float MagnetRadiusBonus { get; private set; }

    /// <summary>
    /// Clears every bonus, so a fresh run (or a reused player object) starts
    /// unmodified.
    /// </summary>
    private void OnEnable()
    {
        ResetAll();
    }

    /// <summary>
    /// Clears every bonus back to its neutral value.
    /// </summary>
    public void ResetAll()
    {
        DamageBonus = 0f;
        FireIntervalScale = 1f;
        MoveSpeedBonus = 0f;
        MaxHealthBonus = 0f;
        MagnetRadiusBonus = 0f;
    }

    /// <summary>
    /// Folds one upgrade into the totals and tells the systems that copied a value
    /// at startup to re-read it (the firing cadence, for one).
    /// </summary>
    /// <param name="upgrade">Upgrade to apply; null is ignored.</param>
    public void Apply(UpgradeData upgrade)
    {
        if (upgrade == null)
        {
            return;
        }

        switch (upgrade.Stat)
        {
            case UpgradeStat.Damage:
                DamageBonus += upgrade.Amount;
                break;
            case UpgradeStat.FireRate:
                // Multiplied rather than added: "15% faster" compounds, and a flat
                // subtraction from the interval would eventually reach zero.
                FireIntervalScale *= upgrade.Amount;
                break;
            case UpgradeStat.MoveSpeed:
                MoveSpeedBonus += upgrade.Amount;
                break;
            case UpgradeStat.MaxHealth:
                MaxHealthBonus += upgrade.Amount;
                break;
            case UpgradeStat.MagnetRadius:
                MagnetRadiusBonus += upgrade.Amount;
                break;
        }

        EventBus.RaiseStatsChanged();
    }
}
