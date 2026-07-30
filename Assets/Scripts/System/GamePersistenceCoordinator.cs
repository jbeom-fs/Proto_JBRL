using System.Collections.Generic;
using UnityEngine;

public sealed class GamePersistenceCoordinator : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerSoulEnhancements soulEnhancements;
    [SerializeField] private PlayerPassiveUnlocks passiveUnlocks;

    private readonly List<ItemData> _removeItemBuffer = new List<ItemData>(16);
    private readonly List<SoulEnhancementLevelSnapshot> _enhancementBuffer = new List<SoulEnhancementLevelSnapshot>(32);
    private readonly List<string> _passiveUnlockBuffer = new List<string>(16);
    private readonly List<ItemStackSave> _unresolvedItemEntries = new List<ItemStackSave>();
    private readonly List<string> _unresolvedPassiveUnlockIds = new List<string>();
    private readonly HashSet<string> _unknownItemWarnings = new HashSet<string>();
    private readonly HashSet<string> _unknownPassiveWarnings = new HashSet<string>();

    private SaveService _service;
    private bool _dirty;
    private bool _isApplying;
    private bool _dependencyWarningLogged;

    private void Awake()
    {
        _service = new SaveService();
    }

    private void OnEnable()
    {
        if (inventory != null)
            inventory.OnInventoryChanged += HandlePersistentStateChanged;

        if (soulEnhancements != null)
            soulEnhancements.OnChanged += HandlePersistentStateChanged;

        if (passiveUnlocks != null)
            passiveUnlocks.OnChanged += HandlePersistentStateChanged;
    }

    private void Start()
    {
        EnsureService();

        if (_service.TryLoad(out SaveData data))
            ApplyFromSave(data);

        GrantStartingSwordSoulIfNeeded();
    }

    private void LateUpdate()
    {
        if (!_dirty)
            return;

        SaveAll();
    }

    private void OnDisable()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= HandlePersistentStateChanged;

        if (soulEnhancements != null)
            soulEnhancements.OnChanged -= HandlePersistentStateChanged;

        if (passiveUnlocks != null)
            passiveUnlocks.OnChanged -= HandlePersistentStateChanged;
    }

    private void OnApplicationQuit()
    {
        SaveAll();
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause)
            SaveAll();
    }

    public SaveData CaptureToSave()
    {
        SaveData data = new SaveData();
        HashSet<string> capturedItemCodes = new HashSet<string>();
        HashSet<string> capturedPassiveIds = new HashSet<string>();

        if (inventory != null)
        {
            IReadOnlyList<InventoryItemStack> source = inventory.Items;
            for (int i = 0; i < source.Count; i++)
            {
                InventoryItemStack stack = source[i];
                if (stack == null || stack.Count <= 0)
                    continue;

                ItemData item = stack.Item;
                if (item == null || !IsPersistentItemType(item.ItemType) || string.IsNullOrWhiteSpace(item.ItemCode))
                    continue;

                data.items.Add(new ItemStackSave
                {
                    itemCode = item.ItemCode,
                    count = stack.Count
                });
                capturedItemCodes.Add(item.ItemCode);
            }
        }

        if (soulEnhancements != null)
        {
            _enhancementBuffer.Clear();
            soulEnhancements.GetLevels(_enhancementBuffer);

            for (int i = 0; i < _enhancementBuffer.Count; i++)
            {
                SoulEnhancementLevelSnapshot level = _enhancementBuffer[i];
                if (level.Level <= 0)
                    continue;

                data.enhancements.Add(new EnhancementSave
                {
                    form = (int)level.Form,
                    stat = (int)level.Stat,
                    level = level.Level
                });
            }

            _enhancementBuffer.Clear();
        }

        if (passiveUnlocks != null)
        {
            _passiveUnlockBuffer.Clear();
            passiveUnlocks.GetUnlockedIds(_passiveUnlockBuffer);
            _passiveUnlockBuffer.Sort(System.StringComparer.Ordinal);

            for (int i = 0; i < _passiveUnlockBuffer.Count; i++)
            {
                string unlockId = _passiveUnlockBuffer[i];
                if (string.IsNullOrWhiteSpace(unlockId) || !capturedPassiveIds.Add(unlockId))
                    continue;

                data.unlockedPassiveIds.Add(unlockId);
            }

            _passiveUnlockBuffer.Clear();
        }

        for (int i = 0; i < _unresolvedItemEntries.Count; i++)
        {
            ItemStackSave unresolved = _unresolvedItemEntries[i];
            if (capturedItemCodes.Contains(unresolved.itemCode))
                continue;

            data.items.Add(unresolved);
        }

        for (int i = 0; i < _unresolvedPassiveUnlockIds.Count; i++)
        {
            string unresolvedId = _unresolvedPassiveUnlockIds[i];
            if (string.IsNullOrWhiteSpace(unresolvedId) || !capturedPassiveIds.Add(unresolvedId))
                continue;

            data.unlockedPassiveIds.Add(unresolvedId);
        }

        return data;
    }

    public void ApplyFromSave(SaveData data)
    {
        _unresolvedItemEntries.Clear();
        _unresolvedPassiveUnlockIds.Clear();

        if (data == null)
            return;

        if (!HasDependencies())
            return;

        _isApplying = true;
        try
        {
            RemovePersistentInventoryItems();
            ApplyInventoryItems(data.items);

            soulEnhancements.Clear();
            ApplyEnhancements(data.enhancements);

            if (passiveUnlocks != null)
            {
                passiveUnlocks.Clear();
                ApplyPassiveUnlocks(data.unlockedPassiveIds);
            }
        }
        finally
        {
            _isApplying = false;
            _dirty = false;
        }
    }

    public void SaveAll()
    {
        _dirty = false;

        if (!HasDependencies())
            return;

        EnsureService();
        _service.Save(CaptureToSave());
    }

    private void HandlePersistentStateChanged()
    {
        if (_isApplying)
            return;

        _dirty = true;
    }

    private void GrantStartingSwordSoulIfNeeded()
    {
        if (inventory == null || inventory.OwnsSoulForm(PlayerFormId.Sword))
            return;

        if (!inventory.TryGetSoulByForm(PlayerFormId.Sword, out ItemData swordSoul) || swordSoul == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[GamePersistenceCoordinator] Sword Soul is missing from ItemDatabase.", this);
#endif
            return;
        }

        inventory.AddItem(swordSoul, 1);
    }

    private void RemovePersistentInventoryItems()
    {
        _removeItemBuffer.Clear();

        IReadOnlyList<InventoryItemStack> source = inventory.Items;
        for (int i = 0; i < source.Count; i++)
        {
            InventoryItemStack stack = source[i];
            if (stack == null)
                continue;

            ItemData item = stack.Item;
            if (item == null || !IsPersistentItemType(item.ItemType))
                continue;

            if (!_removeItemBuffer.Contains(item))
                _removeItemBuffer.Add(item);
        }

        for (int i = 0; i < _removeItemBuffer.Count; i++)
            inventory.RemoveAll(_removeItemBuffer[i]);

        _removeItemBuffer.Clear();
    }

    private void ApplyInventoryItems(List<ItemStackSave> savedItems)
    {
        if (savedItems == null)
            return;

        for (int i = 0; i < savedItems.Count; i++)
        {
            ItemStackSave saved = savedItems[i];
            if (saved == null || saved.count <= 0 || string.IsNullOrWhiteSpace(saved.itemCode))
                continue;

            if (!inventory.TryGetDatabaseItem(saved.itemCode, out ItemData item) || item == null)
            {
                WarnUnknownItem(saved.itemCode);
                _unresolvedItemEntries.Add(saved);
                continue;
            }

            if (!IsPersistentItemType(item.ItemType))
            {
                _unresolvedItemEntries.Add(saved);
                continue;
            }

            inventory.AddItem(item, saved.count);
        }
    }

    private void ApplyEnhancements(List<EnhancementSave> savedEnhancements)
    {
        if (savedEnhancements == null)
            return;

        for (int i = 0; i < savedEnhancements.Count; i++)
        {
            EnhancementSave saved = savedEnhancements[i];
            if (saved == null || saved.level <= 0)
                continue;

            soulEnhancements.SetLevel((PlayerFormId)saved.form, (SoulStatType)saved.stat, saved.level);
        }
    }

    private void ApplyPassiveUnlocks(List<string> savedUnlockIds)
    {
        if (passiveUnlocks == null || savedUnlockIds == null)
            return;

        HashSet<string> appliedIds = new HashSet<string>();
        for (int i = 0; i < savedUnlockIds.Count; i++)
        {
            string unlockId = savedUnlockIds[i];
            if (string.IsNullOrWhiteSpace(unlockId) || !appliedIds.Add(unlockId))
                continue;

            if (!passiveUnlocks.TryGetPassive(unlockId, out PassiveEngravingData passive))
            {
                WarnUnknownPassive(unlockId);
                _unresolvedPassiveUnlockIds.Add(unlockId);
                continue;
            }

            if (!passiveUnlocks.IsUnlocked(passive))
                passiveUnlocks.Unlock(passive);
        }
    }

    private bool HasDependencies()
    {
        if (inventory != null && soulEnhancements != null)
            return true;

        if (!_dependencyWarningLogged)
        {
            _dependencyWarningLogged = true;
            Debug.LogWarning("[GamePersistenceCoordinator] Missing serialized dependencies.", this);
        }

        return false;
    }

    private void EnsureService()
    {
        if (_service == null)
            _service = new SaveService();
    }

    private void WarnUnknownItem(string itemCode)
    {
        if (!_unknownItemWarnings.Add(itemCode))
            return;

        Debug.LogWarning("[GamePersistenceCoordinator] Unknown saved itemCode skipped: " + itemCode, this);
    }

    private void WarnUnknownPassive(string unlockId)
    {
        if (!_unknownPassiveWarnings.Add(unlockId))
            return;

        Debug.LogWarning(
            "[GamePersistenceCoordinator] Unresolved saved passive unlockId preserved " +
            "(not catalogued or duplicated): " + unlockId,
            this);
    }

    private static bool IsPersistentItemType(ItemType itemType)
    {
        return itemType == ItemType.Soul || itemType == ItemType.Material;
    }
}
