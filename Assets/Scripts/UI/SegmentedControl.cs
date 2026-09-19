using UnityEngine;

/// <summary>
/// A row of tab buttons that switches between pages: exactly one tab stays
/// "on" (Toggle + ToggleGroup enforce single selection) and exactly one page
/// GameObject stays active. Tabs and pages pair up by index, so the control is
/// reusable beyond the settings panel.
///
/// The control owns mutual exclusion and page visibility only - visual styling
/// of the tab buttons stays in the editor, tuned by hand per UI convention.
/// </summary>
public class SegmentedControl : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Tabs tapped by the player; one Toggle per option.")]
    [SerializeField] private UnityEngine.UI.Toggle[] _buttons;
    [Tooltip("Panel pages switched by the tabs; same size as _buttons. May be empty for a selector without pages.")]
    [SerializeField] private GameObject[] _pages;

    [Tooltip("Raised with the newly selected index after every switch.")]
    [SerializeField] private UnityEngine.Events.UnityEvent<int> _onValueChanged;

    /// <summary>Raised after every selection switch (c# and inspector handlers).</summary>
    public UnityEngine.Events.UnityEvent<int> onValueChanged => _onValueChanged;

    /// <summary>Index currently selected; -1 once nothing is picked.</summary>
    public int SelectedIndex { get; private set; } = NoSelection;

    private const int NoSelection = -1;

    /// <summary>
    /// Validates wiring, builds (or adopts) the radio group and hooks the
    /// every-tab changed events. The first tab becomes the default selection
    /// so a fresh panel always shows a page instead of an empty board.
    /// </summary>
    private void Awake()
    {
        if (_buttons == null || _buttons.Length == 0)
        {
            Debug.LogError($"SegmentedControl on '{name}' has no tab buttons and cannot switch pages.", this);
            enabled = false;
            return;
        }
        if (_pages != null && _pages.Length > 0 && _pages.Length != _buttons.Length)
        {
            Debug.LogError($"SegmentedControl on '{name}' has {_buttons.Length} tabs but {_pages.Length} pages - they pair one to one.", this);
            enabled = false;
            return;
        }

        var group = GetComponent<UnityEngine.UI.ToggleGroup>();
        if (group == null)
        {
            group = gameObject.AddComponent<UnityEngine.UI.ToggleGroup>();
        }
        group.allowSwitchOff = false;

        for (int i = 0; i < _buttons.Length; i++)
        {
            UnityEngine.UI.Toggle toggle = _buttons[i];
            if (toggle == null)
            {
                Debug.LogError($"SegmentedControl on '{name}' has a null tab at index {i}.", this);
                enabled = false;
                return;
            }
            toggle.group = group;
            int index = i;
            toggle.onValueChanged.AddListener(isOn => OnTabChanged(index, isOn));
        }

        SetSelected(0);
    }

    /// <summary>
    /// Unhooks the tab listeners symmetrically so a disabled control leaves
    /// nothing behind.
    /// </summary>
    private void OnDisable()
    {
        if (_buttons == null)
        {
            return;
        }
        for (int i = 0; i < _buttons.Length; i++)
        {
            if (_buttons[i] == null)
            {
                continue;
            }
            int index = i;
            _buttons[i].onValueChanged.RemoveListener(isOn => OnTabChanged(index, isOn));
        }
    }

    /// <summary>
    /// Selects one tab programmatically: turns it on (group switches the rest
    /// off, whose events route through OnTabChanged as the "off" side) and
    /// shows the paired page while hiding the others.
    /// </summary>
    /// <param name="index">Tab index to select.</param>
    public void SetSelected(int index)
    {
        if (_buttons == null || index < 0 || index >= _buttons.Length || _buttons[index] == null)
        {
            return;
        }

        SelectedIndex = index;
        _buttons[index].SetIsOnWithoutNotify(true);

        if (_pages == null || _pages.Length == 0)
        {
            return;
        }
        for (int i = 0; i < _pages.Length; i++)
        {
            if (_pages[i] != null)
            {
                _pages[i].SetActive(i == index);
            }
        }

        _onValueChanged.Invoke(index);
    }

    /// <summary>
    /// Reacts to a tab's Toggle state change. Only the tab that has turned ON
    /// drives the switch; the group turning others OFF must not cascade.
    /// </summary>
    /// <param name="index">Tab index of the changed toggle.</param>
    /// <param name="isOn">New toggle state.</param>
    private void OnTabChanged(int index, bool isOn)
    {
        if (!isOn)
        {
            return;
        }
        if (index != SelectedIndex)
        {
            SetSelected(index);
        }
    }
}
