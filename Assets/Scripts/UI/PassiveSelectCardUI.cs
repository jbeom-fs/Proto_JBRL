using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PassiveSelectCardUI : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private GameObject lockedOverlay;
    [SerializeField] private GameObject selectedHighlight;
    [SerializeField] private Button selectButton;

    private FormSelectScreenUI _owner;
    private bool _listenerRegistered;

    public PassiveEngravingData Passive { get; private set; }
    public bool IsUnlocked { get; private set; }

    public void Initialize(FormSelectScreenUI owner)
    {
        _owner = owner;
        if (_listenerRegistered || selectButton == null)
            return;

        selectButton.onClick.AddListener(HandleClicked);
        _listenerRegistered = true;
    }

    public void Bind(
        PassiveEngravingData passive,
        in EngravingDisplayInfo info,
        bool unlocked)
    {
        Passive = passive;
        IsUnlocked = unlocked;

        if (iconImage != null)
        {
            iconImage.sprite = info.Icon;
            iconImage.enabled = info.Icon != null;
        }

        if (nameText != null)
            nameText.text = info.Name;
        if (descriptionText != null)
            descriptionText.text = info.Description;
        if (lockedOverlay != null)
            lockedOverlay.SetActive(!unlocked);

        SetSelected(false);
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        Passive = null;
        IsUnlocked = false;
        SetSelected(false);
        if (lockedOverlay != null)
            lockedOverlay.SetActive(false);

        gameObject.SetActive(false);
    }

    public void SetSelected(bool selected)
    {
        if (selectedHighlight != null)
            selectedHighlight.SetActive(selected);
    }

    private void HandleClicked()
    {
        _owner?.HandlePassiveCardClicked(this);
    }
}
