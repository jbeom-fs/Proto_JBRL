using UnityEngine;

// 희미한 < 온전한 < 태초의. 순수 라벨(드랍 희귀도/UI). 코드가 수치 자동스케일 안 함.
public enum EngravingGrade
{
    Faint = 0,       // 희미한
    Whole = 1,       // 온전한
    Primordial = 2   // 태초의
}

/// <summary>
/// 폼 전용 영혼각인. SkillData를 파생해 슬롯에 그대로 Bind 가능.
/// owningForm = 결속 폼(이 폼 풀에만 적재 가능), grade = 드랍/UI 라벨(메커니즘 영향 없음).
/// 고유 메커니즘이 필요한 태초 각인은 별도 execution 로직으로 개별 구현.
/// </summary>
[CreateAssetMenu(fileName = "NewEngraving", menuName = "JBRogLike/Combat/Engraving")]
public sealed class EngravingData : SkillData
{
    [Header("Engraving")]
    [Tooltip("이 영혼각인이 결속된 폼. 해당 폼 풀에만 적재 가능.")]
    public PlayerFormId owningForm = PlayerFormId.Normal;

    [Tooltip("등급 라벨(희미한<온전한<태초의). 드랍 희귀도/UI용. 수치 자동스케일 없음.")]
    public EngravingGrade grade = EngravingGrade.Faint;
}
