using System;

public enum RelicTrigger
{
    OnKill,
    OnSkillUsed
}

public enum RelicAction
{
    Heal
}

[Serializable]
public sealed class RelicBehavior
{
    public RelicTrigger trigger;
    public RelicAction action;
    public int skillTypeFilter;
    public int value;
}
