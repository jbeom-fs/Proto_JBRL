using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PassiveSlotUnlockRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text summaryText;
    [SerializeField] private Button unlockButton;

    private SoulAltarUIController _owner;
    private PlayerFormId _form;
    private bool _listenerRegistered;

    public void Initialize(SoulAltarUIController owner)
    {
        _owner = owner;

        if (unlockButton != null && !_listenerRegistered)
        {
            unlockButton.onClick.AddListener(HandleClicked);
            _listenerRegistered = true;
        }
    }

    public void Bind(
        PlayerFormId form,
        int currentCount,
        int maxCount,
        int cost,
        ItemData shardItem,
        int shardCount)
    {
        _form = form;

        bool maxed = currentCount >= maxCount;
        bool hasEnough = cost <= 0 || (shardItem != null && shardCount >= cost);

        if (summaryText != null)
        {
            summaryText.text = maxed
                ? string.Format(UiMessages.PassiveSlotUnlockMaxFormat, currentCount, maxCount)
                : string.Format(
                    UiMessages.PassiveSlotUnlockFormat,
                    currentCount,
                    maxCount,
                    cost,
                    shardCount);
        }

        if (unlockButton != null)
            unlockButton.interactable = !maxed && hasEnough;

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void HandleClicked()
    {
        _owner?.HandlePassiveUnlockClicked(_form);
    }
}
