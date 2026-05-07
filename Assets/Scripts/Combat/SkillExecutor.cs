using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Executes skill effects.
/// This first version preserves the existing immediate area-damage behavior;
/// later versions can route to projectile, dash, area, buff, or channel handlers.
/// </summary>
public sealed class SkillExecutor
{
    private readonly AttackExecutor _attackExecutor;
    private readonly SkillTargetResolver _targetResolver;
    private readonly HashSet<SkillExecutionType> _reportedUnsupportedTypes = new();

    public SkillExecutor(AttackExecutor attackExecutor)
    {
        _attackExecutor = attackExecutor;
        _targetResolver = new SkillTargetResolver();
    }

    public bool Execute(SkillExecutionContext context)
    {
        if (context == null) return false;
        if (context.Skill == null) return false;
        if (context.CasterTransform == null) return false;
        if (_attackExecutor == null) return false;

        switch (context.Skill.executionType)
        {
            case SkillExecutionType.InstantArea:
                return ExecuteInstantArea(context);

            case SkillExecutionType.Projectile:
            case SkillExecutionType.Dash:
            case SkillExecutionType.AreaOverTime:
            case SkillExecutionType.Buff:
                ReportUnsupportedExecutionType(context.Skill.executionType);
                return false;

            default:
                ReportUnsupportedExecutionType(context.Skill.executionType);
                return false;
        }
    }

    private bool ExecuteInstantArea(SkillExecutionContext context)
    {
        List<Vector2Int> targets = _targetResolver.ResolveTargets(context);
        _attackExecutor.BeginAttackActivation();
        _attackExecutor.ExecuteAttack(
            targets,
            context.TotalAttack + context.Skill.damage,
            context.Skill.canPenetrateWalls,
            context.Skill.isMultiTarget,
            context.Skill.knockbackForce,
            context.Skill.knockbackDuration,
            context.Skill.slowPercentage,
            context.Skill.slowDuration,
            context.HitRadius);

        return true;
    }

    private void ReportUnsupportedExecutionType(SkillExecutionType executionType)
    {
#if UNITY_EDITOR
        if (_reportedUnsupportedTypes.Add(executionType))
            Debug.LogWarning($"[SkillExecutor] Skill execution type is not implemented yet: {executionType}");
#endif
    }
}
