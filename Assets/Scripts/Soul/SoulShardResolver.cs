public static class SoulShardResolver
{
    // Temporarily duplicates SoulAltarUIController resolution until S3 replaces its private helpers.
    public static bool TryResolveShardItem(
        ItemDatabase db,
        PlayerInventory inventory,
        PlayerFormId form,
        out ItemData shardItem)
    {
        shardItem = null;

        if (!TryResolveSoulItem(db, inventory, form, out ItemData soulItem))
            return false;

        if (string.IsNullOrWhiteSpace(soulItem.SalvageItemCode))
            return false;

        if (inventory != null && inventory.TryGetDatabaseItem(soulItem.SalvageItemCode, out shardItem))
            return true;

        return db != null && db.TryGetItem(soulItem.SalvageItemCode, out shardItem);
    }

    private static bool TryResolveSoulItem(
        ItemDatabase db,
        PlayerInventory inventory,
        PlayerFormId form,
        out ItemData soulItem)
    {
        soulItem = null;

        if (db != null && db.TryGetSoulByForm(form, out soulItem))
            return true;

        return inventory != null && inventory.TryGetSoulByForm(form, out soulItem);
    }
}
