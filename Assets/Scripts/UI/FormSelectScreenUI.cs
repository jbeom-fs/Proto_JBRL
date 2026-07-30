using System.Collections.Generic;
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
    [SerializeField] private PassiveSelectCardUI[] passiveCards;
    [SerializeField] private Button enterButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerPassiveUnlocks passiveUnlocks;
    [SerializeField] private EngravingLoadout engravingLoadout;
    [SerializeField, Min(0f)] private float lockedPassiveToastDuration = 2f;
    [SerializeField, TeleportDestinationId] private string dungeonDestinationId;
    [SerializeField] private LocationTransitionManager transitionManager;

    private static PlayerFormId? s_LastEnteredForm;

    private PlayerController _player;
    private FormSelectCardUI _selectedCard;
    private PassiveSelectCardUI _selectedPassiveCard;
    private readonly List<PassiveEngravingData> _passiveCatalogBuffer =
        new List<PassiveEngravingData>(8);
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
        _selectedPassiveCard = null;
        if (panel != null)
            panel.SetActive(false);
    }

    public void HandleCardClicked(FormSelectCardUI card)
    {
        if (!IsOpen || card == null || !card.IsOwned)
            return;

        SelectCard(card);
    }

    public void HandlePassiveCardClicked(PassiveSelectCardUI card)
    {
        if (!IsOpen || card == null)
            return;

        if (!card.IsUnlocked)
        {
            ToastUI.Instance?.Show(
                UiMessages.PassiveSelectionLocked,
                lockedPassiveToastDuration);
            return;
        }

        SelectPassiveCard(card);
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

        if (passiveCards != null)
        {
            for (int i = 0; i < passiveCards.Length; i++)
            {
                if (passiveCards[i] != null)
                    passiveCards[i].Initialize(this);
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

        RefreshPassiveCards(selected != null ? selected.Form : default);
    }

    private void RefreshPassiveCards(PlayerFormId form)
    {
        _selectedPassiveCard = null;
        _passiveCatalogBuffer.Clear();
        if (passiveUnlocks != null)
            passiveUnlocks.GetCatalog(form, _passiveCatalogBuffer);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int cardCount = passiveCards?.Length ?? 0;
        if (_passiveCatalogBuffer.Count > cardCount)
        {
            Debug.LogWarning(
                "[FormSelectScreenUI] Passive card capacity exceeded for " +
                form + ": catalog " + _passiveCatalogBuffer.Count +
                ", cards " + cardCount + ".",
                this);
        }
#endif

        for (int i = 0; passiveCards != null && i < passiveCards.Length; i++)
        {
            PassiveSelectCardUI card = passiveCards[i];
            if (card == null)
                continue;

            if (i >= _passiveCatalogBuffer.Count ||
                !EngravingDisplayInfo.TryCreate(
                    _passiveCatalogBuffer[i],
                    out EngravingDisplayInfo info))
            {
                card.Hide();
                continue;
            }

            PassiveEngravingData passive = _passiveCatalogBuffer[i];
            card.Bind(passive, info, passiveUnlocks.IsUnlocked(passive));
            if (i == 0)
                _selectedPassiveCard = card;
        }

        _passiveCatalogBuffer.Clear();
        SelectPassiveCard(_selectedPassiveCard);
    }

    private void SelectPassiveCard(PassiveSelectCardUI selected)
    {
        _selectedPassiveCard = selected;
        for (int i = 0; passiveCards != null && i < passiveCards.Length; i++)
        {
            if (passiveCards[i] != null)
                passiveCards[i].SetSelected(passiveCards[i] == selected);
        }
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

        if (_selectedPassiveCard != null &&
            (engravingLoadout == null ||
             !engravingLoadout.SetSelectedPassive(
                 _selectedCard.Form,
                 _selectedPassiveCard.Passive)))
        {
            Debug.LogWarning(
                "[FormSelectScreenUI] Passive selection apply blocked: " +
                _selectedPassiveCard.Passive + ".",
                this);
            return;
        }

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
