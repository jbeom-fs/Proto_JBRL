using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class EngravingSlotUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private Image borderImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descriptionText;

    public Button Button => button;

    public void BindCard(
        Sprite iconSprite,
        string name,
        string description,
        bool interactable,
        Color tierColor)
    {
        if (icon != null)
            icon.sprite = iconSprite;

        if (nameText != null)
        {
            nameText.text = name;
            nameText.color = tierColor;
        }

        if (descriptionText != null)
            descriptionText.text = description ?? string.Empty;

        if (button != null)
            button.interactable = interactable;

        if (borderImage != null)
            borderImage.color = tierColor;
    }
}
