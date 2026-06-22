using System.Collections.Generic;
using UnityEngine;

public static class EnemyDropRoller
{
    public static void Roll(EnemyDropGroup group, EnemyInventory inventory, System.Random rng)
    {
        if (group == null || inventory == null || rng == null)
            return;

        RollIndependent(group.drops, inventory, rng);
        RollChoiceGroups(group.choiceGroups, inventory, rng);
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
}
