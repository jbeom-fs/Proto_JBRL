using System;
using UnityEngine;

/// <summary>
/// 폼 전용 패시브 각인. SkillData를 상속하지 않으며 장착 중인 behavior만 런타임에 적재됩니다.
/// owningForm = 귀속 폼, grade = 희귀도/UI 라벨(메커니즘 영향 없음).
/// </summary>
[CreateAssetMenu(fileName = "NewPassiveEngraving", menuName = "JBRogLike/Combat/Passive Engraving")]
public sealed class PassiveEngravingData : ScriptableObject
{
    [Header("Passive Engraving")]
    [Tooltip("영속 해금에 사용하는 폼 무관 전역 고유 ID입니다. 한 번 정한 값은 절대 변경하지 마세요.")]
    public string unlockId;

    [Tooltip("표시 이름. 비어 있으면 에셋 이름을 사용합니다.")]
    public string passiveName;

    [Tooltip("패시브 각인 UI에 표시할 아이콘입니다.")]
    public Sprite icon;

    [TextArea]
    [Tooltip("패시브 각인의 설명입니다.")]
    public string description;

    [Tooltip("이 패시브 각인이 귀속된 폼. 해당 폼의 풀에만 적재할 수 있습니다.")]
    public PlayerFormId owningForm = PlayerFormId.Normal;

    [Tooltip("등급 라벨(희미/완전/태초). 희귀도/UI에만 쓰며 수치 자동 보정은 없습니다.")]
    public EngravingGrade grade = EngravingGrade.Faint;

    [Tooltip("장착 중 BehaviorRuntime에 병합할 behavior 목록입니다.")]
    public BehaviorEffect[] behaviors = Array.Empty<BehaviorEffect>();
}
