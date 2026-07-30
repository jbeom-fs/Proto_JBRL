using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무기 데이터 — Inspector에서 드래그만 하면 기본 공격·스킬·스탯 보정이 즉시 반영됩니다.
/// Assets > Create > JBRogLike > Combat > Weapon 으로 생성합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewWeapon", menuName = "JBRogLike/Combat/Weapon")]
public class WeaponData : ScriptableObject
{
    [Header("기본 정보")]
    public string weaponName = "기본 검";

    [Header("기본 공격")]
    public int            damage         = 5;
    public float          attackCooldown = 0.3f;
    public AttackPatternType attackPattern  = AttackPatternType.Single;
    [Tooltip("SkillData used for this weapon's basic attack animation and projectile execution. PlayerCombatController fallback is used when empty.")]
    public SkillData basicAttackSkillData;
    [Tooltip("모든 패턴의 사정거리(칸). Line=직선N칸, Cross/Diagonal=각방향N칸, Circle=체비쇼프N, Cone=부채꼴N칸, Single=N칸 거리 1타겟")]
    public int            patternRange   = 1;
    [Tooltip("true면 기본 공격이 범위 내 모든 적에게 피해를 줍니다. false면 가장 가까운 적 1명에게만 피해.")]
    public bool           basicAttackMultiTarget = false;

    [Header("Hit Effects")]
    public float knockbackForce = 0f;
    public float knockbackDuration = 0f;
    [Range(0f, 1f)]
    public float slowPercentage = 0f;
    public float slowDuration = 0f;

    [Header("스탯 보정 (장착 시 플레이어에게 합산)")]
    public int bonusAttack  = 0;
    public int bonusDefense = 0;

    [Header("벽 관통")]
    [Tooltip("false: 벽에 막힘 / true: 벽을 무시하고 유닛에게 피해")]
    public bool canPenetrateWalls = false;

  
    [Header("Magazine")]
    public bool usesMagazine = false;
    [Min(0)] public int magazineSize = 0;
    [Min(0f)] public float reloadTime = 1f;
    [Min(0)] public int reloadAmount = 0;

    [Header("Skills (Q / W / E / R)")]
    public SkillData[] skills = new SkillData[4];

    [Header("Passive Engravings")]
    [Tooltip("무료 기본 패시브. 항상 해금되며 런 시작 시 기본으로 적용됩니다.")]
    public PassiveEngravingData defaultPassive;

    [Tooltip("Soul Altar에서 구매할 수 있는 유료 패시브 후보 목록입니다. 기본 패시브는 포함하지 않습니다.")]
    public List<PassiveEngravingData> passiveEngravings = new List<PassiveEngravingData>();
}
