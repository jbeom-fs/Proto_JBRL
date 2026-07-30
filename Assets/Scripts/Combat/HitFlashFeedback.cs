using System.Collections;
using UnityEngine;

public class HitFlashFeedback : MonoBehaviour
{
    [SerializeField] private SpriteRenderer targetRenderer;
    [SerializeField] private StatusEffectIconTable iconTable;
    [SerializeField] private Color flashColor = Color.red;
    [SerializeField, Min(0.01f)] private float flashDuration = 0.08f;
    [SerializeField, Min(1)] private int flashCount = 1;
    [SerializeField] private bool resetColorOnDisable = true;

    private Coroutine _flashRoutine;
    private Color _originalColor = Color.white;
    private bool _hasOriginalColor;
    private bool _warnedMissingIconTable;

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
        Flash(flashColor);
    }

    public void Play(Color color)
    {
        Flash(color);
    }

    public void Flash(StatusEffectIconType type)
    {
        StatusEffectIconTable table = iconTable;
        if (table != null && table.TryGetFlashColor(type, out Color color))
        {
            Flash(color);
        }
        else
        {
            if (table == null)
                WarnMissingIconTable();
            Flash(flashColor);
        }
    }

    public void Flash(Color color)
    {
        if (!isActiveAndEnabled) return;
        if (ResolveRenderer() == null) return;

        bool wasFlashing = _flashRoutine != null;
        StopFlash();
        if (wasFlashing)
            RestoreOriginalColor();

        CaptureOriginalColor();
        _flashRoutine = StartCoroutine(FlashRoutine(color));
    }

    public void ResetColor()
    {
        StopFlash();
        RestoreOriginalColor();
        CaptureOriginalColor();
    }

    private IEnumerator FlashRoutine(Color color)
    {
        for (int i = 0; i < flashCount; i++)
        {
            targetRenderer.color = color;
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

    private void WarnMissingIconTable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedMissingIconTable)
            return;

        _warnedMissingIconTable = true;
        Debug.LogWarning(
            "[HitFlashFeedback] StatusEffectIconTable is missing. Inspector assignment is required.",
            this);
#endif
    }
}
