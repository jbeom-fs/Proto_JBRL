using System.Collections.Generic;
using UnityEngine;

public static class EnemyDropRoller
{
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

        PlayerFormId currentForm = DropQueryResolver.ResolveCurrentForm();
        for (int i = 0; i < queries.Count; i++)
        {
            EnemyDropQuery query = queries[i];
            if (query.chance < 1f && rng.NextDouble() >= query.chance)
                continue;

            if (!DropQueryResolver.TryResolve(
                    database.ItemDatabase,
                    query,
                    currentForm,
                    rng,
                    out DropQueryResult result))
            {
                continue;
            }

            ItemData item = result.Item;
            if (item == null)
                continue;

            int amount;
            if (item.ItemType == ItemType.Engraving ||
                item.ItemType == ItemType.PassiveEngraving)
            {
                amount = 1;
            }
            else
            {
                int minAmount = Mathf.Max(1, query.minAmount);
                int maxAmount = Mathf.Max(minAmount, query.maxAmount);
                amount = rng.Next(minAmount, maxAmount + 1);
            }

            inventory.AddDropItem(item.ItemCode, amount);
        }
    }
}
