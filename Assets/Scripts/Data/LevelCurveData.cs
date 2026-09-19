using UnityEngine;

/// <summary>
/// How much experience each level costs.
///
/// Pacing lives in an asset so it can be retuned without touching the tracker, and
/// so the curve is reviewable as one list rather than scattered through code.
/// </summary>
[CreateAssetMenu(fileName = "LevelCurveData", menuName = "My project/Data/Level Curve")]
public class LevelCurveData : ScriptableObject
{
    [Tooltip("Experience to go from level 1 to 2, then 2 to 3, and so on. " +
             "The player starts at level 1; the last entry is the level cap.")]
    [SerializeField] private int[] _xpRequired = { 5, 10, 18, 28, 40, 55, 72 };

    /// <summary>Highest level this curve can reach; 1 when the curve is empty.</summary>
    public int MaxLevel => _xpRequired != null && _xpRequired.Length > 0 ? _xpRequired.Length + 1 : 1;

    /// <summary>
    /// Experience needed to leave the given level.
    /// </summary>
    /// <param name="level">Level to look up, starting at 1.</param>
    /// <returns>
    /// Experience required, or 0 when the level is at (or past) the cap, which
    /// callers read as "no further levels".
    /// </returns>
    public int RequiredForNextLevel(int level)
    {
        if (_xpRequired == null || level < 1 || level > _xpRequired.Length)
        {
            return 0;
        }

        return _xpRequired[level - 1];
    }
}
