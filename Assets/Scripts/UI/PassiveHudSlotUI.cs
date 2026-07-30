using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class PassiveHudSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image iconImage;

    private EngravingDisplayInfo _info;
    private bool _hasTooltip;
    private RectTransform _rect;

    private void Awake()
    {
        _rect = transform as RectTransform;
    }

    private void OnDisable()
    {
        ItemTooltipUI.HideActive();
    }

    public void BindEngraving(in EngravingDisplayInfo info)
    {
        _info = info;
        _hasTooltip = true;
        if (iconImage != null)
            iconImage.sprite = info.Icon;
    }

    public void BindEmpty(Sprite empty)
    {
        _info = default;
        _hasTooltip = false;
        if (iconImage != null)
            iconImage.sprite = empty;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_hasTooltip)
            ItemTooltipUI.ShowActive(_info, _rect);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ItemTooltipUI.HideActive();
    }
}
