using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves skill shape cells for execution and preview.
/// It keeps the current AttackPattern semantics intact while giving later
/// projectile, dash, area, and installable effects a shared target-query entry point.
/// </summary>
public sealed class SkillTargetResolver
{
    private readonly List<Vector2Int> _targetBuffer = new();

    public List<Vector2Int> ResolveTargets(SkillExecutionContext context)
    {
        _targetBuffer.Clear();
        if (context == null || context.Skill == null) return _targetBuffer;

        DungeonManager dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null) return _targetBuffer;

        Vector2Int origin = dungeonManager.WorldToGrid(context.CasterPosition);
        ResolveShapeCells(context.Skill, origin, context.GridAimDirection, _targetBuffer);
        return _targetBuffer;
    }

    public static void ResolveShapeCells(
        SkillData skill,
        Vector2Int origin,
        Vector2Int gridAimDirection,
        List<Vector2Int> results)
    {
        if (results == null) return;
        results.Clear();
        if (skill == null) return;

        AttackPattern.FillTargets(
            skill.attackPattern,
            origin,
            gridAimDirection,
            skill.patternRange,
            skill.coneHalfAngle,
            results);
    }

    public static Vector2Int ToGridAimDirection(Vector2Int screenFacing)
    {
        return new Vector2Int(screenFacing.x, -screenFacing.y);
    }

    public static float GetPreviewRadius(int range)
    {
        return range * Mathf.Sqrt(2f) + 0.5f;
    }

    public static float GetProjectilePreviewDistance(SkillData skill, float minDistance, float maxDistance)
    {
        if (skill == null) return Mathf.Max(0.1f, minDistance);

        float min = Mathf.Max(0.1f, minDistance);
        float max = Mathf.Max(min, maxDistance);
        float distance = Mathf.Max(0f, skill.projectileSpeed) * Mathf.Max(0f, skill.projectileLifetime);
        return Mathf.Clamp(distance, min, max);
    }

    public static bool IsDirectional(AttackPatternType pattern)
    {
        return pattern == AttackPatternType.Line ||
               pattern == AttackPatternType.Cone ||
               pattern == AttackPatternType.Single;
    }
}
