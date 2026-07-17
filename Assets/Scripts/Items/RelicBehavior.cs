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
    AttackBleed,
    CastSkill
}

public enum ProcOriginMode
{
    CasterPosition,
    HitPosition,
    RandomInRadius
}

public enum ProcDirectionMode
{
    Aim,
    Context,
    Random
}

[Serializable]
public sealed class RelicBehavior
{
    public RelicTrigger trigger;
    public RelicAction action;
    public int skillTypeFilter;
    public int value;
    public float duration;
    public SkillData procSkill;
    public ProcOriginMode procOrigin;
    public ProcDirectionMode procDirection;
    public float procSpawnRadius;
}
