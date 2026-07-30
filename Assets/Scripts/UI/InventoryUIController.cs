using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class InventoryUIController : MonoBehaviour
{
    private static InventoryUIController s_Active;

    [SerializeField] private GameObject root;
    [SerializeField] private Transform slotContent;
    [SerializeField] private InventorySlotUI slotTemplate;
    [SerializeField] private TMP_Text emptyText;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerInputReader inputReader;
    [SerializeField] private Button[] tabButtons;
    [SerializeField] private Color tabSelectedColor = Color.white;
    [SerializeField] private Color tabNormalColor = new Color(0.65f, 0.65f, 0.65f, 1f);

    private readonly List<InventorySlotUI> _slots = new List<InventorySlotUI>(16);
    private readonly List<InventoryItemStack> _filteredBuffer = new List<InventoryItemStack>(16);
    private readonly List<EngravingDisplayInfo> _engravingDisplayBuffer =
        new List<EngravingDisplayInfo>(16);
    private EngravingLoadout _subscribedLoadout;
    private const int TabAll = 0;
    private const int TabConsumable = 1;
    private const int TabRelic = 2;
    private const int TabMaterial = 3;
    private const int TabOther = 4;
    private const int TabEngraving = 5;
    private const int TabCount = 6;
    private int _activeTabIndex;
    private bool _warnedMissingInventory;
    private bool _warnedMissingInputReader;
    private bool _warnedInvalidTabs;
    private bool _warnedMissingEngravingLoadout;
    private bool _warnedMissingPlayerCombat;

    public static InventoryUIController Active => s_Active;
    public static bool IsOpen => s_Active != null && s_Active.IsInventoryOpen;

    public bool IsInventoryOpen => root != null && root.activeSelf;

    private void Awake()
    {
        if (s_Active != null && s_Active != this)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[InventoryUIController] Duplicate instance detected. Latest instance is now active.", this);
#endif
        }

        s_Active = this;
        RegisterTabButtons();
        RefreshTabVisuals();
        if (root != null)
            root.SetActive(false);
    }

    private void OnEnable()
    {
        if (playerInventory != null)
            playerInventory.OnInventoryChanged += Refresh;
    }

    private void OnDisable()
    {
        ItemTooltipUI.HideActive();
        UnsubscribeLoadout();

        if (playerInventory != null)
            playerInventory.OnInventoryChanged -= Refresh;

        if (s_Active == this)
            s_Active = null;
    }

    private void Update()
    {
        if (DeveloperConsoleUI.IsOpen)
        {
            if (IsInventoryOpen)
                Close();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (IsInventoryOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        if (inputReader == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_warnedMissingInputReader)
            {
                Debug.LogWarning("[InventoryUIController] inputReader is not assigned.", this);
                _warnedMissingInputReader = true;
            }
#endif
            return;
        }

        if (inputReader.InventoryPressedThisFrame)
            Toggle();
    }

    public void Toggle()
    {
        if (IsInventoryOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (root == null)
            return;

        SubscribeLoadout();
        root.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        ItemTooltipUI.HideActive();
        UnsubscribeLoadout();

        if (root != null)
            root.SetActive(false);
    }

    private void SubscribeLoadout()
    {
        EngravingLoadout loadout = EngravingLoadout.Active;
        if (loadout == null || ReferenceEquals(_subscribedLoadout, loadout))
            return;

        UnsubscribeLoadout();
        loadout.OnPoolChanged += Refresh;
        _subscribedLoadout = loadout;
    }

    private void UnsubscribeLoadout()
    {
        if (_subscribedLoadout == null)
            return;

        _subscribedLoadout.OnPoolChanged -= Refresh;
        _subscribedLoadout = null;
    }

    private void Refresh()
    {
        ItemTooltipUI.HideActive();

        if (_activeTabIndex == TabEngraving)
        {
            RefreshEngravingPool();
            return;
        }

        if (playerInventory == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_warnedMissingInventory)
            {
                Debug.LogWarning("[InventoryUIController] playerInventory is not assigned.", this);
                _warnedMissingInventory = true;
            }
#endif
            return;
        }

        IReadOnlyList<InventoryItemStack> items = GetDisplayItems(playerInventory.Items);
        if (_activeTabIndex == TabAll && HasUsableTabs())
            CollectEngravingDisplays();
        else
            _engravingDisplayBuffer.Clear();

        int displayCount = items.Count + _engravingDisplayBuffer.Count;
        EnsureSlotCount(displayCount);

        for (int i = 0; i < _slots.Count; i++)
        {
            if (i < items.Count)
                _slots[i].Bind(items[i].Item, items[i].Count, this);
            else if (i < displayCount)
                _slots[i].Bind(_engravingDisplayBuffer[i - items.Count]);
            else
                _slots[i].gameObject.SetActive(false);
        }

        if (emptyText != null)
            emptyText.gameObject.SetActive(displayCount == 0);
    }

    private void RefreshEngravingPool()
    {
        CollectEngravingDisplays();

        EnsureSlotCount(_engravingDisplayBuffer.Count);
        for (int i = 0; i < _slots.Count; i++)
        {
            if (i < _engravingDisplayBuffer.Count)
                _slots[i].Bind(_engravingDisplayBuffer[i]);
            else
                _slots[i].gameObject.SetActive(false);
        }

        if (emptyText != null)
            emptyText.gameObject.SetActive(_engravingDisplayBuffer.Count == 0);
    }

    private void CollectEngravingDisplays()
    {
        _engravingDisplayBuffer.Clear();
        EngravingLoadout loadout = EngravingLoadout.Active;
        PlayerCombatController combat = PlayerCombatController.Active;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (loadout == null && !_warnedMissingEngravingLoadout)
        {
            Debug.LogWarning("[InventoryUIController] EngravingLoadout.Active is unavailable.", this);
            _warnedMissingEngravingLoadout = true;
        }

        if (combat == null && !_warnedMissingPlayerCombat)
        {
            Debug.LogWarning("[InventoryUIController] PlayerCombatController.Active is unavailable.", this);
            _warnedMissingPlayerCombat = true;
        }
#endif

        if (loadout != null && combat != null)
        {
            PlayerFormId form = combat.CurrentFormId;
            int activeCount = loadout.PoolCount(form);
            for (int i = 0; i < activeCount; i++)
            {
                if (EngravingDisplayInfo.TryCreate(loadout.GetPoolAt(form, i), out EngravingDisplayInfo info))
                    _engravingDisplayBuffer.Add(info);
            }

            if (EngravingDisplayInfo.TryCreate(
                    loadout.GetPassive(form),
                    out EngravingDisplayInfo passiveInfo))
            {
                _engravingDisplayBuffer.Add(passiveInfo);
            }
        }
    }

    private IReadOnlyList<InventoryItemStack> GetDisplayItems(IReadOnlyList<InventoryItemStack> source)
    {
        if (source == null)
        {
            _filteredBuffer.Clear();
            return _filteredBuffer;
        }

        if (!HasUsableTabs())
            return source;

        _filteredBuffer.Clear();
        switch (_activeTabIndex)
        {
            case TabAll:
                AppendItemsForGroup(source, TabConsumable);
                AppendItemsForGroup(source, TabRelic);
                AppendItemsForGroup(source, TabMaterial);
                AppendItemsForGroup(source, TabOther);
                break;
            case TabConsumable:
            case TabRelic:
            case TabMaterial:
            case TabOther:
                AppendItemsForGroup(source, _activeTabIndex);
                break;
            default:
                AppendItemsForGroup(source, TabConsumable);
                AppendItemsForGroup(source, TabRelic);
                AppendItemsForGroup(source, TabMaterial);
                AppendItemsForGroup(source, TabOther);
                break;
        }

        return _filteredBuffer;
    }

    private void AppendItemsForGroup(IReadOnlyList<InventoryItemStack> source, int tabIndex)
    {
        for (int i = 0; i < source.Count; i++)
        {
            InventoryItemStack stack = source[i];
            ItemData item = stack != null ? stack.Item : null;
            if (item != null && IsItemInTab(item.ItemType, tabIndex))
                _filteredBuffer.Add(stack);
        }
    }

    private static bool IsItemInTab(ItemType itemType, int tabIndex)
    {
        switch (tabIndex)
        {
            case TabConsumable:
                return itemType == ItemType.Consumable;
            case TabRelic:
                return itemType == ItemType.Relic;
            case TabMaterial:
                return itemType == ItemType.Material;
            case TabOther:
                return itemType == ItemType.Key ||
                       itemType == ItemType.Currency;
            default:
                return true;
        }
    }

    private void EnsureSlotCount(int count)
    {
        if (slotTemplate == null || slotContent == null)
            return;

        while (_slots.Count < count)
        {
            InventorySlotUI slot = Instantiate(slotTemplate, slotContent);
            slot.gameObject.SetActive(false);
            _slots.Add(slot);
        }
    }

    private void RegisterTabButtons()
    {
        if (!HasUsableTabs())
            return;

        for (int i = 0; i < TabCount; i++)
        {
            int tabIndex = i;
            tabButtons[i].onClick.AddListener(() => SelectTab(tabIndex));
        }
    }

    private void SelectTab(int tabIndex)
    {
        if (!HasUsableTabs())
            return;

        if (tabIndex < 0 || tabIndex >= TabCount)
            return;

        if (_activeTabIndex == tabIndex)
            return;

        ItemTooltipUI.HideActive();
        _activeTabIndex = tabIndex;
        RefreshTabVisuals();
        Refresh();
    }

    private void RefreshTabVisuals()
    {
        if (!HasUsableTabs())
            return;

        for (int i = 0; i < TabCount; i++)
        {
            Image image = tabButtons[i].GetComponent<Image>();
            if (image != null)
                image.color = i == _activeTabIndex ? tabSelectedColor : tabNormalColor;
        }
    }

    private bool HasUsableTabs()
    {
        bool valid = tabButtons != null && tabButtons.Length >= TabCount;
        if (valid)
        {
            for (int i = 0; i < TabCount; i++)
            {
                if (tabButtons[i] == null)
                {
                    valid = false;
                    break;
                }
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!valid && !_warnedInvalidTabs)
        {
            Debug.LogWarning("[InventoryUIController] tabButtons must contain 6 buttons in order: All, Consumable, Relic, Material, Other, Engraving. Falling back to unfiltered inventory view.", this);
            _warnedInvalidTabs = true;
        }
#endif
        return valid;
    }

    public void HandleSlotClicked(ItemData item)
    {
        if (item == null || item.ItemType != ItemType.Consumable)
            return;

        if (item.UseEffects == null || item.UseEffects.Count == 0)
            return;

        if (playerInventory == null || !playerInventory.HasItem(item, 1))
            return;

        PlayerCombatController combat = PlayerCombatController.Active;
        if (combat == null || !combat.IsAlive)
            return;

        if (!ItemEffectApplier.ApplyUseEffects(item, combat))
            return;

        playerInventory.RemoveItem(item, 1);
    }
}
