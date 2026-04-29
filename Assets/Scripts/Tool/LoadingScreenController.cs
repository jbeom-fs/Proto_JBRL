using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public class LoadingScreenController : MonoBehaviour
{
    private const float SLOW_FADE_FRAME_MS = 16f;

    [Header("UI")]
    public Image backgroundImage;
    public Image loadingImage;
    public CanvasGroup canvasGroup;

    [Header("Fade")]
    public float fadeDuration = 0.25f;

    [Range(1f, 5f)]
    public float bgFadeInSpeed = 2f;

    [Range(0f, 1f)]
    public float bgFadeOutDelay = 0.5f;

    private bool SeparateMode => backgroundImage != null;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (SeparateMode)
        {
            SetImageAlpha(backgroundImage, 0f);
            if (loadingImage != null) SetImageAlpha(loadingImage, 0f);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }
        else
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
            else if (loadingImage != null)
            {
                SetImageAlpha(loadingImage, 0f);
            }
        }

       
    }

    public IEnumerator Show()
    {
        RuntimePerfLogger.MarkEvent("loading_show_begin",
            "mode=" + (SeparateMode ? "separate" : "single") +
            " fadeDuration=" + fadeDuration.ToString("F3", CultureInfo.InvariantCulture));

        double start = Time.realtimeSinceStartupAsDouble;

        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = true;
        }

        if (SeparateMode)
            yield return StartCoroutine(ShowSeparate());
        else
            yield return StartCoroutine(FadeSingle(0f, 1f, "loading_show_single_frame"));

        RuntimePerfLogger.MarkEvent("loading_show_end",
            "elapsedMs=" + ElapsedMs(start) +
            " dtMs=" + CurrentDtMs());
    }

    public IEnumerator Hide()
    {
        RuntimePerfLogger.MarkEvent("loading_hide_begin",
            "mode=" + (SeparateMode ? "separate" : "single") +
            " fadeDuration=" + fadeDuration.ToString("F3", CultureInfo.InvariantCulture) +
            " bgDelay=" + bgFadeOutDelay.ToString("F3", CultureInfo.InvariantCulture));

        double start = Time.realtimeSinceStartupAsDouble;
        if (SeparateMode)
            yield return StartCoroutine(HideSeparate());
        else
            yield return StartCoroutine(FadeSingle(1f, 0f, "loading_hide_single_frame"));

        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }


        RuntimePerfLogger.MarkEvent("loading_hide_end",
            "elapsedMs=" + ElapsedMs(start) +
            " dtMs=" + CurrentDtMs());
    }

    private IEnumerator ShowSeparate()
    {
        SetImageAlpha(backgroundImage, 0f);
        if (loadingImage != null) SetImageAlpha(loadingImage, 0f);

        if (fadeDuration <= 0f)
        {
            SetImageAlpha(backgroundImage, 1f);
            if (loadingImage != null) SetImageAlpha(loadingImage, 1f);
            yield break;
        }

        float bgDuration = fadeDuration / Mathf.Max(1f, bgFadeInSpeed);
        float elapsed = 0f;
        int frameIndex = 0;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            LogSlowFadeFrame("loading_show_frame", frameIndex, elapsed);

            SetImageAlpha(backgroundImage, Mathf.Clamp01(elapsed / bgDuration));
            if (loadingImage != null)
                SetImageAlpha(loadingImage, Mathf.Clamp01(elapsed / fadeDuration));

            yield return YieldCache.WaitForEndOfFrame;
            frameIndex++;
        }

        SetImageAlpha(backgroundImage, 1f);
        if (loadingImage != null) SetImageAlpha(loadingImage, 1f);
    }

    private IEnumerator HideSeparate()
    {
        if (fadeDuration <= 0f)
        {
            SetImageAlpha(backgroundImage, 0f);
            if (loadingImage != null) SetImageAlpha(loadingImage, 0f);
            yield break;
        }

        float bgDelay = fadeDuration * bgFadeOutDelay;
        float totalTime = fadeDuration + bgDelay;
        float elapsed = 0f;
        int frameIndex = 0;

        while (elapsed < totalTime)
        {
            elapsed += Time.unscaledDeltaTime;
            LogSlowFadeFrame("loading_hide_frame", frameIndex, elapsed);

            if (loadingImage != null)
                SetImageAlpha(loadingImage, 1f - Mathf.Clamp01(elapsed / fadeDuration));

            float bgElapsed = elapsed - bgDelay;
            if (bgElapsed > 0f)
                SetImageAlpha(backgroundImage, 1f - Mathf.Clamp01(bgElapsed / fadeDuration));

            yield return YieldCache.WaitForEndOfFrame;
            frameIndex++;
        }

        SetImageAlpha(backgroundImage, 0f);
        if (loadingImage != null) SetImageAlpha(loadingImage, 0f);
    }

    private IEnumerator FadeSingle(float from, float to, string slowFrameEvent)
    {
        if (canvasGroup == null && loadingImage == null) yield break;

        if (fadeDuration <= 0f)
        {
            SetAlpha(to);
            yield break;
        }

        float elapsed = 0f;
        int frameIndex = 0;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            LogSlowFadeFrame(slowFrameEvent, frameIndex, elapsed);
            SetAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / fadeDuration)));

            yield return YieldCache.WaitForEndOfFrame;
            frameIndex++;
        }

        SetAlpha(to);
    }

    private void SetAlpha(float alpha)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = alpha;
            return;
        }

        if (loadingImage != null)
            SetImageAlpha(loadingImage, alpha);
    }

    private static void SetImageAlpha(Image image, float alpha)
    {
        if (image == null) return;
        var c = image.color;
        c.a = alpha;
        image.color = c;
    }

    private static void LogSlowFadeFrame(string eventName, int frameIndex, float elapsed)
    {
        float dtMs = Time.unscaledDeltaTime * 1000f;
        if (dtMs < SLOW_FADE_FRAME_MS) return;

        RuntimePerfLogger.MarkEvent(eventName,
            "index=" + frameIndex +
            " elapsed=" + elapsed.ToString("F3", CultureInfo.InvariantCulture) +
            " dtMs=" + dtMs.ToString("F3", CultureInfo.InvariantCulture));
    }

    private static string CurrentDtMs()
    {
        return (Time.unscaledDeltaTime * 1000f).ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string ElapsedMs(double startTime)
    {
        return ((Time.realtimeSinceStartupAsDouble - startTime) * 1000.0)
            .ToString("F3", CultureInfo.InvariantCulture);
    }
}
