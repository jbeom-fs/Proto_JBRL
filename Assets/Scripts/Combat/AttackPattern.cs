using System.Collections.Generic;
using UnityEngine;

// ── 공격 패턴 종류 ──────────────────────────────────────────────────────
/// <summary>
/// 새 패턴 추가: enum에 값 하나 추가 → AttackPattern.GetTargets() 에 case 추가.
/// WeaponData / SkillData 의 attackPattern 필드에서 선택합니다.
/// </summary>
public enum AttackPatternType
{
    Single,    // 정면 1칸
    Cross,     // 상하좌우 4칸
    Diagonal,  // 대각선 4칸
    Circle,    // 주변 8칸 전체
    Line,      // 정면 직선 N칸 (patternRange 참조)
    Cone,      // 정면 + 좌우 대각 3칸 (부채꼴)
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
    /// <param name="range">Line/Cone 패턴의 사정거리 (칸)</param>
    public static List<Vector2Int> GetTargets(
        AttackPatternType pattern, Vector2Int origin, Vector2Int facing, int range = 1)
    {
        var targets = new List<Vector2Int>();

        switch (pattern)
        {
            case AttackPatternType.Single:
                targets.Add(origin + facing);
                break;

            case AttackPatternType.Cross:
                foreach (var d in s_Cardinals)
                    targets.Add(origin + d);
                break;

            case AttackPatternType.Diagonal:
                foreach (var d in s_Diagonals)
                    targets.Add(origin + d);
                break;

            case AttackPatternType.Circle:
                foreach (var d in s_Cardinals) targets.Add(origin + d);
                foreach (var d in s_Diagonals) targets.Add(origin + d);
                break;

            case AttackPatternType.Line:
                for (int i = 1; i <= range; i++)
                    targets.Add(origin + facing * i);
                break;

            case AttackPatternType.Cone:
                targets.Add(origin + facing);
                targets.Add(origin + RotateCW45(facing));
                targets.Add(origin + RotateCCW45(facing));
                break;
        }

        return targets;
    }

    // facing 벡터를 45도 시계 방향으로 회전 (그리드 단위, 결과 -1~1 클램프)
    private static Vector2Int RotateCW45(Vector2Int v) =>
        new(Mathf.Clamp(v.x + v.y, -1, 1), Mathf.Clamp(v.y - v.x, -1, 1));

    private static Vector2Int RotateCCW45(Vector2Int v) =>
        new(Mathf.Clamp(v.x - v.y, -1, 1), Mathf.Clamp(v.y + v.x, -1, 1));
}
