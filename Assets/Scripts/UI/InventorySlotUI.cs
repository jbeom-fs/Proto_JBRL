using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class InventorySlotUI : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private TMP_Text nameText;

    private InventoryUIController _owner;
    private ItemData _item;

    public void Bind(ItemData item, int count)
        => Bind(item, count, null);

    public void Bind(ItemData item, int count, InventoryUIController owner)
    {
        bool hasItem = item != null && count > 0;
        _owner = hasItem ? owner : null;
        _item = hasItem ? item : null;

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
}
