using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class EngravingSlotUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Image icon;

    public Button Button => button;

    public void Bind(string text, Sprite iconSprite, bool interactable)
    {
        if (label != null)
            label.text = text;

        if (icon != null)
            icon.sprite = iconSprite;

        if (button != null)
            button.interactable = interactable;
    }
}
