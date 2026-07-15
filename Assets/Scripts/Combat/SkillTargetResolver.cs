using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves skill shape cells for execution and preview.
/// It keeps the current AttackPattern semantics intact while giving later
/// projectile, dash, area, and installable effects a shared target-query entry point.
/// </summary>
public sealed class SkillTargetResolver
{
    private const float AimDirectionEpsilonSqr = 0.0001f;
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
            context.Skill.customCells,
            _worldTargetBuffer);
        return _worldTargetBuffer;
    }

    public List<Vector3> ResolveWorldTargets(
        SkillExecutionContext context,
        Vector3 originWorld,
        IReadOnlyList<Vector2Int> overrideCells)
    {
        _worldTargetBuffer.Clear();
        if (context == null || context.Skill == null) return _worldTargetBuffer;

        bool usesOverride = overrideCells != null && overrideCells.Count > 0;
        FillWorldTargets(
            usesOverride ? AttackPatternType.Custom : context.Skill.attackPattern,
            originWorld,
            context.AimDirection,
            context.GridAimDirection,
            context.Skill.patternRange,
            context.Skill.coneHalfAngle,
            usesOverride ? overrideCells : context.Skill.customCells,
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
            results,
            skill.customCells);
    }

    public static void ResolveShapeCells(
        SkillData skill,
        Vector2Int origin,
        Vector2 gridAimDirection,
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
            results,
            skill.customCells);
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
               pattern == AttackPatternType.Single ||
               pattern == AttackPatternType.Custom;
    }

    public static void FillWorldTargets(
        AttackPatternType pattern,
        Vector3 originWorld,
        Vector2 aimDirection,
        Vector2Int dungeonGridAimDirection,
        int range,
        float coneHalfAngle,
        IReadOnlyList<Vector2Int> customCells,
        List<Vector3> results)
    {
        if (results == null) return;
        results.Clear();

        if (pattern == AttackPatternType.Custom)
        {
            FillCustomWorldTargets(originWorld, aimDirection, dungeonGridAimDirection, customCells, results);
            return;
        }

        if (WorldEnvironmentQuery.IsInRegisteredArea(originWorld))
        {
            FillAreaWorldTargets(pattern, originWorld, aimDirection, range, coneHalfAngle, customCells, results);
            return;
        }

        DungeonManager dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null) return;

        Vector2Int origin = dungeonManager.WorldToGrid(originWorld);
        Vector2 gridFacing = ResolveDungeonGridFacing(aimDirection, dungeonGridAimDirection);
        s_TempGridTargets.Clear();
        if (ShouldUseDiscreteGridFacing(aimDirection, dungeonGridAimDirection))
        {
            AttackPattern.FillTargets(
                pattern,
                origin,
                dungeonGridAimDirection,
                Mathf.Max(0, range),
                coneHalfAngle,
                s_TempGridTargets,
                customCells);
        }
        else
        {
            AttackPattern.FillTargets(
                pattern,
                origin,
                gridFacing,
                Mathf.Max(0, range),
                coneHalfAngle,
                s_TempGridTargets,
                customCells);
        }

        for (int i = 0; i < s_TempGridTargets.Count; i++)
            results.Add(dungeonManager.GridToWorld(s_TempGridTargets[i]));
    }

    private static readonly List<Vector2Int> s_TempGridTargets = new(64);

    private static void FillCustomWorldTargets(
        Vector3 originWorld,
        Vector2 aimDirection,
        Vector2Int dungeonGridAimDirection,
        IReadOnlyList<Vector2Int> customCells,
        List<Vector3> results)
    {
        if (customCells == null || customCells.Count == 0)
            return;

        if (!TryCreateCustomShapeMatcher(
                AttackPatternType.Custom,
                customCells,
                originWorld,
                aimDirection,
                dungeonGridAimDirection,
                out CustomShapeMatcher matcher))
        {
            return;
        }

        for (int i = 0; i < customCells.Count; i++)
        {
            Vector2Int offset = customCells[i];
            Vector2 worldCenter = matcher.GetCellWorldCenter(offset);
            results.Add(new Vector3(worldCenter.x, worldCenter.y, originWorld.z));
        }
    }

    public static bool TryCreateCustomShapeMatcher(
        SkillData skill,
        Vector3 originWorld,
        Vector2 aimDirection,
        Vector2Int dungeonGridAimDirection,
        out CustomShapeMatcher matcher)
    {
        if (skill == null)
        {
            matcher = default;
            return false;
        }

        return TryCreateCustomShapeMatcher(
            skill.attackPattern,
            skill.customCells,
            originWorld,
            aimDirection,
            dungeonGridAimDirection,
            out matcher);
    }

    public static bool TryCreateCustomShapeMatcher(
        IReadOnlyList<Vector2Int> customCells,
        Vector3 originWorld,
        Vector2 aimDirection,
        Vector2Int dungeonGridAimDirection,
        out CustomShapeMatcher matcher)
    {
        return TryCreateCustomShapeMatcher(
            AttackPatternType.Custom,
            customCells,
            originWorld,
            aimDirection,
            dungeonGridAimDirection,
            out matcher);
    }

    private static bool TryCreateCustomShapeMatcher(
        AttackPatternType pattern,
        IReadOnlyList<Vector2Int> customCells,
        Vector3 originWorld,
        Vector2 aimDirection,
        Vector2Int dungeonGridAimDirection,
        out CustomShapeMatcher matcher)
    {
        matcher = default;

        if (pattern != AttackPatternType.Custom || customCells == null || customCells.Count == 0)
            return false;

        Vector2 facing = ResolveCustomWorldFacing(aimDirection, dungeonGridAimDirection);
        float cellSize = WorldEnvironmentQuery.GetCellSize(originWorld);
        float angle = AimDirectionUtility.ToAuthoredFacingAngle(facing);
        matcher = new CustomShapeMatcher(originWorld, angle, cellSize, customCells);
        return true;
    }

    private static Vector2 ResolveCustomWorldFacing(
        Vector2 aimDirection,
        Vector2Int dungeonGridAimDirection)
    {
        if (aimDirection.sqrMagnitude > AimDirectionEpsilonSqr)
            return aimDirection.normalized;

        if (dungeonGridAimDirection != Vector2Int.zero)
            return new Vector2(dungeonGridAimDirection.x, -dungeonGridAimDirection.y).normalized;

        return Vector2.down;
    }

    private static void FillAreaWorldTargets(
        AttackPatternType pattern,
        Vector3 originWorld,
        Vector2 aimDirection,
        int range,
        float coneHalfAngle,
        IReadOnlyList<Vector2Int> customCells,
        List<Vector3> results)
    {
        Vector2 facing = ResolveWorldFacing(aimDirection);
        s_TempGridTargets.Clear();
        if (TryResolveSnappedRaw(aimDirection, out Vector2Int rawFacing))
        {
            AttackPattern.FillTargets(
                pattern,
                Vector2Int.zero,
                rawFacing,
                Mathf.Max(0, range),
                coneHalfAngle,
                s_TempGridTargets,
                customCells);
        }
        else
        {
            AttackPattern.FillTargets(
                pattern,
                Vector2Int.zero,
                facing,
                Mathf.Max(0, range),
                coneHalfAngle,
                s_TempGridTargets,
                customCells);
        }

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

    private static Vector2 ResolveDungeonGridFacing(Vector2 aimDirection, Vector2Int fallback)
    {
        if (aimDirection.sqrMagnitude > AimDirectionEpsilonSqr)
            return new Vector2(aimDirection.x, -aimDirection.y).normalized;

        return fallback != Vector2Int.zero ? (Vector2)fallback : Vector2.down;
    }

    private static Vector2 ResolveWorldFacing(Vector2 aimDirection)
    {
        if (aimDirection.sqrMagnitude > AimDirectionEpsilonSqr)
            return aimDirection.normalized;

        return Vector2.down;
    }

    private static bool ShouldUseDiscreteGridFacing(Vector2 aimDirection, Vector2Int fallback)
    {
        if (fallback == Vector2Int.zero)
            return false;
        if (!TryResolveSnappedRaw(aimDirection, out Vector2Int screenRaw))
            return false;

        return fallback == ToGridAimDirection(screenRaw);
    }

    private static bool TryResolveSnappedRaw(Vector2 direction, out Vector2Int raw)
    {
        raw = Vector2Int.zero;
        if (direction.sqrMagnitude <= AimDirectionEpsilonSqr)
            return false;
        if (!AimDirectionUtility.TryGetEightWayRaw(direction, out raw))
            return false;

        Vector2 normalized = direction.normalized;
        float absX = Mathf.Abs(normalized.x);
        float absY = Mathf.Abs(normalized.y);
        const float snappedEpsilon = 0.001f;

        return absX <= snappedEpsilon ||
               absY <= snappedEpsilon ||
               Mathf.Abs(absX - absY) <= snappedEpsilon;
    }
}
