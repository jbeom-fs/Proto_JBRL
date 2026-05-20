using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class InventorySlotUI : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private TMP_Text nameText;

    public void Bind(ItemData item, int count)
    {
        bool hasItem = item != null && count > 0;
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
}
