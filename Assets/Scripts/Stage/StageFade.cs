using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A full-screen fade used to hide the stage switch: cover to black, let the
/// caller rebuild the arena behind it, then reveal.
///
/// The overlay is built in code rather than in the scene because it has no
/// tunable layout - it is always a full-rect black image at maximum sort order.
/// A scene-canvas fade would be one more object to wire and one more thing to
/// break in tests; a code-built one arrives with the controller that needs it.
///
/// It never touches <c>Time.timeScale</c>: covering is a screen effect, and the
/// global pause belongs to the upgrade flow alone.
/// </summary>
public class StageFade : MonoBehaviour
{
    [Tooltip("Seconds for the cover and for the reveal, in real time (unscaled).")]
    [SerializeField] private float _halfDuration = 0.35f;

    /// <summary>The black image covering the screen while it is opaque.</summary>
    private Image _veil;

    /// <summary>
    /// Builds the overlay canvas in code. It must be a screen-space overlay on
    /// top of everything gameplay draws, with ray casting off so the covered
    /// frames still deliver input to whatever sits below.
    /// </summary>
    private void Awake()
    {
        GameObject canvasObject = new GameObject("StageFadeCanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 800;

        _veil = new GameObject("Veil").AddComponent<Image>();
        _veil.transform.SetParent(canvas.transform, worldPositionStays: false);
        _veil.rectTransform.anchorMin = Vector2.zero;
        _veil.rectTransform.anchorMax = Vector2.one;
        _veil.rectTransform.offsetMin = Vector2.zero;
        _veil.rectTransform.offsetMax = Vector2.zero;
        _veil.color = new Color(0f, 0f, 0f, 0f);
        _veil.raycastTarget = false;

        canvasObject.transform.SetParent(transform, worldPositionStays: false);
    }

    /// <summary>
    /// Covers the screen in black over the configured duration.
    /// </summary>
    /// <returns>A coroutine the caller drives.</returns>
    public IEnumerator Cover()
    {
        float duration = Mathf.Max(0f, _halfDuration);
        float from = _veil.color.a;
        float clock = 0f;
        while (clock < duration)
        {
            clock += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(from, 1f, duration <= 0f ? 1f : clock / duration));
            yield return null;
        }
        SetAlpha(1f);
    }

    /// <summary>
    /// Reveals the screen back from black over the configured duration.
    /// </summary>
    /// <returns>A coroutine the caller drives.</returns>
    public IEnumerator Reveal()
    {
        float duration = Mathf.Max(0f, _halfDuration);
        float from = _veil.color.a;
        float clock = 0f;
        while (clock < duration)
        {
            clock += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(from, 0f, duration <= 0f ? 1f : clock / duration));
            yield return null;
        }
        SetAlpha(0f);
    }

    /// <summary>
    /// Writes only the alpha of the black veil; the colour channel below it
    /// stays whatever was started with.
    /// </summary>
    /// <param name="alpha">Opacity from 0 (transparent) to 1 (covered).</param>
    private void SetAlpha(float alpha)
    {
        Color color = _veil.color;
        color.a = Mathf.Clamp01(alpha);
        _veil.color = color;
    }
}
