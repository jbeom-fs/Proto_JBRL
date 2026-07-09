using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ItemTooltipUI : MonoBehaviour
{
    private const string EmptyDescriptionText = "내용없음";
    private const string EmptyCoreEffectText = "효과 없음";
    private const float EdgePadding = 12f;
    private const float SlotOffset = 12f;

    private static ItemTooltipUI s_Active;

    [SerializeField] private RectTransform panelRect;
    [SerializeField] private TMP_Text headerText;
    [SerializeField] private TMP_Text bodyText;

    private readonly Dictionary<ItemEffectType, int> _effectTotals = new Dictionary<ItemEffectType, int>();
    private readonly StringBuilder _bodyBuilder = new StringBuilder(128);
    private readonly Vector3[] _slotCorners = new Vector3[4];

    private Canvas _canvas;
    private RectTransform _canvasRect;
    private bool _warnedMissingReferences;

    public static ItemTooltipUI Active => s_Active;

    private void Awake()
    {
        if (s_Active != null && s_Active != this)
            Debug.LogWarning("[ItemTooltipUI] Duplicate instance detected. Latest instance is now active.", this);

        s_Active = this;
        ResolveCanvas();
        Hide();
    }

    private void OnDisable()
    {
        Hide();
        if (s_Active == this)
            s_Active = null;
    }

    public static void ShowActive(ItemData item, RectTransform slotRect)
    {
        ItemTooltipUI tooltip = s_Active != null ? s_Active : FindAnyObjectByType<ItemTooltipUI>();
        tooltip?.Show(item, slotRect);
    }

    public static void HideActive()
    {
        ItemTooltipUI tooltip = s_Active != null ? s_Active : FindAnyObjectByType<ItemTooltipUI>();
        tooltip?.Hide();
    }

    public void Show(ItemData item, RectTransform slotRect)
    {
        if (item == null || slotRect == null)
            return;

        if (!HasRequiredReferences())
            return;

        headerText.text = string.IsNullOrWhiteSpace(item.DisplayName) ? item.ItemCode : item.DisplayName;
        bodyText.text = BuildBodyText(item);
        panelRect.gameObject.SetActive(true);

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        PositionNearSlot(slotRect);
    }

    public void Hide()
    {
        if (panelRect != null)
            panelRect.gameObject.SetActive(false);
    }

    private string BuildBodyText(ItemData item)
    {
        if (item.ItemCode == ItemCodes.RunCore)
            return BuildCoreBodyText(item);

        return string.IsNullOrWhiteSpace(item.Description) ? EmptyDescriptionText : item.Description;
    }

    private string BuildCoreBodyText(ItemData item)
    {
        _effectTotals.Clear();
        IReadOnlyList<ItemEffect> effects = item.PassiveEffects;
        for (int i = 0; i < effects.Count; i++)
        {
            ItemEffect effect = effects[i];
            if (effect.type == ItemEffectType.None || effect.value == 0)
                continue;

            _effectTotals.TryGetValue(effect.type, out int total);
            _effectTotals[effect.type] = total + effect.value;
        }

        if (_effectTotals.Count == 0)
            return EmptyCoreEffectText;

        _bodyBuilder.Clear();
        AppendEffectLine(ItemEffectType.AttackBonus);
        AppendEffectLine(ItemEffectType.DefenseBonus);
        AppendEffectLine(ItemEffectType.MaxHpBonus);
        AppendEffectLine(ItemEffectType.MoveSpeedBonus);

        foreach (KeyValuePair<ItemEffectType, int> pair in _effectTotals)
        {
            if (IsKnownCoreEffect(pair.Key))
                continue;

            AppendEffectLine(pair.Key, pair.Value);
        }

        return _bodyBuilder.ToString();
    }

    private void AppendEffectLine(ItemEffectType type)
    {
        if (!_effectTotals.TryGetValue(type, out int value))
            return;

        AppendEffectLine(type, value);
    }

    private void AppendEffectLine(ItemEffectType type, int value)
    {
        if (_bodyBuilder.Length > 0)
            _bodyBuilder.AppendLine();

        _bodyBuilder.Append(GetEffectLabel(type));
        _bodyBuilder.Append(' ');
        if (value >= 0)
            _bodyBuilder.Append('+');
        _bodyBuilder.Append(value);
        if (type == ItemEffectType.MoveSpeedBonus)
            _bodyBuilder.Append('%');
    }

    private static bool IsKnownCoreEffect(ItemEffectType type)
    {
        return type == ItemEffectType.AttackBonus ||
               type == ItemEffectType.DefenseBonus ||
               type == ItemEffectType.MaxHpBonus ||
               type == ItemEffectType.MoveSpeedBonus;
    }

    private static string GetEffectLabel(ItemEffectType type)
    {
        switch (type)
        {
            case ItemEffectType.AttackBonus:
                return "공격";
            case ItemEffectType.DefenseBonus:
                return "방어";
            case ItemEffectType.MaxHpBonus:
                return "최대체력";
            case ItemEffectType.MoveSpeedBonus:
                return "이동속도";
            default:
                return type.ToString();
        }
    }

    private void PositionNearSlot(RectTransform slotRect)
    {
        if (_canvasRect == null)
            ResolveCanvas();

        if (_canvasRect == null || panelRect == null)
            return;

        Camera uiCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? _canvas.worldCamera
            : null;

        slotRect.GetWorldCorners(_slotCorners);
        Vector2 topRight = WorldToCanvasLocal(_slotCorners[2], uiCamera);
        Vector2 topLeft = WorldToCanvasLocal(_slotCorners[1], uiCamera);

        Vector2 size = panelRect.rect.size;
        Rect canvasBounds = _canvasRect.rect;
        Vector2 position = topRight + new Vector2(SlotOffset, 0f);

        if (position.x + size.x > canvasBounds.xMax - EdgePadding)
            position.x = topLeft.x - size.x - SlotOffset;

        position.x = Mathf.Clamp(
            position.x,
            canvasBounds.xMin + EdgePadding,
            canvasBounds.xMax - size.x - EdgePadding);
        position.y = Mathf.Clamp(
            position.y,
            canvasBounds.yMin + size.y + EdgePadding,
            canvasBounds.yMax - EdgePadding);

        panelRect.anchoredPosition = position;
    }

    private Vector2 WorldToCanvasLocal(Vector3 worldPosition, Camera uiCamera)
    {
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, worldPosition);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect,
            screenPoint,
            uiCamera,
            out Vector2 localPoint);
        return localPoint;
    }

    private bool HasRequiredReferences()
    {
        bool valid = panelRect != null && headerText != null && bodyText != null;
        if (valid)
            return true;

        if (!_warnedMissingReferences)
        {
            Debug.LogWarning("[ItemTooltipUI] Scene references are not assigned.", this);
            _warnedMissingReferences = true;
        }

        return false;
    }

    private void ResolveCanvas()
    {
        _canvas = GetComponentInParent<Canvas>();
        _canvasRect = _canvas != null ? _canvas.GetComponent<RectTransform>() : null;
    }
}
