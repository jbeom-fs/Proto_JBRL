using System.Collections.Generic;
using UnityEngine;

public static class DungeonPlacementUtility
{
    public static bool TryGetRoomCenterWalkablePosition(
        RoomInfo room,
        DungeonManager dungeonManager,
        out Vector3 position)
    {
        position = default;
        if (dungeonManager == null || dungeonManager.Data == null)
            return false;

        List<Vector2Int> tiles = dungeonManager.Data.GetWalkableTiles(room);
        if (tiles == null || tiles.Count == 0)
            return false;

        Vector2 center = new Vector2(room.CenterX, room.CenterY);
        Vector2Int best = tiles[0];
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < tiles.Count; i++)
        {
            float distance = ((Vector2)tiles[i] - center).sqrMagnitude;
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = tiles[i];
        }

        position = dungeonManager.GridToWorld(best);
        return true;
    }
}
