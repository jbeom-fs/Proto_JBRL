using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class RestAreaShopUIController : MonoBehaviour
{
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private RestAreaShopTable shopTable;
    [SerializeField] private string coreItemCode = ItemCodes.RunCore;
    [SerializeField] private string currencyItemCode = "Currency";

    [Header("Scene UI")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text currencyText;
    [SerializeField] private TMP_Text emptyText;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private Button closeButton;
    [SerializeField] private OfferRowUI[] offerRows;

    private bool _subscribed;
    private bool _warnedMissingReferences;
    private bool _warnedMissingCore;
    private bool _warnedMissingCurrency;
    private bool _warnedOfferRowOverflow;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        RegisterButtons();
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void OnDisable()
    {
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

    public void Open()
    {
        if (IsOpen)
            return;

        if (!HasRequiredReferences())
            return;

        Subscribe();
        IsOpen = true;
        SetFeedback(string.Empty);

        panelRoot.SetActive(true);
        GamePauseController.Active?.Pause(GamePauseSource.RestAreaShop);
        RefreshAll();
    }

    public void Close()
    {
        bool wasOpen = IsOpen;
        IsOpen = false;
        Unsubscribe();

        if (panelRoot != null)
            panelRoot.SetActive(false);

        if (wasOpen)
            GamePauseController.Active?.Resume(GamePauseSource.RestAreaShop);
    }

    private void RegisterButtons()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (offerRows == null)
            return;

        for (int i = 0; i < offerRows.Length; i++)
        {
            OfferRowUI row = offerRows[i];
            if (row == null || row.PurchaseButton == null)
                continue;

            row.PurchaseButton.onClick.AddListener(() => HandleBuyClicked(row));
        }
    }

    private void HandleBuyClicked(OfferRowUI row)
    {
        if (row == null || row.Offer == null)
            return;

        TryPurchase(row.Offer);
    }

    private void TryPurchase(RestAreaShopOffer offer)
    {
        if (!HasRequiredReferences())
            return;

        if (!TryFindCore(out ItemData core))
        {
            WarnMissingCore();
            SetFeedback("Core missing");
            RefreshAll();
            return;
        }

        if (!TryResolveCurrency(out ItemData currency))
        {
            WarnMissingCurrency();
            SetFeedback("Currency missing");
            RefreshAll();
            return;
        }

        int level = CountEffectLevel(core, offer.EffectType);
        int cost = RestAreaShopTable.GetCost(offer, level);
        int balance = playerInventory.GetItemCount(currency);
        if (balance < cost)
        {
            SetFeedback("Not enough currency");
            RefreshAll();
            return;
        }

        if (!core.AddPassiveEffectRuntime(new ItemEffect
            {
                type = offer.EffectType,
                value = offer.PerLevelValue
            }))
        {
            SetFeedback("Core update blocked");
            RefreshAll();
            return;
        }

        if (cost > 0 && !playerInventory.RemoveItem(currency, cost))
        {
            SetFeedback("Purchase failed");
            playerInventory.NotifyExternalChange();
            RefreshAll();
            return;
        }

        playerInventory.NotifyExternalChange();
        SetFeedback("Purchased " + offer.DisplayName);
        RefreshAll();
    }

    private void RefreshAll()
    {
        if (!IsOpen || shopTable == null)
            return;

        TryResolveCurrency(out ItemData currency);
        int balance = currency != null ? playerInventory.GetItemCount(currency) : 0;
        if (currencyText != null)
            currencyText.text = "Currency " + balance.ToString();

        bool hasCore = TryFindCore(out ItemData core);
        if (!hasCore)
            WarnMissingCore();

        IReadOnlyList<RestAreaShopOffer> entries = shopTable.Entries;
        int rowCount = offerRows != null ? offerRows.Length : 0;
        int visibleCount = Mathf.Min(entries.Count, rowCount);
        if (entries.Count > rowCount)
            WarnOfferRowOverflow(entries.Count, rowCount);

        for (int i = 0; i < rowCount; i++)
        {
            OfferRowUI row = offerRows[i];
            if (row == null)
                continue;

            if (i >= visibleCount)
            {
                row.SetActive(false);
                continue;
            }

            RestAreaShopOffer offer = entries[i];
            int level = hasCore ? CountEffectLevel(core, offer.EffectType) : 0;
            int totalValue = level * offer.PerLevelValue;
            int cost = RestAreaShopTable.GetCost(offer, level);
            row.Bind(offer, level, totalValue, cost, hasCore && currency != null && balance >= cost);
        }

        if (emptyText != null)
        {
            bool showEmpty = entries.Count == 0 || !hasCore;
            emptyText.gameObject.SetActive(showEmpty);
            emptyText.text = !hasCore ? "Core missing" : "No offers";
        }
    }

    private bool TryFindCore(out ItemData core)
    {
        core = null;

        if (playerInventory == null)
            return false;

        IReadOnlyList<InventoryItemStack> items = playerInventory.Items;
        for (int i = 0; i < items.Count; i++)
        {
            InventoryItemStack stack = items[i];
            ItemData item = stack != null ? stack.Item : null;
            if (item != null && stack.Count > 0 && item.ItemCode == coreItemCode)
            {
                core = item;
                return true;
            }
        }

        return false;
    }

    private bool TryResolveCurrency(out ItemData currency)
    {
        currency = null;
        return playerInventory != null &&
               !string.IsNullOrWhiteSpace(currencyItemCode) &&
               playerInventory.TryGetDatabaseItem(currencyItemCode, out currency) &&
               currency != null;
    }

    private static int CountEffectLevel(ItemData core, ItemEffectType effectType)
    {
        if (core == null)
            return 0;

        IReadOnlyList<ItemEffect> effects = core.PassiveEffects;
        int count = 0;
        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i].type == effectType)
                count++;
        }

        return count;
    }

    private bool HasRequiredReferences()
    {
        bool valid = playerInventory != null &&
                     shopTable != null &&
                     panelRoot != null &&
                     currencyText != null &&
                     emptyText != null &&
                     feedbackText != null &&
                     closeButton != null &&
                     offerRows != null &&
                     offerRows.Length > 0;

        if (valid)
        {
            for (int i = 0; i < offerRows.Length; i++)
            {
                if (offerRows[i] == null || offerRows[i].PurchaseButton == null)
                {
                    valid = false;
                    break;
                }
            }
        }

        if (!valid && !_warnedMissingReferences)
        {
            Debug.LogWarning("[RestAreaShopUIController] Required scene references are not assigned.", this);
            _warnedMissingReferences = true;
        }

        return valid;
    }

    private void WarnMissingCore()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedMissingCore)
            return;

        _warnedMissingCore = true;
        Debug.LogWarning("[RestAreaShopUIController] Core item is missing from inventory.", this);
#endif
    }

    private void WarnMissingCurrency()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedMissingCurrency)
            return;

        _warnedMissingCurrency = true;
        Debug.LogWarning("[RestAreaShopUIController] Currency item is missing from PlayerInventory database.", this);
#endif
    }

    private void WarnOfferRowOverflow(int entryCount, int rowCount)
    {
        if (_warnedOfferRowOverflow)
            return;

        _warnedOfferRowOverflow = true;
        Debug.LogWarning(
            "[RestAreaShopUIController] Shop entries exceed scene offer rows. entries=" +
            entryCount +
            " rows=" +
            rowCount,
            this);
    }

    private void Subscribe()
    {
        if (_subscribed || playerInventory == null)
            return;

        playerInventory.OnInventoryChanged += RefreshAll;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;

        if (playerInventory != null)
            playerInventory.OnInventoryChanged -= RefreshAll;

        _subscribed = false;
    }

    private void SetFeedback(string message)
    {
        if (feedbackText != null)
            feedbackText.text = message;
    }
}
