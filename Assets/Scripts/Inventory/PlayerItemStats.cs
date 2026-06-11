using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerItemStats
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private readonly HashSet<ItemEffectType> _warnedUnsupportedPassiveTypes = new();
#endif

    public int MaxHpBonus { get; private set; }
    public int AttackBonus { get; private set; }
    public int DefenseBonus { get; private set; }
    public int MoveSpeedBonusPercent { get; private set; }

    public void Recalculate(IReadOnlyList<InventoryItemStack> items)
    {
        MaxHpBonus = 0;
        AttackBonus = 0;
        DefenseBonus = 0;
        MoveSpeedBonusPercent = 0;

        if (items == null)
            return;

        for (int i = 0; i < items.Count; i++)
        {
            InventoryItemStack stack = items[i];
            if (stack == null || stack.Count <= 0)
                continue;

            ItemData item = stack.Item;
            if (item == null || item.ItemType != ItemType.Relic)
                continue;

            AddPassiveEffects(item, stack.Count);
        }
    }

    private void AddPassiveEffects(ItemData item, int count)
    {
        IReadOnlyList<ItemEffect> effects = item.PassiveEffects;
        if (effects == null || effects.Count == 0)
            return;

        for (int i = 0; i < effects.Count; i++)
        {
            ItemEffect effect = effects[i];
            int totalValue = effect.value * count;
            switch (effect.type)
            {
                case ItemEffectType.None:
                    break;
                case ItemEffectType.MaxHpBonus:
                    MaxHpBonus += totalValue;
                    break;
                case ItemEffectType.AttackBonus:
                    AttackBonus += totalValue;
                    break;
                case ItemEffectType.DefenseBonus:
                    DefenseBonus += totalValue;
                    break;
                case ItemEffectType.MoveSpeedBonus:
                    MoveSpeedBonusPercent += totalValue;
                    break;
                default:
                    WarnUnsupportedPassiveEffect(effect.type, item);
                    break;
            }
        }
    }

    private void WarnUnsupportedPassiveEffect(ItemEffectType type, ItemData item)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_warnedUnsupportedPassiveTypes.Add(type))
            return;

        Debug.LogWarning(
            "[PlayerItemStats] Unsupported passive effect type ignored: " +
            type + " on item " + (item != null ? item.ItemCode : "<null>") + ".");
#endif
    }
}
