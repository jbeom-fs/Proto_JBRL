using System.Collections.Generic;
using UnityEngine;

public static class EnemyDropRoller
{
    public static void Roll(IReadOnlyList<EnemyDropEntry> entries, EnemyInventory inventory, System.Random rng)
    {
        if (entries == null || inventory == null || rng == null)
            return;

        if (entries.Count == 0)
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            EnemyDropEntry entry = entries[i];
            if (string.IsNullOrWhiteSpace(entry.itemCode))
                continue;

            if (entry.chance < 1f && rng.NextDouble() >= entry.chance)
                continue;

            int minAmount = Mathf.Max(1, entry.minAmount);
            int maxAmount = Mathf.Max(minAmount, entry.maxAmount);
            int amount = rng.Next(minAmount, maxAmount + 1);
            inventory.AddDropItem(entry.itemCode, amount);
        }
    }
}
