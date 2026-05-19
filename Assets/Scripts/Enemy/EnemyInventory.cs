using System.Collections.Generic;
using UnityEngine;

public readonly struct EnemyDropItem
{
    public EnemyDropItem(string itemCode, int amount)
    {
        ItemCode = itemCode;
        Amount = amount;
    }

    public string ItemCode { get; }
    public int Amount { get; }
}

public sealed class EnemyInventory : MonoBehaviour
{
    private readonly List<EnemyDropItem> _dropItems = new List<EnemyDropItem>(2);

    public void Clear()
    {
        _dropItems.Clear();
    }

    public void AddDropItem(string itemCode, int amount = 1)
    {
        if (string.IsNullOrWhiteSpace(itemCode) || amount <= 0)
            return;

        _dropItems.Add(new EnemyDropItem(itemCode, amount));
    }

    public IReadOnlyList<EnemyDropItem> GetDropItems()
        => _dropItems;
}
