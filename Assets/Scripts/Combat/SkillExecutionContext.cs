using UnityEngine;

/// <summary>
/// Runtime information needed to execute one skill use.
/// The first user is PlayerCombatController, but the shape is intentionally
/// separated from input so enemy and boss casters can provide equivalent data later.
/// </summary>
public sealed class SkillExecutionContext
{
    public PlayerCombatController CasterCombat { get; }
    public PlayerDashController CasterDash { get; }
    public PlayerFormController CasterForm { get; }
    public PlayerFormData CasterFormData { get; }
    public Transform CasterTransform { get; }
    public SkillData Skill { get; }
    public int SlotIndex { get; }
    public Vector2 AimDirection { get; }
    public Vector2Int GridAimDirection { get; }
    public Vector3 CasterPosition { get; }
    public int TotalAttack { get; }
    public float HitRadius { get; }
    public bool IsProcCast { get; }
    public bool IsBasicAttack { get; }
    public int? SkillDamageOverride { get; }
    public float AmmoRampSnapshot { get; set; } = 1f;

    public SkillExecutionContext(
        PlayerCombatController casterCombat,
        PlayerDashController casterDash,
        PlayerFormController casterForm,
        Transform casterTransform,
        SkillData skill,
        int slotIndex,
        Vector2 aimDirection,
        Vector2Int gridAimDirection,
        int totalAttack,
        float hitRadius,
        bool isProcCast = false,
        Vector3? casterPositionOverride = null,
        int? skillDamageOverride = null,
        bool isBasicAttack = false)
    {
        CasterCombat = casterCombat;
        CasterDash = casterDash;
        CasterForm = casterForm;
        CasterFormData = casterForm != null ? casterForm.CurrentForm : null;
        CasterTransform = casterTransform;
        Skill = skill;
        SlotIndex = slotIndex;
        AimDirection = aimDirection;
        GridAimDirection = gridAimDirection;
        CasterPosition = casterPositionOverride ??
                         (casterTransform != null ? casterTransform.position : Vector3.zero);
        TotalAttack = totalAttack;
        HitRadius = hitRadius;
        IsProcCast = isProcCast;
        IsBasicAttack = isBasicAttack;
        SkillDamageOverride = skillDamageOverride;
    }
}
