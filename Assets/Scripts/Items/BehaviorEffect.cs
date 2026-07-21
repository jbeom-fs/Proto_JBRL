using System;

public enum BehaviorTrigger
{
    OnKill,
    OnSkillUsed,
    Passive
}

public enum BehaviorAction
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
public sealed class BehaviorEffect
{
    public BehaviorTrigger trigger;
    public BehaviorAction action;
    public int skillTypeFilter;
    public int value;
    public float duration;
    public SkillData procSkill;
    public ProcOriginMode procOrigin;
    public ProcDirectionMode procDirection;
    public float procSpawnRadius;
}
