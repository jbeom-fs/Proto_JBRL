using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class InventorySlotUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private TMP_Text nameText;

    private InventoryUIController _owner;
    private ItemData _item;
    private EngravingDisplayInfo _engravingInfo;
    private bool _hasEngraving;

    public void Bind(ItemData item, int count)
        => Bind(item, count, null);

    public void Bind(ItemData item, int count, InventoryUIController owner)
    {
        ItemTooltipUI.HideActive();

        bool hasItem = item != null && count > 0;
        _owner = hasItem ? owner : null;
        _item = hasItem ? item : null;
        _engravingInfo = default;
        _hasEngraving = false;

        gameObject.SetActive(hasItem);
        if (!hasItem)
            return;

        if (iconImage != null)
        {
            iconImage.sprite = item.Icon;
            iconImage.enabled = item.Icon != null;
        }

        if (countText != null)
        {
            countText.gameObject.SetActive(count > 1);
            countText.text = count.ToString();
        }

        if (nameText != null)
            nameText.text = item.DisplayName;
    }

    public void Bind(EngravingDisplayInfo info)
    {
        ItemTooltipUI.HideActive();

        bool hasEngraving = !string.IsNullOrWhiteSpace(info.Name);
        _owner = null;
        _item = null;
        _engravingInfo = hasEngraving ? info : default;
        _hasEngraving = hasEngraving;

        gameObject.SetActive(hasEngraving);
        if (!hasEngraving)
            return;

        if (iconImage != null)
        {
            iconImage.sprite = info.Icon;
            iconImage.enabled = info.Icon != null;
        }

        if (countText != null)
        {
            countText.gameObject.SetActive(false);
            countText.text = string.Empty;
        }

        if (nameText != null)
            nameText.text = info.Name;
    }

    private void OnDisable()
    {
        ItemTooltipUI.HideActive();
    }

    public void SetReferences(Image icon, TMP_Text count, TMP_Text itemName)
    {
        iconImage = icon;
        countText = count;
        nameText = itemName;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            return;

        if (_item == null)
            return;

        _owner?.HandleSlotClicked(_item);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            return;

        RectTransform slotRect = transform as RectTransform;
        if (_item != null)
            ItemTooltipUI.ShowActive(_item, slotRect);
        else if (_hasEngraving)
            ItemTooltipUI.ShowActive(_engravingInfo, slotRect);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ItemTooltipUI.HideActive();
    }
}
