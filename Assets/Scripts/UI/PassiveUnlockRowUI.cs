using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PassiveUnlockRowUI : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text passiveNameText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text ownedText;
    [SerializeField] private Button unlockButton;
    [SerializeField] private TMP_Text unlockButtonText;

    private SoulAltarUIController _owner;
    private PlayerFormId _form;
    private PassiveEngravingData _passive;
    private bool _listenerRegistered;

    public void Initialize(SoulAltarUIController owner)
    {
        _owner = owner;
        if (_listenerRegistered || unlockButton == null)
            return;

        unlockButton.onClick.AddListener(HandleUnlockClicked);
        _listenerRegistered = true;
    }

    public void Bind(
        PlayerFormId form,
        PassiveEngravingData passive,
        int cost,
        ItemData shardItem,
        int shardCount,
        bool unlocked)
    {
        _form = form;
        _passive = passive;

        if (iconImage != null)
        {
            iconImage.sprite = passive != null ? passive.icon : null;
            iconImage.enabled = iconImage.sprite != null;
        }

        if (passiveNameText != null)
        {
            passiveNameText.text = passive != null &&
                                   !string.IsNullOrWhiteSpace(passive.passiveName)
                ? passive.passiveName
                : passive != null ? passive.name : string.Empty;
        }

        if (costText != null)
        {
            costText.text = unlocked
                ? UiMessages.PassiveUnlockCostComplete
                : string.Format(UiMessages.PassiveUnlockCostFormat, cost);
        }

        if (ownedText != null)
        {
            ownedText.text = string.Format(
                UiMessages.PassiveUnlockOwnedFormat,
                shardCount);
        }

        if (unlockButtonText != null)
        {
            unlockButtonText.text = unlocked
                ? UiMessages.PassiveUnlockComplete
                : UiMessages.PassiveUnlockAction;
        }

        if (unlockButton != null)
        {
            bool hasEnoughMaterial =
                cost <= 0 || (shardItem != null && shardCount >= cost);
            unlockButton.interactable = !unlocked && hasEnoughMaterial;
        }

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        _passive = null;
        gameObject.SetActive(false);
    }

    private void HandleUnlockClicked()
    {
        _owner?.HandlePassiveUnlockClicked(_form, _passive);
    }
}
