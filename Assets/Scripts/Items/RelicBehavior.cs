using System;

public enum RelicTrigger
{
    OnKill,
    OnSkillUsed,
    Passive
}

public enum RelicAction
{
    Heal,
    AttackPoison,
    AttackBleed
}

[Serializable]
public sealed class RelicBehavior
{
    public RelicTrigger trigger;
    public RelicAction action;
    public int skillTypeFilter;
    public int value;
    public float duration;
}
