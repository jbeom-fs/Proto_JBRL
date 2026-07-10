using TMPro;
using UnityEngine;

public sealed class AilmentStatusSlotView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer iconRenderer;
    [SerializeField] private TextMeshPro stackText;
    [SerializeField] private float iconWorldSize = 0.25f;

    private int _stackCount = -1;

    public void SetIcon(Sprite icon)
    {
        if (iconRenderer == null)
            return;

        iconRenderer.sprite = icon;
        if (icon == null)
        {
            iconRenderer.transform.localScale = Vector3.one;
            return;
        }

        Vector2 size = icon.bounds.size;
        float longest = Mathf.Max(size.x, size.y);
        float scale = longest > 0.0001f ? iconWorldSize / longest : 1f;
        iconRenderer.transform.localScale = new Vector3(scale, scale, scale);
    }

    public void SetStackCount(int count)
    {
        count = Mathf.Max(0, count);
        if (_stackCount == count)
            return;

        _stackCount = count;
        if (stackText == null)
            return;

        bool visible = count > 0;
        stackText.gameObject.SetActive(visible);
        if (visible)
            stackText.SetText(count.ToString());
        else
            stackText.text = string.Empty;
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }
}
