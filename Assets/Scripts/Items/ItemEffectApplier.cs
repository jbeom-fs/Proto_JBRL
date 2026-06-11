using System.Collections.Generic;
using UnityEngine;

public static class ItemEffectApplier
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static readonly HashSet<ItemEffectType> s_WarnedUnsupportedUseTypes = new();
#endif

    public static bool ApplyUseEffects(ItemData item, PlayerCombatController combat)
    {
        if (item == null || combat == null || !combat.IsAlive)
            return false;

        IReadOnlyList<ItemEffect> effects = item.UseEffects;
        if (effects == null || effects.Count == 0)
            return false;

        bool applied = false;
        for (int i = 0; i < effects.Count; i++)
        {
            ItemEffect effect = effects[i];
            switch (effect.type)
            {
                case ItemEffectType.None:
                    break;
                case ItemEffectType.HealHp:
                    if (effect.value > 0)
                    {
                        combat.RestoreHp(effect.value);
                        applied = true;
                    }
                    break;
                default:
                    WarnUnsupportedUseEffect(effect.type, item);
                    break;
            }
        }

        return applied;
    }

    private static void WarnUnsupportedUseEffect(ItemEffectType type, ItemData item)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!s_WarnedUnsupportedUseTypes.Add(type))
            return;

        Debug.LogWarning(
            "[ItemEffectApplier] Unsupported use effect type ignored: " +
            type + " on item " + (item != null ? item.ItemCode : "<null>") + ".");
#endif
    }
}
