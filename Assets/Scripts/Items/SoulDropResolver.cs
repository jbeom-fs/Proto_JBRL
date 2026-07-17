using System.Collections.Generic;
using UnityEngine;

public static class SoulDropResolver
{
    private static readonly List<ItemData> SoulCandidates = new List<ItemData>();

    public static (ItemData item, int amount) ResolveDrop(
        ItemData item,
        int amount,
        PlayerInventory inventory,
        ItemDatabase database,
        System.Random salvageRng)
    {
        if (item == null || item.ItemType != ItemType.Soul)
            return (item, amount);

        if (inventory == null || !inventory.OwnsSoulForm(item.SoulFormId))
            return (item, amount);

        if (database != null)
        {
            SoulCandidates.Clear();
            database.GetSoulItems(SoulCandidates);

            int writeIndex = 0;
            for (int i = 0; i < SoulCandidates.Count; i++)
            {
                ItemData candidate = SoulCandidates[i];
                if (inventory.OwnsSoulForm(candidate.SoulFormId))
                    continue;

                SoulCandidates[writeIndex++] = candidate;
            }

            if (writeIndex > 0)
            {
                int index = salvageRng != null ? salvageRng.Next(writeIndex) : 0;
                return (SoulCandidates[index], amount);
            }
        }

        if (string.IsNullOrWhiteSpace(item.SalvageItemCode) ||
            database == null ||
            !database.TryGetItem(item.SalvageItemCode, out ItemData fragment) ||
            fragment == null)
            return (item, amount);

        int min = Mathf.Max(1, item.SalvageMinAmount);
        int max = Mathf.Max(min, item.SalvageMaxAmount);
        int count = salvageRng != null ? salvageRng.Next(min, max + 1) : min;
        return (fragment, count);
    }
}
