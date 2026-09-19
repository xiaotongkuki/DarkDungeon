/// <summary>
/// Player stats an upgrade can raise.
///
/// Kept as an enum rather than a string so an upgrade asset and the code that
/// applies it cannot drift apart silently.
/// </summary>
public enum UpgradeStat
{
    /// <summary>Flat damage added to every shot.</summary>
    Damage,

    /// <summary>Multiplier on the weapon's fire interval; below 1 fires faster.</summary>
    FireRate,

    /// <summary>Flat bonus to top movement speed, in units per second.</summary>
    MoveSpeed,

    /// <summary>Flat bonus to the health pool.</summary>
    MaxHealth,

    /// <summary>Flat bonus to the gem magnet radius, in units.</summary>
    MagnetRadius,
}
