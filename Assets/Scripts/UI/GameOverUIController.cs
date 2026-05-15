using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class GameOverUIController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private CanvasGroup rootGroup;
    [SerializeField] private RectTransform root;
    [SerializeField] private Image gameOverImage;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Sprite gameOverSprite;

    [Header("Fade")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.35f;

    private Coroutine _fadeRoutine;
    private bool _isVisible;
    private UnityAction _confirmAction;
    private bool _warnedMissingReferences;

    public bool IsVisible => _isVisible;

    private void Awake()
    {
        EnsureUi();
        HideImmediate();
    }

    private void OnDisable()
    {
        if (_fadeRoutine != null)
        {
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }
    }

    public void SetConfirmAction(UnityAction action)
    {
        EnsureUi();

        if (confirmButton == null)
            return;

        if (_confirmAction != null)
            confirmButton.onClick.RemoveListener(_confirmAction);

        _confirmAction = action;

        if (_confirmAction != null)
            confirmButton.onClick.AddListener(_confirmAction);
    }

    public void Show()
    {
        EnsureUi();

        if (rootGroup == null || _isVisible)
            return;

        if (_fadeRoutine != null)
            StopCoroutine(_fadeRoutine);

        _fadeRoutine = StartCoroutine(FadeInRoutine());
    }

    public void HideImmediate()
    {
        EnsureUi();

        if (_fadeRoutine != null)
        {
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }

        SetGroupState(0f, false);
        _isVisible = false;
    }

    private IEnumerator FadeInRoutine()
    {
        _isVisible = true;
        SetGroupState(0f, false);

        if (fadeDuration <= 0f)
        {
            SetGroupState(1f, true);
            _fadeRoutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            rootGroup.alpha = Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }

        SetGroupState(1f, true);
        _fadeRoutine = null;
    }

    private void SetGroupState(float alpha, bool interactive)
    {
        if (rootGroup == null)
            return;

        rootGroup.alpha = alpha;
        rootGroup.interactable = interactive;
        rootGroup.blocksRaycasts = interactive;

        if (confirmButton != null)
            confirmButton.interactable = interactive;
    }

    private void EnsureUi()
    {
        if (rootGroup != null && root != null && gameOverImage != null && confirmButton != null)
        {
            if (gameOverImage.sprite == null && gameOverSprite != null)
                gameOverImage.sprite = gameOverSprite;
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_warnedMissingReferences)
        {
            Debug.LogWarning("[GameOverUIController] UI ì°¸ì¡°ê°€ ?„ì „???°ê²°?˜ì? ?Šì•˜?µë‹ˆ?? ?ë™ UI ?ì„± ?†ì´ ?œì‹œë¥?ê±´ë„ˆ?ë‹ˆ??", this);
            _warnedMissingReferences = true;
        }
#endif
    }

}
