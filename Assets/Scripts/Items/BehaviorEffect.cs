using System;
using UnityEngine;

public enum BehaviorTrigger
{
    OnKill,
    OnSkillUsed,
    Passive,
    OnSkillCanceled,
    OnMarkerDetonate
}

public enum BehaviorAction
{
    Heal,
    AttackPoison,
    AttackBleed,
    CastSkill,
    Shield,
    LifestealEngine,
    AttackBuff,
    AilmentOverload,
    ExecuteThreshold,
    AmmoRamp,
    FocusStack,
    SplitShot
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
    [Tooltip("일반 행동 값입니다. LifestealEngine은 기본 피흡률(%), AmmoRamp는 최대 피해 보너스율(%), FocusStack은 폭발 피해, SplitShot은 분열 피해율(%)로 사용합니다.")]
    public int value;
    [Tooltip("FocusStack 전용. 폭발에 필요한 적별 투사체 명중 스택 수입니다.")]
    [Min(1)]
    public int focusRequiredStacks = 6;
    [Tooltip("SplitShot 전용. 적중 시 생성할 분열탄 수입니다.")]
    [Min(1)]
    public int splitCount = 2;
    [Tooltip("SplitShot 전용. 원본 진행 방향 좌우로 펼칠 각도입니다.")]
    [Min(0f)]
    public float splitAngleDeg = 30f;
    [Tooltip("SplitShot 전용. 연쇄 분열 가능한 세대 깊이입니다.")]
    [Range(0, 3)]
    public int splitDepth = 1;
    [Tooltip("LifestealEngine 전용. 저체력 보너스가 시작되는 HP 비율(%)입니다.")]
    [Min(0f)]
    public float lowHealthThresholdPct;
    [Tooltip("LifestealEngine 전용. 피흡 +1%를 얻는 데 필요한 잃은 HP 비율(%)입니다.")]
    [Min(0f)]
    public float lostHealthPctPerLifestealPct;
    [Tooltip("LifestealEngine 전용. 초과 회복분이 쉴드로 전환되는 비율(%)입니다.")]
    [Min(0f)]
    public float overhealShieldConversionPct;
    [Tooltip("LifestealEngine 전용. MaxHp 대비 피흡 쉴드 최대 비율(%)입니다.")]
    [Min(0f)]
    public float lifestealShieldCapPct;
    [Tooltip("LifestealEngine 전용. 피흡 쉴드 지속시간(초)입니다. 0 이하면 무한입니다.")]
    public float lifestealShieldDuration;
    [Tooltip("AilmentOverload 전용. 과부하를 적용할 상태이상 타입입니다. 패시브 전용 — 유물에 저작하지 말 것.")]
    public AilmentType ailmentOverloadType = AilmentType.Poison;
    [Tooltip("AilmentOverload 전용. 해당 상태이상이 프로필 최대 스택에 도달하면 폭발합니다. 폭발 보너스 피해율(%)입니다.")]
    [Min(0f)]
    public float ailmentOverloadBonusPct = 30f;
    [Tooltip("AilmentOverload 폭발 피해 대비 회복 비율(%). 0이면 회복 없음.")]
    [Min(0f)]
    public float ailmentOverloadHealPct;
    [Tooltip("ExecuteThreshold HP threshold percentage.")]
    [Min(0f)]
    public float executeThresholdHpPct = 20f;
    [Tooltip("ExecuteThreshold elite/boss ramp start HP percentage.")]
    [Min(0f)]
    public float executeEliteBossStartHpPct = 90f;
    [Tooltip("ExecuteThreshold elite/boss bonus percentage at ramp start.")]
    [Min(0f)]
    public float executeEliteBossStartBonusPct = 10f;
    [Tooltip("ExecuteThreshold elite/boss HP interval percentage.")]
    [Min(0f)]
    public float executeEliteBossIntervalHpPct = 10f;
    [Tooltip("ExecuteThreshold elite/boss bonus increase per HP interval.")]
    [Min(0f)]
    public float executeEliteBossBonusPerIntervalPct = 5f;
    public float duration;
    public SkillData procSkill;
    public ProcOriginMode procOrigin;
    public ProcDirectionMode procDirection;
    public float procSpawnRadius;
}
