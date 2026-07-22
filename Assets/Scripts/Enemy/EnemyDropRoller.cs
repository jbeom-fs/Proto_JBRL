using System.Collections.Generic;
using UnityEngine;

public static class EnemyDropRoller
{
    private struct QueryDropBufferEntry
    {
        public ItemData Item;
        public int Count;
    }

    private static readonly List<QueryDropBufferEntry> s_QueryDropBuffer =
        new List<QueryDropBufferEntry>(16);

    public static void Roll(
        EnemyDropDatabase database,
        EnemyDropGroup group,
        EnemyInventory inventory,
        System.Random rng)
    {
        if (group == null || inventory == null || rng == null)
            return;

        RollIndependent(group.drops, inventory, rng);
        RollChoiceGroups(group.choiceGroups, inventory, rng);
        RollQueries(database, group.queries, inventory, rng);
    }

    private static void RollIndependent(IReadOnlyList<EnemyDropEntry> entries, EnemyInventory inventory, System.Random rng)
    {
        if (entries == null || entries.Count == 0)
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

    private static void RollChoiceGroups(IReadOnlyList<EnemyDropChoiceGroup> choiceGroups, EnemyInventory inventory, System.Random rng)
    {
        if (choiceGroups == null || choiceGroups.Count == 0)
            return;

        for (int i = 0; i < choiceGroups.Count; i++)
        {
            EnemyDropChoiceGroup choiceGroup = choiceGroups[i];
            if (choiceGroup == null || choiceGroup.choices == null || choiceGroup.choices.Count == 0)
                continue;

            if (choiceGroup.chance < 1f && rng.NextDouble() >= choiceGroup.chance)
                continue;

            if (!TryChoose(choiceGroup.choices, rng, out EnemyDropChoice choice))
                continue;

            int minAmount = Mathf.Max(1, choice.minAmount);
            int maxAmount = Mathf.Max(minAmount, choice.maxAmount);
            int amount = rng.Next(minAmount, maxAmount + 1);
            inventory.AddDropItem(choice.itemCode, amount);
        }
    }

    private static bool TryChoose(IReadOnlyList<EnemyDropChoice> choices, System.Random rng, out EnemyDropChoice selected)
    {
        selected = default;
        float totalWeight = 0f;
        for (int i = 0; i < choices.Count; i++)
        {
            EnemyDropChoice choice = choices[i];
            if (string.IsNullOrWhiteSpace(choice.itemCode) || choice.weight <= 0f)
                continue;

            totalWeight += choice.weight;
        }

        if (totalWeight <= 0f)
            return false;

        double roll = rng.NextDouble() * totalWeight;
        float accumulated = 0f;
        bool hasFallback = false;
        EnemyDropChoice fallback = default;
        for (int i = 0; i < choices.Count; i++)
        {
            EnemyDropChoice choice = choices[i];
            if (string.IsNullOrWhiteSpace(choice.itemCode) || choice.weight <= 0f)
                continue;

            accumulated += choice.weight;
            fallback = choice;
            hasFallback = true;
            if (roll < accumulated)
            {
                selected = choice;
                return true;
            }
        }

        selected = fallback;
        return hasFallback;
    }

    private static void RollQueries(
        EnemyDropDatabase database,
        IReadOnlyList<EnemyDropQuery> queries,
        EnemyInventory inventory,
        System.Random rng)
    {
        if (database == null ||
            database.ItemDatabase == null ||
            queries == null ||
            queries.Count == 0)
        {
            return;
        }

        s_QueryDropBuffer.Clear();
        PlayerFormId currentForm = DropQueryResolver.ResolveCurrentForm();
        for (int i = 0; i < queries.Count; i++)
        {
            EnemyDropQuery query = queries[i];
            if (query.chance < 1f && rng.NextDouble() >= query.chance)
                continue;

            // 횟수 가중치가 없거나 전부 0이면 1회다. tier 총합 0의 무드랍 규칙과 의도적으로 다르다.
            int rollCount = 1;
            if (DropQueryResolver.TryChooseWeightedIndex(
                    query.rollCountWeights,
                    rng,
                    out int rollCountIndex))
            {
                rollCount = rollCountIndex + 1;
            }

            for (int rollIndex = 0; rollIndex < rollCount; rollIndex++)
            {
                if (!DropQueryResolver.TryResolve(
                        database.ItemDatabase,
                        query,
                        currentForm,
                        rng,
                        out DropQueryResult result))
                {
                    continue;
                }

                AddQueryDropResult(result.Item);
            }
        }

        for (int i = 0; i < s_QueryDropBuffer.Count; i++)
        {
            QueryDropBufferEntry entry = s_QueryDropBuffer[i];
            if (entry.Item == null)
                continue;

            if (entry.Item.Stackable)
            {
                inventory.AddDropItem(entry.Item.ItemCode, entry.Count);
                continue;
            }

            for (int count = 0; count < entry.Count; count++)
                inventory.AddDropItem(entry.Item.ItemCode, 1);
        }

        s_QueryDropBuffer.Clear();
    }

    private static void AddQueryDropResult(ItemData item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.ItemCode))
            return;

        for (int i = 0; i < s_QueryDropBuffer.Count; i++)
        {
            QueryDropBufferEntry entry = s_QueryDropBuffer[i];
            if (!string.Equals(entry.Item.ItemCode, item.ItemCode, System.StringComparison.Ordinal))
                continue;

            entry.Count++;
            s_QueryDropBuffer[i] = entry;
            return;
        }

        s_QueryDropBuffer.Add(new QueryDropBufferEntry
        {
            Item = item,
            Count = 1
        });
    }
}
