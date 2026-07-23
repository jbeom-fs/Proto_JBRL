using System;
using System.Collections.Generic;

[Serializable]
public sealed class SaveData
{
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;
    public List<ItemStackSave> items = new List<ItemStackSave>();
    public List<EnhancementSave> enhancements = new List<EnhancementSave>();
    public List<PassiveSlotUnlockSave> passiveUnlocks = new List<PassiveSlotUnlockSave>();
}

[Serializable]
public sealed class ItemStackSave
{
    public string itemCode;
    public int count;
}

[Serializable]
public sealed class EnhancementSave
{
    public int form;
    public int stat;
    public int level;
}

[Serializable]
public sealed class PassiveSlotUnlockSave
{
    public int form;
    public int count;
}
