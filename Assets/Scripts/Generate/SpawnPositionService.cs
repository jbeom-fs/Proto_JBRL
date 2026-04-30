// ═══════════════════════════════════════════════════════════════════
//  SpawnPositionService.cs
//  Domain Layer — 플레이어 스폰 위치 계산 전담
//
//  책임:
//    • DungeonData를 받아 최적 스폰 타일 좌표를 계산합니다.
//    • 상태를 보유하지 않는 순수 계산 서비스입니다.
//    • 데이터를 생성하거나 변경하지 않습니다.
//
//  알고리즘:
//    맵 중앙(mapWidth/2, mapHeight/2)에 맨해튼 거리가 가장 가까운
//    ROOM 타일을 탐색합니다. ROOM 타일이 없으면 맵 중앙을 반환합니다.
// ═══════════════════════════════════════════════════════════════════

using UnityEngine;

public class SpawnPositionService
{
    /// <summary>
    /// DungeonData에서 맵 중앙과 가장 가까운 ROOM 타일 좌표를 반환합니다.
    /// </summary>
    /// <param name="data">생성된 던전 데이터 (null 허용 — null 시 zero 반환)</param>
    /// <param name="mapWidth">맵 가로 타일 수</param>
    /// <param name="mapHeight">맵 세로 타일 수</param>
    public Vector2Int ComputeSpawnPos(DungeonData data, int mapWidth, int mapHeight)
    {
        if (data == null) return Vector2Int.zero;

        int midX     = mapWidth  / 2;
        int midY     = mapHeight / 2;
        int bestDist = int.MaxValue;
        int spawnCol = midX, spawnRow = midY;

        for (int row = 0; row < data.MapHeight; row++)
            for (int col = 0; col < data.MapWidth; col++)
            {
                if (data.GetTileType(col, row) != DungeonGenerator.ROOM) continue;
                int dist = Mathf.Abs(col - midX) + Mathf.Abs(row - midY);
                if (dist >= bestDist) continue;
                bestDist = dist;
                spawnCol = col;
                spawnRow = row;
            }

        return new Vector2Int(spawnCol, spawnRow);
    }
}
