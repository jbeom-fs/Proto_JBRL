using System;
using UnityEngine;

public enum BehaviorTrigger
{
    OnKill,
    OnSkillUsed,
    Passive,
    OnSkillCanceled
}

public enum BehaviorAction
{
    Heal,
    AttackPoison,
    AttackBleed,
    CastSkill,
    Shield
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
    [Tooltip("콤보 티어별 proc 기본 데미지입니다. 길이는 ComboTierConfig.maxTier를 권장합니다. index 0 = 티어 1. 비어 있으면 티어 게이트와 오버라이드 없이 기존 동작합니다.")]
    public int[] comboTierDamages = Array.Empty<int>();
    public int value;
    public float duration;
    public SkillData procSkill;
    public ProcOriginMode procOrigin;
    public ProcDirectionMode procDirection;
    public float procSpawnRadius;
}
