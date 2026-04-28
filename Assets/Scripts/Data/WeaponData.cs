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
    [Tooltip("Line 패턴 전용 — 직선 사정거리(칸)")]
    public int            patternRange   = 1;

    [Header("스탯 보정 (장착 시 플레이어에게 합산)")]
    public int bonusAttack  = 0;
    public int bonusDefense = 0;

    [Header("스킬 슬롯 (1 ~ 4번 키)")]
    public SkillData[] skills = new SkillData[4];
}
