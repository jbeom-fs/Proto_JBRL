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
    [Tooltip("비각인 아이템 전용 등급. 각인류는 각 SO의 grade가 단일 진실이므로 이 값을 사용하지 않습니다.")]
    [SerializeField] private ItemRarity rarity;
    [SerializeField] private bool stackable;
    [Min(1)]
    [SerializeField] private int maxStack = 1;
    [SerializeField] private bool removeOnFloorTransition;
    [SerializeField] private bool removeOnDungeonExit;
    [SerializeField] private ItemEffect[] useEffects = Array.Empty<ItemEffect>();
    [SerializeField] private ItemEffect[] passiveEffects = Array.Empty<ItemEffect>();
    [SerializeField] private BehaviorEffect[] behaviorEffects = Array.Empty<BehaviorEffect>();
    [SerializeField] private PlayerFormId soulFormId;
    [SerializeField] private EngravingData engraving;
    [SerializeField] private PassiveEngravingData passiveEngraving;
    [SerializeField] private string salvageItemCode;
    [SerializeField] private int salvageMinAmount = 1;
    [SerializeField] private int salvageMaxAmount = 1;
    [NonSerialized] private bool _isRuntimeClone;

    public string ItemCode => itemCode;
    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public string Description => description;
    public ItemType ItemType => itemType;
    /// <summary>
    /// 비각인 아이템 전용 등급. Engraving과 PassiveEngraving의 등급은 각 SO의 grade만 사용합니다.
    /// </summary>
    public ItemRarity Rarity => rarity;
    public bool Stackable => stackable;
    public int MaxStack => maxStack;
    public bool RemoveOnFloorTransition => removeOnFloorTransition;
    public bool RemoveOnDungeonExit => removeOnDungeonExit;
    public IReadOnlyList<ItemEffect> UseEffects => useEffects ?? Array.Empty<ItemEffect>();
    public IReadOnlyList<ItemEffect> PassiveEffects => passiveEffects ?? Array.Empty<ItemEffect>();
    public IReadOnlyList<BehaviorEffect> BehaviorEffects => behaviorEffects ?? Array.Empty<BehaviorEffect>();
    public PlayerFormId SoulFormId => soulFormId;
    public EngravingData Engraving => engraving;
    public PassiveEngravingData PassiveEngraving => passiveEngraving;
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
            rarity = rarity,
            stackable = stackable,
            maxStack = maxStack,
            removeOnFloorTransition = removeOnFloorTransition,
            removeOnDungeonExit = removeOnDungeonExit,
            useEffects = CloneEffects(useEffects),
            passiveEffects = CloneEffects(passiveEffects),
            behaviorEffects = CloneBehaviors(behaviorEffects),
            soulFormId = soulFormId,
            engraving = engraving,
            passiveEngraving = passiveEngraving,
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

    private static BehaviorEffect[] CloneBehaviors(BehaviorEffect[] source)
    {
        if (source == null || source.Length == 0)
            return Array.Empty<BehaviorEffect>();

        BehaviorEffect[] clone = new BehaviorEffect[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            BehaviorEffect behavior = source[i];
            if (behavior == null)
                continue;

            clone[i] = new BehaviorEffect
            {
                trigger = behavior.trigger,
                action = behavior.action,
                skillTypeFilter = behavior.skillTypeFilter,
                comboTierDamages = behavior.comboTierDamages != null
                    ? (int[])behavior.comboTierDamages.Clone()
                    : Array.Empty<int>(),
                value = behavior.value,
                lowHealthThresholdPct = behavior.lowHealthThresholdPct,
                lostHealthPctPerLifestealPct =
                    behavior.lostHealthPctPerLifestealPct,
                overhealShieldConversionPct =
                    behavior.overhealShieldConversionPct,
                lifestealShieldCapPct = behavior.lifestealShieldCapPct,
                lifestealShieldDuration = behavior.lifestealShieldDuration,
                duration = behavior.duration,
                procSkill = behavior.procSkill,
                procOrigin = behavior.procOrigin,
                procDirection = behavior.procDirection,
                procSpawnRadius = behavior.procSpawnRadius
            };
        }

        return clone;
    }
}
