using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SoulAltarStatRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text summaryText;
    [SerializeField] private Button enhanceButton;
    [SerializeField] private SoulStatLabelTable labelTable;

    private SoulAltarUIController _owner;
    private PlayerFormId _form;
    private SoulStatType _stat;
    private bool _listenerRegistered;

    public void Initialize(SoulAltarUIController owner)
    {
        _owner = owner;

        if (enhanceButton != null && !_listenerRegistered)
        {
            enhanceButton.onClick.AddListener(HandleEnhanceClicked);
            _listenerRegistered = true;
        }
    }

    public void Bind(
        PlayerFormId form,
        in SoulStatGrowth growth,
        int currentLevel,
        int materialCost,
        ItemData shardItem,
        int shardCount,
        bool shared = false)
    {
        _form = form;
        _stat = growth.stat;

        int maxLevel = Mathf.Max(0, growth.maxLevel);
        bool maxed = currentLevel >= maxLevel;
        bool hasEnoughMaterial = shared || materialCost <= 0 || (shardItem != null && shardCount >= materialCost);

        if (summaryText != null)
        {
            string statName = labelTable != null ? labelTable.GetStatName(growth.stat) : growth.stat.ToString();
            string costLabel = materialCost > 0 ? materialCost.ToString() : "무료";
            summaryText.text = statName + " Lv." + currentLevel + "/" + maxLevel +
                               "  비용 " + costLabel +
                               (shared ? string.Empty : "  (보유 " + shardCount + ")");
        }

        if (enhanceButton != null)
            enhanceButton.interactable = !maxed && hasEnoughMaterial;

        gameObject.SetActive(true);
    }

    public void SetInteractable(bool interactable)
    {
        if (enhanceButton != null)
            enhanceButton.interactable = interactable;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void HandleEnhanceClicked()
    {
        _owner?.HandleEnhanceClicked(_form, _stat);
    }
}
