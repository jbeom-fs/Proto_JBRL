using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class StatusEffectIconView : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text timeText;

    private int _lastDisplayedTenths = int.MinValue;

    public void SetIcon(Sprite sprite)
    {
        if (iconImage != null)
            iconImage.sprite = sprite;
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
        _lastDisplayedTenths = int.MinValue;

        if (!visible)
            SetTime(0f, 0f);
    }

    public void SetTime(float remainingTime, float ratio)
    {
        if (fillImage != null)
            fillImage.fillAmount = Mathf.Clamp01(ratio);

        if (timeText != null)
        {
            int tenths = remainingTime > 0f
                ? Mathf.RoundToInt(remainingTime * 10f)
                : int.MinValue;
            if (tenths == _lastDisplayedTenths)
                return;

            _lastDisplayedTenths = tenths;
            timeText.text = tenths != int.MinValue
                ? (tenths * 0.1f).ToString("0.0")
                : string.Empty;
        }
    }

    public void MoveToLast()
    {
        transform.SetAsLastSibling();
    }
}
