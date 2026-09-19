/// <summary>
/// Anything that can be hurt: enemies today, the player and future destructibles
/// tomorrow.
///
/// Damage sources talk to this instead of a concrete type, so a weapon does not
/// have to know whether it is hitting an enemy, the player or a crate. The
/// contract is deliberately tiny: ask whether the target is still worth hitting,
/// then hand it a hit.
/// </summary>
public interface IDamageable
{
    /// <summary>True while the target still has health left.</summary>
    bool IsAlive { get; }

    /// <summary>
    /// Applies damage. Implementations are expected to ignore non-positive amounts
    /// and to do nothing once dead, so callers never have to guard the call.
    /// </summary>
    /// <param name="amount">Hits to remove.</param>
    void TakeDamage(int amount);
}
