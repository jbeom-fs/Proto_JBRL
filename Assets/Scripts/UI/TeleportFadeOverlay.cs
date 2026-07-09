using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(Image))]
public sealed class TeleportFadeOverlay : MonoBehaviour
{
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.4f;

    private CanvasGroup _canvasGroup;
    private Image _image;
    private Coroutine _fadeRoutine;

    private void Reset()
    {
        ConfigureOverlay();
    }

    private void Awake()
    {
        ConfigureOverlay();
    }

    private void OnValidate()
    {
        ConfigureOverlay();
    }

    public void TriggerFade()
    {
        ConfigureOverlay();

        if (_fadeRoutine != null)
            StopCoroutine(_fadeRoutine);

        _canvasGroup.alpha = 1f;
        _fadeRoutine = StartCoroutine(FadeOut());
    }

    private IEnumerator FadeOut()
    {
        float duration = Mathf.Max(0f, fadeOutDuration);
        if (duration <= 0f)
        {
            _canvasGroup.alpha = 0f;
            _fadeRoutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        _canvasGroup.alpha = 0f;
        _fadeRoutine = null;
    }

    private void ConfigureOverlay()
    {
        if (_canvasGroup == null)
            _canvasGroup = GetComponent<CanvasGroup>();
        if (_image == null)
            _image = GetComponent<Image>();

        if (_canvasGroup != null)
        {
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            if (!Application.isPlaying)
                _canvasGroup.alpha = 0f;
        }

        if (_image != null)
        {
            _image.color = Color.black;
            _image.raycastTarget = false;
        }

        RectTransform rect = transform as RectTransform;
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }
}
