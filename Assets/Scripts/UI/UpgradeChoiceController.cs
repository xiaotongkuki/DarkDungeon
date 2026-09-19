using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The level-up choice: pauses the run, offers three upgrades, applies the picked
/// one and resumes.
///
/// It lives on an always-active object and only toggles the panel, because a
/// component on an inactive panel would never receive OnEnable and could never hear
/// the level-up it exists to react to. The panel is hidden in Awake rather than in
/// the scene so it stays visible and editable in the editor.
///
/// One gem can cross several levels, so level-ups are counted and the panel is
/// re-rolled for each of them instead of stacking panels.
/// </summary>
public class UpgradeChoiceController : MonoBehaviour
{
    /// <summary>How many upgrades are offered per level-up.</summary>
    private const int ChoiceCount = 3;

    [Header("Data")]
    [Tooltip("Pool the offered upgrades are drawn from. Needs at least three entries.")]
    [SerializeField] private UpgradePoolData _pool;
    [Tooltip("Component the picked upgrade is applied to.")]
    [SerializeField] private PlayerStatModifiers _modifiers;

    [Header("References")]
    [Tooltip("Panel shown while a choice is pending; hidden otherwise.")]
    [SerializeField] private GameObject _panel;
    [Tooltip("Buttons that carry the choices; each needs a Text child for its label.")]
    [SerializeField] private Button[] _choiceButtons = new Button[ChoiceCount];

    /// <summary>Level-ups waiting for a choice; a single gem can add more than one.</summary>
    private int _pendingChoices;

    /// <summary>Upgrades currently on the buttons.</summary>
    private readonly List<UpgradeData> _offered = new List<UpgradeData>();

    /// <summary>Labels of the choice buttons, cached so no lookup happens per level-up.</summary>
    private Text[] _labels;

    /// <summary>
    /// Caches the labels, wires the buttons and rejects a setup that cannot offer a
    /// real choice. A component disabled during its own Awake never reaches
    /// OnEnable, so a misconfigured panel is inert and loud.
    /// </summary>
    private void Awake()
    {
        if (_pool == null || _modifiers == null || _panel == null
            || _choiceButtons == null || _choiceButtons.Length < ChoiceCount)
        {
            Debug.LogError($"UpgradeChoiceController on '{name}' is missing a reference and will not offer upgrades.", this);
            enabled = false;
            return;
        }

        if (_pool.Count < ChoiceCount)
        {
            Debug.LogError($"UpgradeChoiceController on '{name}' has a pool of {_pool.Count} upgrades " +
                           $"but must offer {ChoiceCount}; it will not offer upgrades.", this);
            enabled = false;
            return;
        }

        _labels = new Text[_choiceButtons.Length];
        for (int i = 0; i < _choiceButtons.Length; i++)
        {
            _labels[i] = _choiceButtons[i].GetComponentInChildren<Text>(true);

            // Captured per button: every listener would otherwise close over the
            // same index and every button would pick the last choice.
            int index = i;
            _choiceButtons[i].onClick.AddListener(() => Choose(index));
        }

        _panel.SetActive(false);
    }

    /// <summary>
    /// Removes the button listeners, so a destroyed controller cannot be called
    /// through buttons that outlive it.
    /// </summary>
    private void OnDestroy()
    {
        if (_choiceButtons == null)
        {
            return;
        }

        for (int i = 0; i < _choiceButtons.Length; i++)
        {
            if (_choiceButtons[i] != null)
            {
                _choiceButtons[i].onClick.RemoveAllListeners();
            }
        }
    }

    /// <summary>
    /// Starts listening for level-ups.
    /// </summary>
    private void OnEnable()
    {
        if (_pool == null || _panel == null)
        {
            return;
        }

        EventBus.LevelUp += OnLevelUp;
    }

    /// <summary>
    /// Drops the subscription; the bus is static and would otherwise keep this panel
    /// reacting to a scene it no longer belongs to.
    /// </summary>
    private void OnDisable()
    {
        EventBus.LevelUp -= OnLevelUp;
    }

    /// <summary>
    /// Queues a level-up and shows the panel if it is not already up.
    /// </summary>
    /// <param name="level">Level the player just reached; the panel does not use it.</param>
    private void OnLevelUp(int level)
    {
        _pendingChoices++;

        if (!_panel.activeSelf)
        {
            ShowChoices();
        }
    }

    /// <summary>
    /// Applies the picked upgrade, then either rolls the next pending level-up or
    /// closes the panel and resumes the run.
    /// </summary>
    /// <param name="index">Index of the pressed button.</param>
    public void Choose(int index)
    {
        if (index < 0 || index >= _offered.Count)
        {
            return;
        }

        _modifiers.Apply(_offered[index]);
        _pendingChoices--;

        if (_pendingChoices > 0)
        {
            ShowChoices();
            return;
        }

        _panel.SetActive(false);
        Time.timeScale = 1f;
    }

    /// <summary>
    /// Rolls a fresh set of distinct upgrades, writes them onto the buttons, shows
    /// the panel and pauses the run.
    /// </summary>
    private void ShowChoices()
    {
        RollChoices();

        for (int i = 0; i < _choiceButtons.Length; i++)
        {
            if (i >= _offered.Count)
            {
                _choiceButtons[i].gameObject.SetActive(false);
                continue;
            }

            UpgradeData upgrade = _offered[i];
            _labels[i].text = string.Format("{0}\n{1}", upgrade.DisplayName, upgrade.Description);
            _choiceButtons[i].gameObject.SetActive(true);
        }

        _panel.SetActive(true);
        Time.timeScale = 0f;
    }

    /// <summary>
    /// Fills <see cref="_offered"/> with distinct random upgrades.
    ///
    /// Distinctness is what makes the choice meaningful: three copies of the same
    /// upgrade is not a decision. Awake guarantees the pool is at least as large as
    /// the number of choices, so the loop always terminates.
    /// </summary>
    private void RollChoices()
    {
        _offered.Clear();
        var usedIndices = new List<int>();

        while (_offered.Count < ChoiceCount)
        {
            int index = Random.Range(0, _pool.Count);
            if (usedIndices.Contains(index))
            {
                continue;
            }

            usedIndices.Add(index);
            _offered.Add(_pool.Get(index));
        }
    }
}
