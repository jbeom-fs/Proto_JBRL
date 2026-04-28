using UnityEngine;

/// <summary>
/// 스킬 데이터 — WeaponData.skills[] 슬롯에 할당합니다.
/// Assets > Create > JBLogLike > Combat > Skill 로 생성합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewSkill", menuName = "JBLogLike/Combat/Skill")]
public class SkillData : ScriptableObject
{
    [Header("기본 정보")]
    public string skillName = "새 스킬";
    [TextArea(2, 4)]
    public string description;

    [Header("전투")]
    public int               damage        = 10;
    public AttackPatternType attackPattern = AttackPatternType.Cross;
    [Tooltip("Line/Cone 패턴 전용 사정거리(칸)")]
    public int               patternRange  = 1;

    [Header("비용 및 쿨다운")]
    public int   mpCost   = 3;
    public float cooldown = 2f;
}
