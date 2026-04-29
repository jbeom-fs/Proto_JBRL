using UnityEngine;

/// <summary>
/// 무기 데이터 — Inspector에서 드래그만 하면 기본 공격·스킬·스탯 보정이 즉시 반영됩니다.
/// Assets > Create > JBLogLike > Combat > Weapon 으로 생성합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewWeapon", menuName = "JBLogLike/Combat/Weapon")]
public class WeaponData : ScriptableObject
{
    [Header("기본 정보")]
    public string weaponName = "기본 검";

    [Header("기본 공격")]
    public int            damage         = 5;
    public float          attackCooldown = 0.3f;
    public AttackPatternType attackPattern  = AttackPatternType.Single;
    [Tooltip("모든 패턴의 사정거리(칸). Line=직선N칸, Cross/Diagonal=각방향N칸, Circle=체비쇼프N, Cone=부채꼴N칸, Single=N칸 거리 1타겟")]
    public int            patternRange   = 1;

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

    [Header("스킬 슬롯 (Q / W / E / R 키)")]
    public SkillData[] skills = new SkillData[4];
}
