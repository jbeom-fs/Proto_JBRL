using UnityEngine;
using UnityEngine.UI;

public sealed class FormSelectCardUI : MonoBehaviour
{
    [SerializeField] private PlayerFormId form;
    [SerializeField] private Image cardImage;
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite silhouetteSprite;
    [SerializeField] private Sprite backgroundIllust;
    [SerializeField] private GameObject selectedHighlight;
    [SerializeField] private Button selectButton;

    private FormSelectScreenUI _owner;
    private bool _listenerRegistered;

    public PlayerFormId Form => form;
    public bool IsOwned { get; private set; }
    public Sprite BackgroundIllust => backgroundIllust;
    public string DisplayName => UiMessages.GetFormName(form);
    public string Description => UiMessages.GetFormDescription(form);

    public void Initialize(FormSelectScreenUI owner)
    {
        _owner = owner;
        if (_listenerRegistered || selectButton == null)
            return;

        selectButton.onClick.AddListener(HandleClicked);
        _listenerRegistered = true;
    }

    public void Refresh(bool owned)
    {
        IsOwned = owned;
        if (cardImage == null)
            return;

        cardImage.sprite = owned ? normalSprite : silhouetteSprite;
        cardImage.enabled = cardImage.sprite != null;
    }

    public void SetSelected(bool selected)
    {
        if (selectedHighlight != null)
            selectedHighlight.SetActive(selected);
    }

    private void HandleClicked()
    {
        _owner?.HandleCardClicked(this);
    }
}
