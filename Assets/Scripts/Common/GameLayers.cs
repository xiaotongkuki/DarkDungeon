using UnityEngine;

/// <summary>
/// The project's layer scheme in one place, so gameplay code never spells out a
/// bare layer number and the collision matrix has a single documented meaning.
///
/// Current scheme:
/// <list type="bullet">
/// <item><description><c>Default (0)</c> - scenery: walls and obstacles.</description></item>
/// <item><description><c>Player (8)</c> - the player object.</description></item>
/// <item><description><c>Enemy (9)</c> - spawned enemies.</description></item>
/// </list>
///
/// Collision matrix intent:
/// <list type="bullet">
/// <item><description>Player ↔ Default: <b>on</b> - the player is stopped by walls.</description></item>
/// <item><description>Enemy ↔ Default: <b>off</b> - enemies walk through scenery.</description></item>
/// <item><description>Enemy ↔ Enemy: <b>off</b> - enemies overlap instead of shoving each other.</description></item>
/// <item><description>Enemy ↔ Player: <b>on</b> - enemies overlap the player, and the trigger
/// contact is what deals contact damage.</description></item>
/// </list>
///
/// <see cref="GameLayersTests"/> pins both the indices and the matrix, so a change
/// here or in Project Settings fails the suite instead of silently altering play.
/// </summary>
public static class GameLayers
{
    /// <summary>Layer index of the player object.</summary>
    public const int Player = 8;

    /// <summary>Layer index of spawned enemies.</summary>
    public const int Enemy = 9;

    /// <summary>Layer index of scenery: walls and obstacles.</summary>
    public const int Scenery = 0;

    /// <summary>Mask matching only the player layer.</summary>
    public static readonly LayerMask PlayerMask = 1 << Player;

    /// <summary>Mask matching only the enemy layer.</summary>
    public static readonly LayerMask EnemyMask = 1 << Enemy;
}
