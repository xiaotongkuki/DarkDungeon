using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the player's health pool as a row of pixel-art hearts in the HUD.
///
/// The heart count is derived from the announced maximum pool, so a health
/// upgrade that raises the cap grows the row on the very next announcement -
/// the row never stores its own count. The row is laid out by a
/// <see cref="HorizontalLayoutGroup"/>, so adding hearts just extends it;
/// every heart shares the template's look and scale.
///
/// Each heart carries <see cref="_healthPerHeart"/> hit points; a partially
/// filled heart shows the half-heart sprite, which is how an odd pool size
/// still ends on exactly full. Before the first announcement the row is
/// empty (no hearts at all), which is also the honest picture of a pool the
/// bus has said nothing about.
/// </summary>
public class HudHealthBar : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Template for one heart: the first child (active template) is cloned on demand; the template itself is hidden.")]
    [SerializeField] private Image _heartTemplate;

    [Header("Sprites")]
    [Tooltip("Sprite for a heart with all of its hit points.")]
    [SerializeField] private Sprite _fullHeart;
    [Tooltip("Sprite for a heart holding none of its hit points.")]
    [SerializeField] private Sprite _emptyHeart;
    [Tooltip("Sprite for a heart in between; optional - without it hearts render only full or empty.")]
    [SerializeField] private Sprite _halfHeart;

    [Header("Layout")]
    [Tooltip("Hit points one heart represents.")]
    [SerializeField] private float _healthPerHeart = 2f;

    /// <summary>Hearts cloned from the template; the list parallels the visible row.</summary>
    private readonly List<Image> _hearts = new List<Image>();

    /// <summary>
    /// Rejects a HUD row with no template or no full/empty pair; the same
    /// "disabled and loud" pattern the other HUD parts use for a miswired
    /// object, because a broken heart row must not draw garbage.
    /// </summary>
    private void Awake()
    {
        if (_heartTemplate == null || _fullHeart == null || _emptyHeart == null)
        {
            Debug.LogError($"HudHealthBar on '{name}' is missing its heart template or full/empty sprites and will stay blank.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Subscribes to pool changes and clears the row, so a slow first
    /// announcement still leaves a correct (if pessimistic) picture.
    /// </summary>
    private void OnEnable()
    {
        EventBus.HealthChanged += OnHealthChanged;
        foreach (Image heart in _hearts)
        {
            if (heart != null)
            {
                heart.gameObject.SetActive(false);
            }
        }

        _hearts.Clear();
    }

    /// <summary>
    /// Drops the subscription; the bus is static and would otherwise keep this
    /// row alive past its scene.
    /// </summary>
    private void OnDisable()
    {
        EventBus.HealthChanged -= OnHealthChanged;
    }

    /// <summary>
    /// Redraws the row from the announced pool, first growing (or never
    /// shrinking: a lower cap keeps old hearts only until the next draw, and
    /// a bigger cap clones more) the row to fit.
    /// </summary>
    /// <param name="current">Hits the player has left.</param>
    /// <param name="max">Total hits the pool holds.</param>
    private void OnHealthChanged(int current, int max)
    {
        EnsureHeartCount(max);
        FillHearts(current);
    }

    /// <summary>
    /// Clones template hearts until the row can display the announced pool,
    /// one heart per constant chunk; the layout group does the arranging.
    /// The template is the first clone's source and stays hidden forever,
    /// which is also how the row starts empty without any manual hiding.
    /// </summary>
    /// <param name="max">Total hits the pool holds.</param>
    private void EnsureHeartCount(int max)
    {
        int needed = Mathf.CeilToInt(Mathf.Max(0, max) / _healthPerHeart);
        needed = Mathf.Min(needed, 100);
        while (_hearts.Count < needed)
        {
            Image clone = Instantiate(_heartTemplate, transform);
            clone.name = "Heart" + _hearts.Count;
            clone.gameObject.SetActive(true);
            _hearts.Add(clone);
        }
    }

    /// <summary>
    /// Fills hearts left to right: one with more than a full heart's worth of
    /// health becomes full, one with anything between (the half step included)
    /// becomes half when a half sprite is assigned, the rest empty. Hearts
    /// beyond the announced pool's reach stay hidden - they belong to the
    /// previous (smaller) row.
    /// </summary>
    /// <param name="current">Hits the player has left.</param>
    private void FillHearts(int current)
    {
        int visible = Mathf.CeilToInt(Mathf.Max(0, current) / _healthPerHeart);
        for (int i = 0; i < _hearts.Count; i++)
        {
            if (i >= visible)
            {
                _hearts[i].gameObject.SetActive(false);
                continue;
            }

            _hearts[i].gameObject.SetActive(true);
            float filled = current - i * _healthPerHeart;
            if (filled >= _healthPerHeart)
            {
                _hearts[i].sprite = _fullHeart;
            }
            else if (_halfHeart != null)
            {
                _hearts[i].sprite = _halfHeart;
            }
            else
            {
                _hearts[i].sprite = _emptyHeart;
            }
        }
    }
}
