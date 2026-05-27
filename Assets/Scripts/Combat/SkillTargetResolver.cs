using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves skill shape cells for execution and preview.
/// It keeps the current AttackPattern semantics intact while giving later
/// projectile, dash, area, and installable effects a shared target-query entry point.
/// </summary>
public sealed class SkillTargetResolver
{
    private readonly List<Vector3> _worldTargetBuffer = new();

    public List<Vector3> ResolveWorldTargets(SkillExecutionContext context)
    {
        _worldTargetBuffer.Clear();
        if (context == null || context.Skill == null) return _worldTargetBuffer;

        FillWorldTargets(
            context.Skill.attackPattern,
            context.CasterPosition,
            context.AimDirection,
            context.GridAimDirection,
            context.Skill.patternRange,
            context.Skill.coneHalfAngle,
            _worldTargetBuffer);
        return _worldTargetBuffer;
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

    public static void FillWorldTargets(
        AttackPatternType pattern,
        Vector3 originWorld,
        Vector2 aimDirection,
        Vector2Int dungeonGridAimDirection,
        int range,
        float coneHalfAngle,
        List<Vector3> results)
    {
        if (results == null) return;
        results.Clear();

        if (WorldEnvironmentQuery.IsInRegisteredArea(originWorld))
        {
            FillAreaWorldTargets(pattern, originWorld, aimDirection, range, coneHalfAngle, results);
            return;
        }

        DungeonManager dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null) return;

        Vector2Int origin = dungeonManager.WorldToGrid(originWorld);
        s_TempGridTargets.Clear();
        AttackPattern.FillTargets(
            pattern,
            origin,
            dungeonGridAimDirection,
            Mathf.Max(0, range),
            coneHalfAngle,
            s_TempGridTargets);

        for (int i = 0; i < s_TempGridTargets.Count; i++)
            results.Add(dungeonManager.GridToWorld(s_TempGridTargets[i]));
    }

    private static readonly List<Vector2Int> s_TempGridTargets = new(64);

    private static void FillAreaWorldTargets(
        AttackPatternType pattern,
        Vector3 originWorld,
        Vector2 aimDirection,
        int range,
        float coneHalfAngle,
        List<Vector3> results)
    {
        Vector2Int facing = ResolveWorldFacing(aimDirection);
        s_TempGridTargets.Clear();
        AttackPattern.FillTargets(
            pattern,
            Vector2Int.zero,
            facing,
            Mathf.Max(0, range),
            coneHalfAngle,
            s_TempGridTargets);

        float cellSize = WorldEnvironmentQuery.GetCellSize(originWorld);
        for (int i = 0; i < s_TempGridTargets.Count; i++)
        {
            Vector2Int offset = s_TempGridTargets[i];
            results.Add(new Vector3(
                originWorld.x + offset.x * cellSize,
                originWorld.y + offset.y * cellSize,
                originWorld.z));
        }
    }

    private static Vector2Int ResolveWorldFacing(Vector2 aimDirection)
    {
        if (AimDirectionUtility.TryGetEightWayRaw(aimDirection, out Vector2Int raw))
            return raw;

        return Vector2Int.down;
    }
}
