using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerInventory : MonoBehaviour
{
    [SerializeField] private ItemDatabase itemDatabase;
    [SerializeField] private List<InventoryItemStack> items = new List<InventoryItemStack>();

    public event Action OnInventoryChanged;

    public IReadOnlyList<InventoryItemStack> Items => items;

    public bool TryGetDatabaseItem(string itemCode, out ItemData item)
    {
        item = null;

        if (itemDatabase == null || string.IsNullOrWhiteSpace(itemCode))
            return false;

        return itemDatabase.TryGetItem(itemCode, out item);
    }

    public bool TryGetSoulByForm(PlayerFormId formId, out ItemData soul)
    {
        soul = null;

        if (itemDatabase == null)
            return false;

        return itemDatabase.TryGetSoulByForm(formId, out soul);
    }

    public void GetDatabaseItemCodes(List<string> output)
    {
        if (itemDatabase == null)
            return;

        itemDatabase.GetItemCodes(output);
    }

    public bool AddItem(ItemData item, int amount)
    {
        if (item == null || amount <= 0)
            return false;

        if (!item.Stackable)
        {
            for (int i = 0; i < amount; i++)
                items.Add(new InventoryItemStack(item, 1));

            RaiseChanged();
            return true;
        }

        int remaining = amount;
        int maxStack = Mathf.Max(1, item.MaxStack);
        for (int i = 0; i < items.Count && remaining > 0; i++)
        {
            InventoryItemStack stack = items[i];
            if (stack.Item != item || stack.Count >= maxStack)
                continue;

            int addAmount = Mathf.Min(remaining, maxStack - stack.Count);
            stack.Add(addAmount);
            remaining -= addAmount;
        }

        while (remaining > 0)
        {
            int addAmount = Mathf.Min(remaining, maxStack);
            items.Add(new InventoryItemStack(item, addAmount));
            remaining -= addAmount;
        }

        RaiseChanged();
        return true;
    }

    public bool RemoveItem(ItemData item, int amount)
    {
        if (item == null || amount <= 0)
            return false;

        if (!HasItem(item, amount))
            return false;

        int remaining = amount;
        for (int i = items.Count - 1; i >= 0 && remaining > 0; i--)
        {
            InventoryItemStack stack = items[i];
            if (stack.Item != item)
                continue;

            int removeAmount = Mathf.Min(remaining, stack.Count);
            stack.Remove(removeAmount);
            remaining -= removeAmount;

            if (stack.Count <= 0)
                items.RemoveAt(i);
        }

        RaiseChanged();
        return true;
    }

    public bool RemoveAll(ItemData item)
    {
        if (item == null)
            return false;

        bool removed = false;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].Item != item)
                continue;

            items.RemoveAt(i);
            removed = true;
        }

        if (removed)
            RaiseChanged();

        return removed;
    }

    public bool RemoveAllByCode(string itemCode)
    {
        if (!TryGetDatabaseItem(itemCode, out ItemData item))
            return false;

        return RemoveAll(item);
    }

    public bool HasItem(ItemData item, int amount)
    {
        if (item == null || amount <= 0)
            return false;

        return GetItemCount(item) >= amount;
    }

    public int GetItemCount(ItemData item)
    {
        if (item == null)
            return 0;

        int total = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Item == item)
                total += items[i].Count;
        }

        return total;
    }

    public bool OwnsSoulForm(PlayerFormId formId)
    {
        for (int i = 0; i < items.Count; i++)
        {
            InventoryItemStack stack = items[i];
            if (stack.Item != null &&
                stack.Item.ItemType == ItemType.Soul &&
                stack.Item.SoulFormId == formId &&
                stack.Count > 0)
                return true;
        }

        return false;
    }

    public void RemoveItemsOnFloorTransition()
    {
        bool changed = false;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].Item != null && items[i].Item.RemoveOnFloorTransition)
            {
                items.RemoveAt(i);
                changed = true;
            }
        }
        if (changed)
            RaiseChanged();
    }

    public void RemoveItemsOnDungeonExit()
    {
        bool changed = false;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].Item != null && items[i].Item.RemoveOnDungeonExit)
            {
                items.RemoveAt(i);
                changed = true;
            }
        }
        if (changed)
            RaiseChanged();
    }

    public void Clear()
    {
        if (items.Count == 0)
            return;

        items.Clear();
        RaiseChanged();
    }

    public void NotifyExternalChange()
    {
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        OnInventoryChanged?.Invoke();
    }
}
