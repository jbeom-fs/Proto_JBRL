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

        if (targetCanvas == null)
            targetCanvas = FindAnyObjectByType<Canvas>();

        if (targetCanvas == null)
            return;

        BuildDefaultUi(targetCanvas.transform);
    }

    private void BuildDefaultUi(Transform parent)
    {
        GameObject rootObject = new GameObject("GameOver UI Root", typeof(RectTransform), typeof(CanvasGroup));
        rootObject.transform.SetParent(parent, false);
        root = rootObject.GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        rootGroup = rootObject.GetComponent<CanvasGroup>();

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(root, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.72f);

        GameObject imageObject = new GameObject("GameOverScene Image", typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(root, false);
        RectTransform imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = new Vector2(0.5f, 0.5f);
        imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        imageRect.pivot = new Vector2(0.5f, 0.5f);
        imageRect.anchoredPosition = new Vector2(0f, 70f);
        imageRect.sizeDelta = new Vector2(720f, 405f);
        gameOverImage = imageObject.GetComponent<Image>();
        gameOverImage.sprite = gameOverSprite;
        gameOverImage.preserveAspect = true;

        GameObject buttonObject = new GameObject("Confirm Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(root, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(0f, -190f);
        buttonRect.sizeDelta = new Vector2(180f, 54f);
        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = new Color(0.12f, 0.1f, 0.16f, 0.94f);
        confirmButton = buttonObject.GetComponent<Button>();

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        Text buttonText = textObject.GetComponent<Text>();
        buttonText.text = "확인";
        buttonText.alignment = TextAnchor.MiddleCenter;
        buttonText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        buttonText.fontSize = 26;
        buttonText.color = Color.white;
    }
}
