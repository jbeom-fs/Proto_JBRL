using System.Collections.Generic;
using UnityEngine;

// ── 공격 패턴 종류 ──────────────────────────────────────────────────────
/// <summary>
/// 새 패턴 추가: enum에 값 하나 추가 → AttackPattern.GetTargets() 에 case 추가.
/// WeaponData / SkillData 의 attackPattern 필드에서 선택합니다.
/// </summary>
public enum AttackPatternType
{
    Single,    // 정면 range칸 거리의 1칸
    Cross,     // 상하좌우 각 range칸
    Diagonal,  // 대각선 4방향 각 range칸
    Circle,    // 체비쇼프 거리 range 이내 전체 (정사각 영역)
    Line,      // 정면 직선 range칸
    Cone,      // 정면 + 좌우45° 각 range칸 부채꼴
}

// ── 패턴 → 타겟 그리드 좌표 변환 ────────────────────────────────────────
public static class AttackPattern
{
    private static readonly Vector2Int[] s_Cardinals = {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };
    private static readonly Vector2Int[] s_Diagonals = {
        new( 1,  1), new( 1, -1), new(-1,  1), new(-1, -1)
    };

    /// <param name="pattern">공격 형태</param>
    /// <param name="origin">공격자의 그리드 좌표</param>
    /// <param name="facing">공격자의 바라보는 방향 (정규화된 그리드 단위)</param>
    /// <param name="range">모든 패턴의 사정거리 (칸)</param>
    public static List<Vector2Int> GetTargets(
        AttackPatternType pattern, Vector2Int origin, Vector2Int facing, int range = 1, float coneHalfAngle = 45f)
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
        switch (pattern)
        {
            case AttackPatternType.Single:
                // 정면 range칸 거리의 단일 타겟
                targets.Add(origin + facing * range);
                break;

            case AttackPatternType.Cross:
                // 상하좌우 각 방향으로 range칸
                for (int i = 1; i <= range; i++)
                    foreach (var d in s_Cardinals)
                        targets.Add(origin + d * i);
                break;

            case AttackPatternType.Diagonal:
                // 대각 4방향으로 각 range칸
                for (int i = 1; i <= range; i++)
                    foreach (var d in s_Diagonals)
                        targets.Add(origin + d * i);
                break;

            case AttackPatternType.Circle:
                // 체비쇼프 거리 range 이내 모든 칸 (정사각 영역, 자신 제외)
                for (int dx = -range; dx <= range; dx++)
                    for (int dy = -range; dy <= range; dy++)
                        if (dx != 0 || dy != 0)
                            targets.Add(origin + new Vector2Int(dx, dy));
                break;

            case AttackPatternType.Line:
                // 정면 직선 range칸
                for (int i = 1; i <= range; i++)
                    targets.Add(origin + facing * i);
                break;

            case AttackPatternType.Cone:
                AddConeTargets(targets, origin, facing, range, coneHalfAngle);
                break;
        }
    }

    // facing 벡터를 45도 시계 방향으로 회전 (그리드 단위, 결과 -1~1 클램프)
    private static void AddConeTargets(
        List<Vector2Int> targets, Vector2Int origin, Vector2Int facing, int range, float halfAngleDeg)
    {
        Vector2 facingDir = new Vector2(facing.x, facing.y);
        if (facingDir.sqrMagnitude < 0.01f)
            facingDir = Vector2.down;

        facingDir.Normalize();

        float radius    = range * Mathf.Sqrt(2f) + 0.5f;
        float radiusSqr = radius * radius;
        int   maxOffset = Mathf.CeilToInt(radius);
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
