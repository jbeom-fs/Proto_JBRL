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
    [SerializeField] private PlayerFormId soulFormId;
    [SerializeField] private EngravingData engraving;
    [SerializeField] private string salvageItemCode;
    [SerializeField] private int salvageMinAmount = 1;
    [SerializeField] private int salvageMaxAmount = 1;

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
    public PlayerFormId SoulFormId => soulFormId;
    public EngravingData Engraving => engraving;
    public string SalvageItemCode => salvageItemCode;
    public int SalvageMinAmount => salvageMinAmount;
    public int SalvageMaxAmount => salvageMaxAmount;
}
