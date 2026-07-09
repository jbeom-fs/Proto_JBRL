using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class RestAreaShopUIController : MonoBehaviour
{
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private RestAreaShopTable shopTable;
    [SerializeField] private string coreItemCode = "Core";
    [SerializeField] private string currencyItemCode = "Currency";

    private readonly List<OfferRow> _rows = new List<OfferRow>(4);

    private GameObject _panel;
    private Transform _rowContainer;
    private TMP_Text _currencyText;
    private TMP_Text _emptyText;
    private TMP_Text _feedbackText;
    private bool _subscribed;
    private bool _warnedMissingReferences;
    private bool _warnedMissingCore;
    private bool _warnedMissingCurrency;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        EnsureUi();
        if (_panel != null)
            _panel.SetActive(false);
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

        EnsureUi();
        Subscribe();
        IsOpen = true;
        SetFeedback(string.Empty);

        if (_panel != null)
            _panel.SetActive(true);

        GamePauseController.Active?.Pause(GamePauseSource.RestAreaShop);
        RefreshAll();
    }

    public void Close()
    {
        bool wasOpen = IsOpen;
        IsOpen = false;
        Unsubscribe();

        if (_panel != null)
            _panel.SetActive(false);

        if (wasOpen)
            GamePauseController.Active?.Resume(GamePauseSource.RestAreaShop);
    }

    private void HandleBuyClicked(OfferRow row)
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
        if (_currencyText != null)
            _currencyText.text = "Currency " + balance.ToString();

        bool hasCore = TryFindCore(out ItemData core);
        if (!hasCore)
            WarnMissingCore();

        IReadOnlyList<RestAreaShopOffer> entries = shopTable.Entries;
        EnsureRowCount(entries.Count);
        for (int i = 0; i < _rows.Count; i++)
        {
            if (i >= entries.Count)
            {
                _rows[i].SetActive(false);
                continue;
            }

            RestAreaShopOffer offer = entries[i];
            int level = hasCore ? CountEffectLevel(core, offer.EffectType) : 0;
            int totalValue = level * offer.PerLevelValue;
            int cost = RestAreaShopTable.GetCost(offer, level);
            _rows[i].Bind(offer, level, totalValue, cost, hasCore && currency != null && balance >= cost);
        }

        if (_emptyText != null)
        {
            bool showEmpty = entries.Count == 0 || !hasCore;
            _emptyText.gameObject.SetActive(showEmpty);
            _emptyText.text = !hasCore ? "Core missing" : "No offers";
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
        bool valid = playerInventory != null && shopTable != null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!valid && !_warnedMissingReferences)
        {
            Debug.LogWarning("[RestAreaShopUIController] Required references are not assigned.", this);
            _warnedMissingReferences = true;
        }
#endif

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
        if (_feedbackText != null)
            _feedbackText.text = message;
    }

    private void EnsureUi()
    {
        if (_panel != null)
            return;

        _panel = CreateUiObject("RestAreaShopRoot", transform);
        RectTransform panelRect = _panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(640f, 400f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);

        Image panelImage = _panel.AddComponent<Image>();
        panelImage.color = new Color(0.08f, 0.09f, 0.11f, 0.96f);

        VerticalLayoutGroup layout = _panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        GameObject header = CreateUiObject("Header", _panel.transform);
        HorizontalLayoutGroup headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        AddLayoutElement(header, -1f, 36f, 1f);

        TMP_Text title = CreateText("Title", header.transform, 24f, TextAlignmentOptions.Left, Color.white);
        title.text = "Rest Area Shop";
        AddLayoutElement(title.gameObject, 1f, 36f, 1f);

        Button closeButton = CreateButton("CloseButton", header.transform, "X");
        closeButton.onClick.AddListener(Close);
        AddLayoutElement(closeButton.gameObject, 42f, 36f, 0f);

        _currencyText = CreateText("CurrencyText", _panel.transform, 17f, TextAlignmentOptions.Left, new Color(0.92f, 0.9f, 0.72f, 1f));
        AddLayoutElement(_currencyText.gameObject, -1f, 26f, 0f);

        _rowContainer = CreateUiObject("Rows", _panel.transform).transform;
        VerticalLayoutGroup rowsLayout = _rowContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        rowsLayout.spacing = 8f;
        rowsLayout.childControlWidth = true;
        rowsLayout.childControlHeight = true;
        rowsLayout.childForceExpandWidth = true;
        rowsLayout.childForceExpandHeight = false;
        AddLayoutElement(_rowContainer.gameObject, -1f, 236f, 1f);

        _emptyText = CreateText("EmptyText", _panel.transform, 16f, TextAlignmentOptions.Center, new Color(0.86f, 0.86f, 0.86f, 1f));
        AddLayoutElement(_emptyText.gameObject, -1f, 28f, 0f);

        _feedbackText = CreateText("FeedbackText", _panel.transform, 15f, TextAlignmentOptions.Center, new Color(0.74f, 0.86f, 1f, 1f));
        AddLayoutElement(_feedbackText.gameObject, -1f, 28f, 0f);
    }

    private void EnsureRowCount(int count)
    {
        if (_rowContainer == null)
            return;

        while (_rows.Count < count)
        {
            OfferRow row = CreateRow(_rowContainer);
            row.PurchaseButton.onClick.AddListener(() => HandleBuyClicked(row));
            _rows.Add(row);
        }
    }

    private OfferRow CreateRow(Transform parent)
    {
        GameObject root = CreateUiObject("OfferRow", parent);
        Image image = root.AddComponent<Image>();
        image.color = new Color(0.15f, 0.16f, 0.18f, 0.95f);

        HorizontalLayoutGroup layout = root.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        AddLayoutElement(root, -1f, 48f, 0f);

        TMP_Text nameText = CreateText("Name", root.transform, 17f, TextAlignmentOptions.Left, Color.white);
        AddLayoutElement(nameText.gameObject, 150f, 32f, 0f);

        TMP_Text levelText = CreateText("Level", root.transform, 15f, TextAlignmentOptions.Left, new Color(0.85f, 0.88f, 0.92f, 1f));
        AddLayoutElement(levelText.gameObject, 200f, 32f, 1f);

        TMP_Text costText = CreateText("Cost", root.transform, 15f, TextAlignmentOptions.Right, new Color(0.92f, 0.9f, 0.72f, 1f));
        AddLayoutElement(costText.gameObject, 100f, 32f, 0f);

        Button buyButton = CreateButton("BuyButton", root.transform, "Buy");
        AddLayoutElement(buyButton.gameObject, 72f, 32f, 0f);

        return new OfferRow(root, nameText, levelText, costText, buyButton);
    }

    private GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject uiObject = new GameObject(objectName, typeof(RectTransform));
        uiObject.layer = gameObject.layer;
        uiObject.transform.SetParent(parent, false);
        return uiObject;
    }

    private TMP_Text CreateText(string objectName, Transform parent, float fontSize, TextAlignmentOptions alignment, Color color)
    {
        GameObject textObject = CreateUiObject(objectName, parent);
        TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private Button CreateButton(string objectName, Transform parent, string label)
    {
        GameObject buttonObject = CreateUiObject(objectName, parent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.78f, 0.82f, 0.88f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        TMP_Text labelText = CreateText("Label", buttonObject.transform, 15f, TextAlignmentOptions.Center, new Color(0.06f, 0.06f, 0.07f, 1f));
        labelText.text = label;
        RectTransform labelRect = labelText.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        return button;
    }

    private static void AddLayoutElement(GameObject target, float preferredWidth, float preferredHeight, float flexibleWidth)
    {
        LayoutElement element = target.AddComponent<LayoutElement>();
        if (preferredWidth >= 0f)
            element.preferredWidth = preferredWidth;
        if (preferredHeight >= 0f)
            element.preferredHeight = preferredHeight;
        element.flexibleWidth = flexibleWidth;
    }

    private sealed class OfferRow
    {
        private readonly GameObject _root;
        private readonly TMP_Text _nameText;
        private readonly TMP_Text _levelText;
        private readonly TMP_Text _costText;

        public OfferRow(GameObject root, TMP_Text nameText, TMP_Text levelText, TMP_Text costText, Button purchaseButton)
        {
            _root = root;
            _nameText = nameText;
            _levelText = levelText;
            _costText = costText;
            PurchaseButton = purchaseButton;
        }

        public RestAreaShopOffer Offer { get; private set; }
        public Button PurchaseButton { get; }

        public void Bind(RestAreaShopOffer offer, int level, int totalValue, int cost, bool canBuy)
        {
            Offer = offer;

            if (_nameText != null)
                _nameText.text = offer.DisplayName;

            if (_levelText != null)
                _levelText.text = "Lv " + level.ToString() + "  Total +" + totalValue.ToString();

            if (_costText != null)
                _costText.text = "Cost " + cost.ToString();

            if (PurchaseButton != null)
                PurchaseButton.interactable = canBuy;

            SetActive(true);
        }

        public void SetActive(bool active)
        {
            if (_root != null)
                _root.SetActive(active);
        }
    }
}
