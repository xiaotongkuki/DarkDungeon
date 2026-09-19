using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the run's progress in the corner of the screen: kills and level.
///
/// It listens to the bus rather than reading the trackers, so the display is
/// independent of how scoring and leveling work. The starting score is written on
/// enable; the starting level and experience arrive from the tracker's own
/// announcement, which is made in Start so a display enabled in any order hears it.
/// </summary>
public class HudController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Label that shows the running score.")]
    [SerializeField] private Text _scoreText;
    [Tooltip("Label that shows the current level; the required-for-next text was dropped in favour of the bar fill itself.")]
    [SerializeField] private Text _levelText;
    [Tooltip("Fill image of the experience bar; type must be Filled / Horizontal. Optional: a null simply leaves the bar untouched.")]
    [SerializeField] private Image _xpFill;
    [Tooltip("Label that shows the current wave. Optional: a null simply leaves the label unused.")]
    [SerializeField] private Text _stageText;

    /// <summary>Format used for the score label; the placeholder is the score.</summary>
    private const string ScoreFormat = "击杀: {0}";

    /// <summary>Format used by the level label; the placeholder is the level.</summary>
    private const string LevelFormat = "Lv.{0}";

    /// <summary>Format used once the curve is exhausted and no further level is possible.</summary>
    private const string MaxLevelFormat = "Lv.{0} MAX";

    /// <summary>Format used by the wave label: the one-based wave number.</summary>
    private const string WaveFormat = "关卡 {0}";

    /// <summary>Level last announced; starts at 1 because that is where a run begins.</summary>
    private int _level = 1;

    /// <summary>
    /// Rejects a HUD with a missing label, the same way gameplay components reject
    /// missing data assets: a disabled component never reaches OnEnable, so a
    /// misconfigured object is inert and loud instead of throwing on every change.
    /// </summary>
    private void Awake()
    {
        if (_scoreText == null || _levelText == null)
        {
            Debug.LogError($"HudController on '{name}' is missing a label reference and will stay blank.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Subscribes to score, level and experience changes, and writes the starting
    /// score and level.
    /// </summary>
    private void OnEnable()
    {
        if (_scoreText == null || _levelText == null)
        {
            return;
        }

        EventBus.ScoreChanged += OnScoreChanged;
        EventBus.LevelUp += OnLevelUp;
        EventBus.XpChanged += OnXpChanged;
        EventBus.WaveStarted += OnWaveStarted;

        _scoreText.text = string.Format(ScoreFormat, 0);
        OnLevelUp(1);
    }

    /// <summary>
    /// Drops the subscriptions; the bus is static and would otherwise keep this
    /// label alive past its scene.
    /// </summary>
    private void OnDisable()
    {
        EventBus.ScoreChanged -= OnScoreChanged;
        EventBus.LevelUp -= OnLevelUp;
        EventBus.XpChanged -= OnXpChanged;
        EventBus.WaveStarted -= OnWaveStarted;
    }

    /// <summary>
    /// Rewrites the wave label when a wave begins. A HUD with no wave slot wired
    /// simply ignores the fact.
    /// </summary>
    /// <param name="wave">One-based wave number.</param>
    private void OnWaveStarted(int wave)
    {
        if (_stageText == null)
        {
            return;
        }

        _stageText.text = string.Format(WaveFormat, wave);
    }

    /// <summary>
    /// Writes the new score into the label.
    /// </summary>
    /// <param name="score">Player's current total score.</param>
    private void OnScoreChanged(int score)
    {
        _scoreText.text = string.Format(ScoreFormat, score);
    }

    /// <summary>
    /// Rewrites the level label; the experience fill itself is drawn from the
    /// experience announcements and needs no text.
    /// </summary>
    /// <param name="level">Level to display.</param>
    private void OnLevelUp(int level)
    {
        _level = level;
        _levelText.text = string.Format(LevelFormat, level);
    }

    /// <summary>
    /// Fills the bar when one is wired, and switches the level label wording to
    /// the capped one once the curve has nothing left to buy.
    /// </summary>
    /// <param name="xpInLevel">Experience banked toward the next level.</param>
    /// <param name="requiredForNext">Experience the next level needs, or 0 at the cap.</param>
    private void OnXpChanged(int xpInLevel, int requiredForNext)
    {
        if (requiredForNext <= 0)
        {
            _levelText.text = string.Format(MaxLevelFormat, _level);
        }

        if (_xpFill != null)
        {
            _xpFill.fillAmount = requiredForNext > 0
                ? Mathf.Clamp01((float)xpInLevel / requiredForNext)
                : 1f;
        }
    }
}
