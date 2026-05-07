using UnityEngine;

/// <summary>
/// Runtime information needed to execute one skill use.
/// The first user is PlayerCombatController, but the shape is intentionally
/// separated from input so enemy and boss casters can provide equivalent data later.
/// </summary>
public sealed class SkillExecutionContext
{
    public PlayerCombatController CasterCombat { get; }
    public Transform CasterTransform { get; }
    public SkillData Skill { get; }
    public int SlotIndex { get; }
    public Vector2 AimDirection { get; }
    public Vector2Int GridAimDirection { get; }
    public Vector3 CasterPosition { get; }
    public int TotalAttack { get; }
    public float HitRadius { get; }

    public SkillExecutionContext(
        PlayerCombatController casterCombat,
        Transform casterTransform,
        SkillData skill,
        int slotIndex,
        Vector2 aimDirection,
        Vector2Int gridAimDirection,
        int totalAttack,
        float hitRadius)
    {
        CasterCombat = casterCombat;
        CasterTransform = casterTransform;
        Skill = skill;
        SlotIndex = slotIndex;
        AimDirection = aimDirection;
        GridAimDirection = gridAimDirection;
        CasterPosition = casterTransform != null ? casterTransform.position : Vector3.zero;
        TotalAttack = totalAttack;
        HitRadius = hitRadius;
    }
}
