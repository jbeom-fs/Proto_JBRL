using System.Collections.Generic;
using UnityEngine;

public static class SoulCommonEnhancement
{
    public enum Result
    {
        Success,
        NotSharedStat,
        GrowthMissing,
        MaxLevel,
        AllocationMismatch,
        ShardResolveFailed,
        InsufficientShards
    }

    public readonly struct ShardAllocation
    {
        public ShardAllocation(PlayerFormId form, int amount)
        {
            Form = form;
            Amount = amount;
        }

        public readonly PlayerFormId Form;
        public readonly int Amount;
    }

    public static Result TryEnhance(
        SoulEnhancementTable table,
        PlayerSoulEnhancements enhancements,
        PlayerInventory inventory,
        ItemDatabase itemDatabase,
        SoulStatType stat,
        IReadOnlyList<ShardAllocation> allocations)
    {
        if (!SoulStatTypeUtility.IsShared(stat))
            return Result.NotSharedStat;

        if (table == null || !table.TryGetGrowth(PlayerFormId.Normal, stat, out SoulStatGrowth growth))
            return Result.GrowthMissing;

        int currentLevel = enhancements.GetLevel(PlayerFormId.Normal, stat);
        if (currentLevel >= Mathf.Max(0, growth.maxLevel))
            return Result.MaxLevel;

        int cost = SoulEnhancementCost.GetMaterialCost(growth, currentLevel);
        int allocationCount = allocations?.Count ?? 0;
        var seenForms = new HashSet<PlayerFormId>();
        long allocatedTotal = 0;

        for (int i = 0; i < allocationCount; i++)
        {
            ShardAllocation allocation = allocations[i];
            if (allocation.Amount <= 0 || !seenForms.Add(allocation.Form))
                return Result.AllocationMismatch;

            allocatedTotal += allocation.Amount;
        }

        if (allocatedTotal != cost)
            return Result.AllocationMismatch;

        var resolvedShards = new List<ItemData>(allocationCount);
        for (int i = 0; i < allocationCount; i++)
        {
            ShardAllocation allocation = allocations[i];
            if (!SoulShardResolver.TryResolveShardItem(
                    itemDatabase,
                    inventory,
                    allocation.Form,
                    out ItemData shardItem))
            {
                return Result.ShardResolveFailed;
            }

            if (inventory == null || !inventory.HasItem(shardItem, allocation.Amount))
                return Result.InsufficientShards;

            resolvedShards.Add(shardItem);
        }

        for (int i = 0; i < allocationCount; i++)
        {
            if (!inventory.RemoveItem(resolvedShards[i], allocations[i].Amount))
            {
                Debug.LogError(
                    "[SoulCommonEnhancement] Validated shard removal failed: " +
                    allocations[i].Form + " x " + allocations[i].Amount + ".");
            }
        }

        enhancements.AddLevel(PlayerFormId.Normal, stat, 1);
        return Result.Success;
    }
}
