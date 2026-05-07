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

    public SkillExecutor(AttackExecutor attackExecutor)
    {
        _attackExecutor = attackExecutor;
    }

    public bool Execute(SkillExecutionContext context)
    {
        if (context == null) return false;
        if (context.Skill == null) return false;
        if (context.CasterTransform == null) return false;
        if (_attackExecutor == null) return false;

        List<Vector2Int> targets = ResolveTargets(context);
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

    private static List<Vector2Int> ResolveTargets(SkillExecutionContext context)
    {
        DungeonManager dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null) return new List<Vector2Int>();

        Vector2Int origin = dungeonManager.WorldToGrid(context.CasterPosition);
        SkillData skill = context.Skill;
        return AttackPattern.GetTargets(
            skill.attackPattern,
            origin,
            context.GridAimDirection,
            skill.patternRange,
            skill.coneHalfAngle);
    }
}
