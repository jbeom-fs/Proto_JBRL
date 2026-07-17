using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ItemData
{
    [SerializeField] private string itemCode;
    [SerializeField] private string displayName;
    [SerializeField] private Sprite icon;
    [TextArea]
    [SerializeField] private string description;
    [SerializeField] private ItemType itemType;
    [SerializeField] private bool stackable;
    [Min(1)]
    [SerializeField] private int maxStack = 1;
    [SerializeField] private bool removeOnFloorTransition;
    [SerializeField] private bool removeOnDungeonExit;
    [SerializeField] private ItemEffect[] useEffects = Array.Empty<ItemEffect>();
    [SerializeField] private ItemEffect[] passiveEffects = Array.Empty<ItemEffect>();
    [SerializeField] private RelicBehavior[] behaviorEffects = Array.Empty<RelicBehavior>();
    [SerializeField] private PlayerFormId soulFormId;
    [SerializeField] private EngravingData engraving;
    [SerializeField] private string salvageItemCode;
    [SerializeField] private int salvageMinAmount = 1;
    [SerializeField] private int salvageMaxAmount = 1;
    [NonSerialized] private bool _isRuntimeClone;

    public string ItemCode => itemCode;
    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public string Description => description;
    public ItemType ItemType => itemType;
    public bool Stackable => stackable;
    public int MaxStack => maxStack;
    public bool RemoveOnFloorTransition => removeOnFloorTransition;
    public bool RemoveOnDungeonExit => removeOnDungeonExit;
    public IReadOnlyList<ItemEffect> UseEffects => useEffects ?? Array.Empty<ItemEffect>();
    public IReadOnlyList<ItemEffect> PassiveEffects => passiveEffects ?? Array.Empty<ItemEffect>();
    public IReadOnlyList<RelicBehavior> BehaviorEffects => behaviorEffects ?? Array.Empty<RelicBehavior>();
    public PlayerFormId SoulFormId => soulFormId;
    public EngravingData Engraving => engraving;
    public string SalvageItemCode => salvageItemCode;
    public int SalvageMinAmount => salvageMinAmount;
    public int SalvageMaxAmount => salvageMaxAmount;

    public ItemData CreateRuntimeClone()
    {
        return new ItemData
        {
            itemCode = itemCode,
            displayName = displayName,
            icon = icon,
            description = description,
            itemType = itemType,
            stackable = stackable,
            maxStack = maxStack,
            removeOnFloorTransition = removeOnFloorTransition,
            removeOnDungeonExit = removeOnDungeonExit,
            useEffects = CloneEffects(useEffects),
            passiveEffects = CloneEffects(passiveEffects),
            behaviorEffects = CloneBehaviors(behaviorEffects),
            soulFormId = soulFormId,
            engraving = engraving,
            salvageItemCode = salvageItemCode,
            salvageMinAmount = salvageMinAmount,
            salvageMaxAmount = salvageMaxAmount,
            _isRuntimeClone = true
        };
    }

    public bool AddPassiveEffectRuntime(ItemEffect effect)
    {
        if (!_isRuntimeClone)
        {
            Debug.LogError("[ItemData] Runtime passive effect add blocked for non-runtime item: " + itemCode + ".");
            return false;
        }

        int length = passiveEffects != null ? passiveEffects.Length : 0;
        ItemEffect[] next = new ItemEffect[length + 1];
        for (int i = 0; i < length; i++)
        {
            next[i] = new ItemEffect
            {
                type = passiveEffects[i].type,
                value = passiveEffects[i].value
            };
        }

        next[length] = new ItemEffect
        {
            type = effect.type,
            value = effect.value
        };

        passiveEffects = next;
        return true;
    }

    private static ItemEffect[] CloneEffects(ItemEffect[] source)
    {
        if (source == null || source.Length == 0)
            return Array.Empty<ItemEffect>();

        ItemEffect[] clone = new ItemEffect[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            clone[i] = new ItemEffect
            {
                type = source[i].type,
                value = source[i].value
            };
        }

        return clone;
    }

    private static RelicBehavior[] CloneBehaviors(RelicBehavior[] source)
    {
        if (source == null || source.Length == 0)
            return Array.Empty<RelicBehavior>();

        RelicBehavior[] clone = new RelicBehavior[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            RelicBehavior behavior = source[i];
            if (behavior == null)
                continue;

            clone[i] = new RelicBehavior
            {
                trigger = behavior.trigger,
                action = behavior.action,
                skillTypeFilter = behavior.skillTypeFilter,
                value = behavior.value,
                duration = behavior.duration
            };
        }

        return clone;
    }
}
