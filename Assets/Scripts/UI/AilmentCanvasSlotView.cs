using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class AilmentCanvasSlotView : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text stackText;

    public void SetIcon(Sprite icon)
    {
        if (iconImage != null)
            iconImage.sprite = icon;
    }

    public void SetStack(StatusEffectIconType type, int stackCount)
    {
        if (stackText == null)
            return;

        bool showsStack = type == StatusEffectIconType.Poison || type == StatusEffectIconType.Bleed;
        bool visible = showsStack && stackCount > 0;
        stackText.gameObject.SetActive(visible);
        stackText.text = visible ? stackCount.ToString() : string.Empty;
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }
}
