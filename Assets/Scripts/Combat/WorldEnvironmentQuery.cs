using UnityEngine;

/// <summary>
/// 월드 좌표 기반 환경(Walkable / Wall / LOS / Bounds) 판정을 한 곳으로 모은 정적 서비스입니다.
///
/// 이 단계는 no-op refactor 입니다. 내부 구현은 기존 EliteArenaEncounterController.Try... 분기와
/// DungeonManager.Instance / DungeonData fallback을 그대로 위임합니다. Physics2D 기반 전환은
/// 후속 단계(Wall Tilemap Collider/Layer 세팅 이후)에서 진행합니다.
///
/// 호출자는 더 이상 EliteArenaEncounterController.Try... 분기를 자체적으로 가질 필요가 없습니다.
/// MovementBlockerQuery는 별도의 관심사(이동을 차단하는 동적 객체)이므로 본 서비스에 통합하지 않습니다.
/// </summary>
public static class WorldEnvironmentQuery
{
    /// <summary>해당 월드 좌표가 단일 지점 기준 walkable인지 여부를 반환합니다.</summary>
    public static bool IsWalkablePoint(Vector2 worldPosition)
    {
        if (EliteArenaEncounterController.TryIsArenaPointWalkable(worldPosition, out bool arenaWalkable))
            return arenaWalkable;

        DungeonManager dungeon = DungeonManager.Instance;
        if (dungeon == null || dungeon.Data == null)
            return true;

        Vector2Int grid = dungeon.WorldToGrid(worldPosition);
        return dungeon.IsWalkable(grid.x, grid.y);
    }

    /// <summary>월드 좌표 중심으로 4 코너 footprint(반경 radius)가 모두 walkable인지 여부를 반환합니다.</summary>
    public static bool IsFootprintWalkable(Vector2 worldPosition, float radius)
    {
        if (EliteArenaEncounterController.TryIsArenaFootprintWalkable(worldPosition, radius, out bool arenaWalkable))
            return arenaWalkable;

        DungeonManager dungeon = DungeonManager.Instance;
        if (dungeon == null || dungeon.Data == null)
            return true;

        // DungeonManager.IsFootprintWalkable 내부에도 동일한 Arena guard가 있지만,
        // 이미 위에서 Arena를 처리했으므로 여기서는 DungeonData 기반 4-corner 검사 경로로 떨어집니다.
        return dungeon.IsFootprintWalkable(worldPosition, radius);
    }

    /// <summary>두 월드 좌표 사이가 기하학적 LOS(wall = !IsWalkable)에서 막혀 있지 않은지 여부를 반환합니다.</summary>
    /// <remarks>
    /// AttackExecutor.HasWallBetween가 사용하던 grid Bresenham을 그대로 옮긴 동작입니다.
    /// DOOR_CLOSED 같이 IsWalkable이 false인 타일은 LOS를 막는 것으로 취급합니다.
    /// EnemyMovementHandler.HasLineOfSight는 EMPTY 전용 차단이라는 다른 semantics 이므로
    /// 이번 단계에서는 이 API로 통합하지 않습니다.
    /// </remarks>
    public static bool HasGeometryLineOfSight(Vector2 from, Vector2 to)
    {
        if (EliteArenaEncounterController.TryHasArenaLineOfSight(from, to, out bool arenaLineOfSight))
            return arenaLineOfSight;

        DungeonManager dungeon = DungeonManager.Instance;
        DungeonData data = dungeon != null ? dungeon.Data : null;
        if (dungeon == null || data == null)
            return true;

        Vector2Int fromGrid = dungeon.WorldToGrid(from);
        Vector2Int toGrid = dungeon.WorldToGrid(to);

        int dx = toGrid.x - fromGrid.x;
        int dy = toGrid.y - fromGrid.y;
        int steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        if (steps == 0)
            return true;

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            int col = Mathf.RoundToInt(fromGrid.x + dx * t);
            int row = Mathf.RoundToInt(fromGrid.y + dy * t);
            if (!data.IsWalkable(col, row))
                return false;
        }

        return true;
    }

    /// <summary>해당 월드 좌표가 벽 타일 위에 있는지 여부를 반환합니다.</summary>
    public static bool IsWallAt(Vector2 worldPosition)
    {
        return !IsWalkablePoint(worldPosition);
    }

    /// <summary>
    /// 해당 월드 좌표가 알려진 전투 공간(현재는 Arena 또는 DungeonData bounds) 안에 있는지 여부를 반환합니다.
    /// 어떤 공간도 활성화되어 있지 않으면(예: Town, 데이터 없음) true를 반환해 기존 OOB 판정과
    /// 동일하게 "안전한 쪽"으로 동작합니다.
    /// </summary>
    public static bool IsInsideKnownCombatSpace(Vector2 worldPosition)
    {
        if (EliteArenaEncounterController.TryIsArenaWorldPosition(worldPosition, out bool isInArena))
            return isInArena;

        DungeonManager dungeon = DungeonManager.Instance;
        if (dungeon == null || dungeon.Data == null)
            return true;

        Vector2Int grid = dungeon.WorldToGrid(worldPosition);
        return dungeon.Data.InBounds(grid.x, grid.y);
    }
}
