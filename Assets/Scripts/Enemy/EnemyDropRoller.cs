using UnityEngine;

public static class EnemyDropRoller
{
    public static void Roll(EnemyData data, EnemyInventory inventory, System.Random rng)
    {
        if (data == null || inventory == null || rng == null)
            return;

        if (data.drops == null || data.drops.Count == 0)
            return;

        for (int i = 0; i < data.drops.Count; i++)
        {
            EnemyDropEntry entry = data.drops[i];
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
