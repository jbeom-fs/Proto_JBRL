using System;
using UnityEngine;

[Serializable]
public sealed class InventoryItemStack
{
    [SerializeField] private ItemData item;
    [SerializeField] private int count;

    public InventoryItemStack(ItemData item, int count)
    {
        this.item = item;
        this.count = count;
    }

    public ItemData Item => item;
    public int Count => count;

    public void Add(int amount)
    {
        count += amount;
    }

    public void Remove(int amount)
    {
        count -= amount;
    }
}
