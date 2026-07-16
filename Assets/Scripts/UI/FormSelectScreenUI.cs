using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class FormSelectScreenUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TMP_Text formNameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private FormSelectCardUI[] cards;
    [SerializeField] private Button enterButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField, TeleportDestinationId] private string dungeonDestinationId;
    [SerializeField] private LocationTransitionManager transitionManager;

    private static PlayerFormId? s_LastEnteredForm;

    private PlayerController _player;
    private FormSelectCardUI _selectedCard;
    private bool _initialized;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        EnsureInitialized();
        Close();
    }

    private void Update()
    {
        if (!IsOpen)
            return;

        if (DeveloperConsoleUI.IsOpen)
        {
            Close();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            Close();
    }

    public void Open(PlayerController player)
    {
        EnsureInitialized();
        if (player == null || panel == null || playerInventory == null || cards == null)
            return;

        _player = player;
        for (int i = 0; i < cards.Length; i++)
        {
            FormSelectCardUI card = cards[i];
            if (card == null)
                continue;

            card.Refresh(playerInventory.OwnsSoulForm(card.Form));
            card.SetSelected(false);
        }

        FormSelectCardUI defaultCard = s_LastEnteredForm.HasValue
            ? FindOwnedCard(s_LastEnteredForm.Value)
            : null;
        if (defaultCard == null)
            defaultCard = FindOwnedCard(PlayerFormId.Sword);
        if (defaultCard == null)
            defaultCard = FindFirstOwnedCard();

        IsOpen = true;
        panel.SetActive(true);
        SelectCard(defaultCard);
    }

    public void Close()
    {
        IsOpen = false;
        _player = null;
        _selectedCard = null;
        if (panel != null)
            panel.SetActive(false);
    }

    public void HandleCardClicked(FormSelectCardUI card)
    {
        if (!IsOpen || card == null || !card.IsOwned)
            return;

        SelectCard(card);
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        if (cards != null)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i] != null)
                    cards[i].Initialize(this);
            }
        }

        if (enterButton != null)
            enterButton.onClick.AddListener(EnterDungeon);
        if (exitButton != null)
            exitButton.onClick.AddListener(Close);

        _initialized = true;
    }

    private void SelectCard(FormSelectCardUI selected)
    {
        _selectedCard = selected;
        if (cards != null)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i] != null)
                    cards[i].SetSelected(cards[i] == selected);
            }
        }

        Sprite background = selected != null ? selected.BackgroundIllust : null;
        if (backgroundImage != null)
        {
            backgroundImage.sprite = background;
            backgroundImage.enabled = background != null;
        }

        if (formNameText != null)
            formNameText.text = selected != null ? selected.DisplayName : string.Empty;
        if (descriptionText != null)
            descriptionText.text = selected != null ? selected.Description : string.Empty;
        if (enterButton != null)
            enterButton.interactable = selected != null;
    }

    private void EnterDungeon()
    {
        if (!IsOpen ||
            _player == null ||
            _selectedCard == null ||
            transitionManager == null ||
            string.IsNullOrWhiteSpace(dungeonDestinationId))
        {
            return;
        }

        PlayerFormController forms = _player.GetComponent<PlayerFormController>();
        if (forms == null)
            return;

        FormSwitchResult result = forms.TrySwitchForm(_selectedCard.Form);
        if (result != FormSwitchResult.Switched && result != FormSwitchResult.AlreadyActive)
        {
            Debug.LogWarning("[FormSelectScreenUI] Form switch blocked: " + result + ".", this);
            return;
        }

        s_LastEnteredForm = _selectedCard.Form;
        PlayerController player = _player;
        transitionManager.TeleportPlayer(player, dungeonDestinationId);
        Close();
    }

    private FormSelectCardUI FindOwnedCard(PlayerFormId form)
    {
        if (cards == null)
            return null;

        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] != null && cards[i].Form == form && cards[i].IsOwned)
                return cards[i];
        }

        return null;
    }

    private FormSelectCardUI FindFirstOwnedCard()
    {
        if (cards == null)
            return null;

        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] != null && cards[i].IsOwned)
                return cards[i];
        }

        return null;
    }
}
