using UnityEngine;

/// <summary>
/// 전투 코드가 직접 호출하는 환경 query 인터페이스입니다.
/// 내부 구현은 <see cref="WalkabilityQuery"/>에 위임합니다.
///
/// 호출자는 이 클래스만 알면 되고, 어떤 공간(Dungeon, Elite Arena, 이후 Boss Arena 등)인지 신경 쓰지 않습니다.
/// 새로운 공간을 추가하려면 Scene에 <see cref="WalkabilityArea"/> 컴포넌트를 붙이고 Inspector에서 연결하면 됩니다.
/// </summary>
public static class WorldEnvironmentQuery
{
    public static bool IsWalkable(Vector2 worldPosition)
        => WalkabilityQuery.IsWalkable(worldPosition);

    public static bool IsWalkable(Vector3 worldPosition)
        => WalkabilityQuery.IsWalkable(worldPosition);

    public static bool IsWalkablePoint(Vector2 worldPosition)
        => WalkabilityQuery.IsWalkable(worldPosition);

    public static bool IsFootprintWalkable(Vector2 worldPosition, float radius)
        => WalkabilityQuery.IsFootprintWalkable(worldPosition, radius);

    public static bool IsFootprintWalkable(Vector3 worldPosition, float radius)
        => WalkabilityQuery.IsFootprintWalkable(worldPosition, radius);

    public static bool IsBlocked(Vector2 worldPosition)
        => WalkabilityQuery.IsBlocked(worldPosition);

    public static bool IsBlocked(Vector3 worldPosition)
        => WalkabilityQuery.IsBlocked(worldPosition);

    public static bool IsProjectilePositionValid(Vector2 worldPosition)
        => WalkabilityQuery.IsProjectilePositionValid(worldPosition);

    public static bool IsProjectilePositionValid(Vector3 worldPosition)
        => WalkabilityQuery.IsProjectilePositionValid(worldPosition);

    public static bool HasLineOfSight(Vector2 from, Vector2 to)
        => WalkabilityQuery.HasLineOfSight(from, to);

    public static bool HasLineOfSight(Vector3 from, Vector3 to)
        => WalkabilityQuery.HasLineOfSight(from, to);

    public static bool HasGeometryLineOfSight(Vector2 from, Vector2 to)
        => WalkabilityQuery.HasLineOfSight(from, to);

    public static bool IsWallAt(Vector2 worldPosition)
        => WalkabilityQuery.IsBlocked(worldPosition);

    public static bool IsInsideKnownCombatSpace(Vector2 worldPosition)
        => WalkabilityQuery.IsInsideKnownArea(worldPosition);

    public static bool IsInRegisteredArea(Vector2 worldPosition)
        => WalkabilityQuery.FindAreaContaining(worldPosition) != null;

    public static bool IsInRegisteredArea(Vector3 worldPosition)
        => WalkabilityQuery.FindAreaContaining(worldPosition) != null;

    public static bool TryFindNearestWalkable(
        Vector3 desired,
        Vector3 origin,
        float maxDistanceFromOrigin,
        float footprintRadius,
        int maxSearchRadius,
        out Vector3 position)
        => WalkabilityQuery.TryFindNearestWalkable(
            desired,
            origin,
            maxDistanceFromOrigin,
            footprintRadius,
            maxSearchRadius,
            out position);

    public static bool TryFindNearestWalkable(Vector3 position, float radius, out Vector3 result)
        => WalkabilityQuery.TryFindNearestWalkable(position, radius, out result);

    public static float GetCellSize(Vector3 worldPosition)
    {
        WalkabilityArea area = WalkabilityQuery.FindAreaContaining(worldPosition);
        if (area != null && area.WalkTilemap != null)
            return Mathf.Max(0.01f, area.WalkTilemap.cellSize.x);

        DungeonManager dungeon = DungeonManager.Instance;
        if (dungeon != null &&
            dungeon.dungeonRenderer != null &&
            dungeon.dungeonRenderer.tilemap != null)
        {
            return Mathf.Max(0.01f, dungeon.dungeonRenderer.tilemap.cellSize.x);
        }

        return 1f;
    }
}
