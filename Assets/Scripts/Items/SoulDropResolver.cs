using UnityEngine;

public static class SoulDropResolver
{
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
