using System.Collections.Generic;
using UnityEngine;

public enum AttackPatternType
{
    Single,
    Cross,
    Diagonal,
    Circle,
    Line,
    Cone,
}

public static class AttackPattern
{
    private static readonly Vector2Int[] s_Cardinals =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private static readonly Vector2Int[] s_Diagonals =
    {
        new(1, 1),
        new(1, -1),
        new(-1, 1),
        new(-1, -1)
    };

    public static List<Vector2Int> GetTargets(
        AttackPatternType pattern,
        Vector2Int origin,
        Vector2Int facing,
        int range = 1,
        float coneHalfAngle = 45f)
    {
        var targets = new List<Vector2Int>();
        FillTargets(pattern, origin, facing, range, coneHalfAngle, targets);
        return targets;
    }

    public static void FillTargets(
        AttackPatternType pattern,
        Vector2Int origin,
        Vector2Int facing,
        int range,
        float coneHalfAngle,
        List<Vector2Int> targets)
    {
        if (targets == null) return;

        range = Mathf.Max(0, range);

        switch (pattern)
        {
            case AttackPatternType.Single:
                targets.Add(origin + facing * range);
                break;

            case AttackPatternType.Cross:
                for (int i = 1; i <= range; i++)
                    foreach (Vector2Int direction in s_Cardinals)
                        targets.Add(origin + direction * i);
                break;

            case AttackPatternType.Diagonal:
                for (int i = 1; i <= range; i++)
                    foreach (Vector2Int direction in s_Diagonals)
                        targets.Add(origin + direction * i);
                break;

            case AttackPatternType.Circle:
                for (int dx = -range; dx <= range; dx++)
                    for (int dy = -range; dy <= range; dy++)
                        if (dx != 0 || dy != 0)
                            targets.Add(origin + new Vector2Int(dx, dy));
                break;

            case AttackPatternType.Line:
                for (int i = 1; i <= range; i++)
                    targets.Add(origin + facing * i);
                break;

            case AttackPatternType.Cone:
                AddConeTargets(targets, origin, new Vector2(facing.x, facing.y), range, coneHalfAngle);
                break;
        }
    }

    public static void FillTargets(
        AttackPatternType pattern,
        Vector2Int origin,
        Vector2 facing,
        int range,
        float coneHalfAngle,
        List<Vector2Int> targets)
    {
        if (targets == null) return;

        range = Mathf.Max(0, range);
        facing = ResolveFacing(facing);

        switch (pattern)
        {
            case AttackPatternType.Single:
                AddUnique(targets, origin + RoundToCell(facing * range));
                break;

            case AttackPatternType.Cross:
                for (int i = 1; i <= range; i++)
                    foreach (Vector2Int direction in s_Cardinals)
                        targets.Add(origin + direction * i);
                break;

            case AttackPatternType.Diagonal:
                for (int i = 1; i <= range; i++)
                    foreach (Vector2Int direction in s_Diagonals)
                        targets.Add(origin + direction * i);
                break;

            case AttackPatternType.Circle:
                for (int dx = -range; dx <= range; dx++)
                    for (int dy = -range; dy <= range; dy++)
                        if (dx != 0 || dy != 0)
                            targets.Add(origin + new Vector2Int(dx, dy));
                break;

            case AttackPatternType.Line:
                for (int i = 1; i <= range; i++)
                    AddUnique(targets, origin + RoundToCell(facing * i));
                break;

            case AttackPatternType.Cone:
                AddConeTargets(targets, origin, facing, range, coneHalfAngle);
                break;
        }
    }

    private static Vector2 ResolveFacing(Vector2 facing)
    {
        if (facing.sqrMagnitude < 0.01f)
            return Vector2.down;

        return facing.normalized;
    }

    private static Vector2Int RoundToCell(Vector2 value)
    {
        return new Vector2Int(
            Mathf.RoundToInt(value.x),
            Mathf.RoundToInt(value.y));
    }

    private static void AddUnique(List<Vector2Int> targets, Vector2Int cell)
    {
        if (!targets.Contains(cell))
            targets.Add(cell);
    }

    private static void AddConeTargets(
        List<Vector2Int> targets,
        Vector2Int origin,
        Vector2 facing,
        int range,
        float halfAngleDeg)
    {
        Vector2 facingDir = ResolveFacing(facing);
        float radius = range * Mathf.Sqrt(2f) + 0.5f;
        float radiusSqr = radius * radius;
        int maxOffset = Mathf.CeilToInt(radius);
        float halfAngle = Mathf.Clamp(halfAngleDeg, 1f, 179f);

        for (int dx = -maxOffset; dx <= maxOffset; dx++)
        {
            for (int dy = -maxOffset; dy <= maxOffset; dy++)
            {
                if (dx == 0 && dy == 0) continue;

                Vector2 toCell = new Vector2(dx, dy);
                if (toCell.sqrMagnitude > radiusSqr) continue;
                if (Vector2.Angle(facingDir, toCell) > halfAngle) continue;

                targets.Add(origin + new Vector2Int(dx, dy));
            }
        }
    }
}
