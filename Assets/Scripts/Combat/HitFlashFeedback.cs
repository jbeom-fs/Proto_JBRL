using System.Collections;
using UnityEngine;

public class HitFlashFeedback : MonoBehaviour
{
    [SerializeField] private SpriteRenderer targetRenderer;
    [SerializeField] private Color flashColor = Color.red;
    [SerializeField, Min(0.01f)] private float flashDuration = 0.08f;
    [SerializeField, Min(1)] private int flashCount = 1;
    [SerializeField] private bool resetColorOnDisable = true;

    private Coroutine _flashRoutine;
    private Color _originalColor = Color.white;
    private bool _hasOriginalColor;

    private void Awake()
    {
        ResolveRenderer();
        CaptureOriginalColor();
    }

    private void OnEnable()
    {
        ResolveRenderer();
        CaptureOriginalColor();
    }

    private void OnDisable()
    {
        StopFlash();

        if (resetColorOnDisable)
            RestoreOriginalColor();
    }

    public void Play()
    {
        if (!isActiveAndEnabled) return;
        if (ResolveRenderer() == null) return;

        bool wasFlashing = _flashRoutine != null;
        StopFlash();
        if (wasFlashing)
            RestoreOriginalColor();

        CaptureOriginalColor();
        _flashRoutine = StartCoroutine(FlashRoutine());
    }

    public void ResetColor()
    {
        StopFlash();
        RestoreOriginalColor();
        CaptureOriginalColor();
    }

    private IEnumerator FlashRoutine()
    {
        for (int i = 0; i < flashCount; i++)
        {
            targetRenderer.color = flashColor;
            yield return new WaitForSeconds(flashDuration);
            RestoreOriginalColor();

            if (i < flashCount - 1)
                yield return new WaitForSeconds(flashDuration);
        }

        _flashRoutine = null;
    }

    private void StopFlash()
    {
        if (_flashRoutine == null) return;

        StopCoroutine(_flashRoutine);
        _flashRoutine = null;
    }

    private void CaptureOriginalColor()
    {
        if (targetRenderer == null) return;
        if (_flashRoutine != null) return;

        _originalColor = targetRenderer.color;
        _hasOriginalColor = true;
    }

    private void RestoreOriginalColor()
    {
        if (targetRenderer == null || !_hasOriginalColor) return;

        targetRenderer.color = _originalColor;
    }

    private SpriteRenderer ResolveRenderer()
    {
        if (targetRenderer != null)
            return targetRenderer;

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        if (renderers == null || renderers.Length == 0)
            return null;

        targetRenderer = FindBestRenderer(renderers);
        return targetRenderer;
    }

    private static SpriteRenderer FindBestRenderer(SpriteRenderer[] renderers)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.sprite != null)
                return renderer;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer != null && renderer.enabled && renderer.sprite != null)
                return renderer;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer != null && renderer.sprite != null)
                return renderer;
        }

        return renderers[0];
    }
}
