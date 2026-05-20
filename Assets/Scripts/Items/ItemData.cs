using System;
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

    public string ItemCode => itemCode;
    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public string Description => description;
    public ItemType ItemType => itemType;
    public bool Stackable => stackable;
    public int MaxStack => maxStack;
    public bool RemoveOnFloorTransition => removeOnFloorTransition;
    public bool RemoveOnDungeonExit => removeOnDungeonExit;
}
