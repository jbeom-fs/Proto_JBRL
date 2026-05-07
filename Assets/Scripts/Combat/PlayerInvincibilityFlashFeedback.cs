using System.Collections;
using UnityEngine;

/// <summary>
/// Visual-only white flash feedback for invincibility windows.
/// Uses MaterialPropertyBlock so the shared sprite material is not cloned at runtime.
/// </summary>
public sealed class PlayerInvincibilityFlashFeedback : MonoBehaviour
{
    private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
    private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

    [SerializeField] private SpriteRenderer targetRenderer;
    [SerializeField] private Color flashColor = Color.white;

    private MaterialPropertyBlock _propertyBlock;
    private Coroutine _routine;

    private void Awake()
    {
        EnsurePropertyBlock();
        ResolveRenderer();
        SetFlashAmount(0f);
    }

    private void OnEnable()
    {
        EnsurePropertyBlock();
        ResolveRenderer();
        SetFlashAmount(0f);
    }

    private void OnDisable()
    {
        StopAndReset();
    }

    public void Play(float duration)
    {
        if (duration <= 0f)
        {
            StopAndReset();
            return;
        }

        if (!isActiveAndEnabled) return;
        if (ResolveRenderer() == null) return;

        if (_routine != null)
            StopCoroutine(_routine);

        _routine = StartCoroutine(FlashRoutine(duration));
    }

    public void StopAndReset()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        SetFlashAmount(0f);
    }

    private IEnumerator FlashRoutine(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float progress = Mathf.Clamp01(elapsed / duration);
            float flashAmount = progress < 0.5f
                ? progress / 0.5f
                : 1f - ((progress - 0.5f) / 0.5f);

            SetFlashAmount(flashAmount);
            elapsed += Time.deltaTime;
            yield return null;
        }

        SetFlashAmount(0f);
        _routine = null;
    }

    private void SetFlashAmount(float amount)
    {
        if (targetRenderer == null) return;
        EnsurePropertyBlock();

        targetRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(FlashColorId, flashColor);
        _propertyBlock.SetFloat(FlashAmountId, Mathf.Clamp01(amount));
        targetRenderer.SetPropertyBlock(_propertyBlock);
    }

    private void EnsurePropertyBlock()
    {
        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();
    }

    private SpriteRenderer ResolveRenderer()
    {
        if (targetRenderer != null)
            return targetRenderer;

        targetRenderer = GetComponentInChildren<SpriteRenderer>(true);
        return targetRenderer;
    }
}
