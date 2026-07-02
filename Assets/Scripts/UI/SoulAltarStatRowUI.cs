using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SoulAltarStatRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text summaryText;
    [SerializeField] private Button enhanceButton;

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

    public void Bind(PlayerFormId form, in SoulStatGrowth growth, int currentLevel, int materialCost, ItemData shardItem, int shardCount)
    {
        _form = form;
        _stat = growth.stat;

        int maxLevel = Mathf.Max(0, growth.maxLevel);
        bool maxed = currentLevel >= maxLevel;
        bool hasEnoughMaterial = materialCost <= 0 || (shardItem != null && shardCount >= materialCost);
        string shardName = shardItem != null ? shardItem.DisplayName : "Shard missing";

        if (summaryText != null)
        {
            string costLabel = materialCost > 0 ? materialCost.ToString() : "Free";
            summaryText.text = growth.stat + "  " +
                               currentLevel.ToString() + " / " + maxLevel.ToString() +
                               "  Cost " + costLabel +
                               "  " + shardName + " x" + shardCount.ToString();
        }

        if (enhanceButton != null)
            enhanceButton.interactable = !maxed && hasEnoughMaterial;

        gameObject.SetActive(true);
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
