using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 모든 전투 공간(Dungeon, Elite Arena, 추후 Boss Arena 등)의 walkable / wall / LOS / bounds 판정을
/// 한 곳으로 통합한 정적 query 서비스입니다.
///
/// 라우팅 규칙:
///   1. 등록된 <see cref="WalkabilityArea"/> 중 world 좌표를 포함하는 첫 번째 Area를 사용합니다.
///   2. 어느 Area에도 속하지 않으면 절차 Dungeon(<see cref="DungeonManager"/> / DungeonData)로 fallback.
///   3. Dungeon 데이터마저 없으면 "안전한 쪽"(walkable=true, OOB=false)으로 반환해 기존 동작을 보존합니다.
///
/// Area 등록은 WalkabilityArea의 OnEnable/OnDisable에서만 일어나며,
/// 런타임 Find나 매 프레임 allocation을 사용하지 않습니다.
/// </summary>
public static class WalkabilityQuery
{
    private static readonly List<WalkabilityArea> s_Areas = new List<WalkabilityArea>(4);

    /// <summary>Area를 등록합니다. WalkabilityArea.OnEnable에서 호출됩니다.</summary>
    public static void Register(WalkabilityArea area)
    {
        if (area == null) return;
        for (int i = 0; i < s_Areas.Count; i++)
            if (ReferenceEquals(s_Areas[i], area))
                return;
        s_Areas.Add(area);
    }

    /// <summary>Area 등록을 해제합니다. WalkabilityArea.OnDisable에서 호출됩니다.</summary>
    public static void Unregister(WalkabilityArea area)
    {
        if (area == null) return;
        for (int i = s_Areas.Count - 1; i >= 0; i--)
            if (ReferenceEquals(s_Areas[i], area))
                s_Areas.RemoveAt(i);
    }

    /// <summary>world 좌표를 포함하는 첫 Area를 반환하거나 null.</summary>
    public static WalkabilityArea FindAreaContaining(Vector3 worldPosition)
    {
        for (int i = 0; i < s_Areas.Count; i++)
        {
            WalkabilityArea a = s_Areas[i];
            if (a == null) continue;
            if (!a.MightContainWorld(worldPosition)) continue;
            if (a.IsInsideWorld(worldPosition))
                return a;
        }
        return null;
    }

    /// <summary>해당 world 좌표가 walkable인지 판정합니다.</summary>
    public static bool IsWalkable(Vector3 worldPosition)
    {
        WalkabilityArea area = FindAreaContaining(worldPosition);
        if (area != null)
            return area.IsWalkableWorld(worldPosition);

        // Dungeon fallback — 절차 던전은 DungeonData에 grid가 있다.
        DungeonManager dungeon = DungeonManager.Instance;
        DungeonData data = dungeon != null ? dungeon.Data : null;
        if (dungeon == null || data == null)
            return true;

        Vector2Int grid = dungeon.WorldToGrid(worldPosition);
        return data.IsWalkable(grid.x, grid.y);
    }

    /// <summary>해당 world 좌표가 wall인지 여부. <see cref="IsWalkable"/>의 보수.</summary>
    public static bool IsWall(Vector3 worldPosition) => !IsWalkable(worldPosition);

    public static bool IsBlocked(Vector3 worldPosition)
    {
        WalkabilityArea area = FindAreaContaining(worldPosition);
        if (area != null)
            return !area.IsWalkableWorld(worldPosition);

        DungeonManager dungeon = DungeonManager.Instance;
        DungeonData data = dungeon != null ? dungeon.Data : null;
        if (dungeon == null || data == null)
            return false;

        Vector2Int grid = dungeon.WorldToGrid(worldPosition);
        return !data.IsWalkable(grid.x, grid.y);
    }

    public static bool IsProjectilePositionValid(Vector3 worldPosition)
    {
        return IsInsideKnownArea(worldPosition) && !IsBlocked(worldPosition);
    }

    /// <summary>
    /// 중심 좌표를 기준으로 4-corner footprint(반경 radius)가 모두 walkable인지 판정합니다.
    /// Area 안에서는 해당 Area의 grid query, 밖에서는 DungeonData grid query를 사용해
    /// 기존 IsFootprintWalkable의 의미와 동일하게 동작합니다.
    /// </summary>
    public static bool IsFootprintWalkable(Vector3 worldPosition, float radius)
    {
        WalkabilityArea area = FindAreaContaining(worldPosition);
        if (area != null)
            return area.IsFootprintWalkableWorld(worldPosition, radius);

        DungeonManager dungeon = DungeonManager.Instance;
        DungeonData data = dungeon != null ? dungeon.Data : null;
        if (dungeon == null || data == null)
            return true;

        return DungeonFootprintWalkable(dungeon, data, worldPosition, radius);
    }

    /// <summary>
    /// world 좌표가 알려진 공간(등록된 Area 또는 절차 Dungeon bounds) 안에 있는지 판정합니다.
    /// 어떤 공간도 활성화되지 않은 시점에는 true를 반환해 기존 OOB 판정과 동일하게 "안전한 쪽"으로 둡니다.
    /// </summary>
    public static bool IsInsideKnownArea(Vector3 worldPosition)
    {
        if (FindAreaContaining(worldPosition) != null) return true;

        DungeonManager dungeon = DungeonManager.Instance;
        DungeonData data = dungeon != null ? dungeon.Data : null;
        if (dungeon == null || data == null)
            return true;

        Vector2Int grid = dungeon.WorldToGrid(worldPosition);
        return data.InBounds(grid.x, grid.y);
    }

    /// <summary>
    /// 두 world 좌표 사이가 기하학적 LOS로 막혀 있지 않은지 판정합니다.
    /// 같은 Area 내부면 그 Area의 grid Bresenham을, 둘 다 Area 밖이면 Dungeon grid Bresenham을 사용합니다.
    /// 서로 다른 Area에 걸친 경우(또는 한쪽만 Area인 경우) false를 반환해 공간 간 LOS는 항상 차단합니다.
    /// </summary>
    public static bool HasLineOfSight(Vector3 fromWorld, Vector3 toWorld)
    {
        WalkabilityArea fromArea = FindAreaContaining(fromWorld);
        WalkabilityArea toArea = FindAreaContaining(toWorld);

        if (fromArea != null || toArea != null)
        {
            if (!ReferenceEquals(fromArea, toArea))
                return false;
            return fromArea.HasLineOfSightWorld(fromWorld, toWorld);
        }

        DungeonManager dungeon = DungeonManager.Instance;
        DungeonData data = dungeon != null ? dungeon.Data : null;
        if (dungeon == null || data == null)
            return true;

        Vector2Int fromGrid = dungeon.WorldToGrid(fromWorld);
        Vector2Int toGrid = dungeon.WorldToGrid(toWorld);

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

    /// <summary>
    /// desired 좌표에서 가장 가까운 walkable world 좌표를 spiral 검색해 반환합니다.
    /// Area 안이면 Area의 walkTilemap을 그리드 단위로, Dungeon이면 DungeonData를 사용합니다.
    /// 찾지 못하면 <paramref name="fallback"/>을 그대로 반환합니다.
    /// </summary>
    public static Vector3 GetNearestWalkableWorldPosition(Vector3 desired, Vector3 fallback)
    {
        if (TryGetNearestWalkableWorldPosition(desired, out Vector3 result))
            return result;
        return fallback;
    }

    /// <summary>
    /// preferred에서 가장 가까운 walkable world 좌표를 찾습니다.
    /// Area 안이면 해당 Area의 spiral 검색, 밖이면 DungeonData spiral 검색을 사용합니다.
    /// 찾지 못하면 result는 preferred 그대로, false 반환.
    /// </summary>
    public static bool TryGetNearestWalkableWorldPosition(Vector3 preferred, out Vector3 result)
    {
        const int DefaultMaxRadius = 8;

        WalkabilityArea area = FindAreaContaining(preferred);
        if (area != null)
            return area.TryGetNearestWalkableWorldPosition(preferred, out result);

        DungeonManager dungeon = DungeonManager.Instance;
        DungeonData data = dungeon != null ? dungeon.Data : null;
        if (dungeon == null || data == null)
        {
            result = preferred;
            return false;
        }

        if (TrySpiralSearchInDungeon(dungeon, data, preferred, DefaultMaxRadius, out Vector3 dungeonResult))
        {
            result = dungeonResult;
            return true;
        }
        result = preferred;
        return false;
    }

    /// <summary>
    /// 패턴 코드(EliteJumpPatternRuntime 등)의 기존 시그니처와 동일한 형태로
    /// "원점에서 maxDistance 이내, footprintRadius로 점유 가능한 가장 가까운 walkable world 좌표"를 찾습니다.
    /// </summary>
    public static bool TryFindNearestWalkable(
        Vector3 desired,
        Vector3 origin,
        float maxDistanceFromOrigin,
        float footprintRadius,
        int maxSearchRadius,
        out Vector3 position)
    {
        position = desired;
        float maxDistanceSqr = maxDistanceFromOrigin * maxDistanceFromOrigin;
        int searchRadius = Mathf.Max(0, maxSearchRadius);

        WalkabilityArea area = FindAreaContaining(desired);
        if (area != null && area.WalkTilemap != null)
        {
            Tilemap walk = area.WalkTilemap;
            Vector3Int center = walk.WorldToCell(desired);
            for (int r = 0; r <= searchRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (r > 0 && Mathf.Abs(dx) != r && Mathf.Abs(dy) != r)
                            continue;

                        Vector3Int cell = new Vector3Int(center.x + dx, center.y + dy, center.z);
                        if (!area.IsWalkableCell(cell)) continue;

                        Vector3 world = walk.GetCellCenterWorld(cell);
                        if ((world - origin).sqrMagnitude > maxDistanceSqr) continue;
                        if (!IsFootprintWalkable(world, footprintRadius)) continue;

                        position = world;
                        return true;
                    }
                }
            }
            return false;
        }

        DungeonManager dungeon = DungeonManager.Instance;
        DungeonData data = dungeon != null ? dungeon.Data : null;
        if (dungeon == null || data == null)
            return false;

        Vector2Int centerGrid = dungeon.WorldToGrid(desired);
        for (int r = 0; r <= searchRadius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (r > 0 && Mathf.Abs(dx) != r && Mathf.Abs(dy) != r)
                        continue;

                    int col = centerGrid.x + dx;
                    int row = centerGrid.y + dy;
                    if (!data.InBounds(col, row)) continue;
                    if (!data.IsWalkable(col, row)) continue;

                    Vector3 world = dungeon.GridToWorld(new Vector2Int(col, row));
                    if ((world - origin).sqrMagnitude > maxDistanceSqr) continue;
                    if (!IsFootprintWalkable(world, footprintRadius)) continue;

                    position = world;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool TryFindNearestWalkable(Vector3 position, float radius, out Vector3 result)
    {
        const int DefaultSearchRadius = 8;
        float maxDistance = Mathf.Max(1f, radius) * DefaultSearchRadius;
        return TryFindNearestWalkable(
            position,
            position,
            maxDistance,
            radius,
            DefaultSearchRadius,
            out result);
    }

    private static bool TrySpiralSearchInArea(WalkabilityArea area, Vector3 desired, int maxRadius, out Vector3 result)
    {
        Tilemap walk = area.WalkTilemap;
        Vector3Int center = walk.WorldToCell(desired);
        for (int r = 0; r <= maxRadius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (r > 0 && Mathf.Abs(dx) != r && Mathf.Abs(dy) != r)
                        continue;

                    Vector3Int cell = new Vector3Int(center.x + dx, center.y + dy, center.z);
                    if (!area.IsWalkableCell(cell)) continue;

                    result = walk.GetCellCenterWorld(cell);
                    return true;
                }
            }
        }

        result = default;
        return false;
    }

    private static bool TrySpiralSearchInDungeon(DungeonManager dungeon, DungeonData data, Vector3 desired, int maxRadius, out Vector3 result)
    {
        Vector2Int center = dungeon.WorldToGrid(desired);
        for (int r = 0; r <= maxRadius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (r > 0 && Mathf.Abs(dx) != r && Mathf.Abs(dy) != r)
                        continue;

                    int col = center.x + dx;
                    int row = center.y + dy;
                    if (!data.InBounds(col, row)) continue;
                    if (!data.IsWalkable(col, row)) continue;

                    result = dungeon.GridToWorld(new Vector2Int(col, row));
                    return true;
                }
            }
        }

        result = default;
        return false;
    }

    private static bool DungeonFootprintWalkable(DungeonManager dungeon, DungeonData data, Vector3 worldPosition, float radius)
    {
        Vector2Int g;
        g = dungeon.WorldToGrid(new Vector3(worldPosition.x - radius, worldPosition.y - radius, 0f));
        if (!data.IsWalkable(g.x, g.y)) return false;
        g = dungeon.WorldToGrid(new Vector3(worldPosition.x + radius, worldPosition.y - radius, 0f));
        if (!data.IsWalkable(g.x, g.y)) return false;
        g = dungeon.WorldToGrid(new Vector3(worldPosition.x - radius, worldPosition.y + radius, 0f));
        if (!data.IsWalkable(g.x, g.y)) return false;
        g = dungeon.WorldToGrid(new Vector3(worldPosition.x + radius, worldPosition.y + radius, 0f));
        if (!data.IsWalkable(g.x, g.y)) return false;
        return true;
    }
}
