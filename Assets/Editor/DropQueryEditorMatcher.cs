using System.Collections.Generic;

internal static class DropQueryEditorMatcher
{
    private const int TierCount = 3;

    public static bool Matches(
        ItemData item,
        EnemyDropQuery query,
        out bool currentFormDependent)
    {
        currentFormDependent = false;
        if (item == null || item.ItemType != query.itemType)
            return false;

        int tier = DropQueryResolver.GetTier(item);
        if ((uint)tier >= TierCount || !IsUsableWeight(GetTierWeight(query, tier)))
            return false;

        if (!IsEngravingLike(item.ItemType))
            return true;

        if (!TryGetOwningForm(item, out PlayerFormId owningForm))
            return false;

        if (query.formScope == DropFormScope.Any)
            return true;

        if (query.formScope == DropFormScope.Specific)
            return owningForm == query.specificForm;

        currentFormDependent = true;
        return true;
    }

    public static bool HasAnyMatch(IReadOnlyList<ItemData> items, EnemyDropQuery query)
    {
        if (items == null)
            return false;

        for (int i = 0; i < items.Count; i++)
        {
            if (Matches(items[i], query, out _))
                return true;
        }

        return false;
    }

    public static bool HasAnyTier(EnemyDropQuery query)
    {
        return IsUsableWeight(query.tierWeight0) ||
               IsUsableWeight(query.tierWeight1) ||
               IsUsableWeight(query.tierWeight2);
    }

    private static float GetTierWeight(EnemyDropQuery query, int tier)
    {
        switch (tier)
        {
            case 0:
                return query.tierWeight0;
            case 1:
                return query.tierWeight1;
            case 2:
                return query.tierWeight2;
            default:
                return 0f;
        }
    }

    private static bool IsUsableWeight(float weight)
    {
        return weight > 0f && !float.IsNaN(weight) && !float.IsInfinity(weight);
    }

    private static bool IsEngravingLike(ItemType itemType)
    {
        return itemType == ItemType.Engraving || itemType == ItemType.PassiveEngraving;
    }

    private static bool TryGetOwningForm(ItemData item, out PlayerFormId form)
    {
        if (item.ItemType == ItemType.Engraving && item.Engraving != null)
        {
            form = item.Engraving.owningForm;
            return true;
        }

        if (item.ItemType == ItemType.PassiveEngraving && item.PassiveEngraving != null)
        {
            form = item.PassiveEngraving.owningForm;
            return true;
        }

        form = default;
        return false;
    }
}
