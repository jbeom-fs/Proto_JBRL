using System.Collections.Generic;
using UnityEngine;

public static class TeleportDestinationRegistry
{
    private static readonly Dictionary<string, TeleportDestinationPoint> PointsById = new();

    public static void Register(TeleportDestinationPoint point)
    {
        if (point == null)
            return;

        string id = point.DestinationId;
        if (string.IsNullOrWhiteSpace(id))
        {
            Warn("Teleport destination id is empty.", point);
            return;
        }

        if (PointsById.TryGetValue(id, out TeleportDestinationPoint existing) &&
            existing != null &&
            existing != point)
        {
            Warn("Duplicate teleport destination id '" + id + "'. Keeping first point.", point);
            return;
        }

        PointsById[id] = point;
    }

    public static void Unregister(TeleportDestinationPoint point)
    {
        if (point == null)
            return;

        string id = point.DestinationId;
        if (string.IsNullOrWhiteSpace(id))
            return;

        if (PointsById.TryGetValue(id, out TeleportDestinationPoint existing) && existing == point)
            PointsById.Remove(id);
    }

    public static bool TryGetPoint(string destinationId, out TeleportDestinationPoint point)
    {
        point = null;
        if (string.IsNullOrWhiteSpace(destinationId))
        {
            Warn("Teleport target destination id is empty.", null);
            return false;
        }

        if (PointsById.TryGetValue(destinationId, out point) && point != null)
            return true;

        Warn("Teleport destination id not registered: " + destinationId, null);
        point = null;
        return false;
    }

    private static void Warn(string message, Object context)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning("[TeleportDestinationRegistry] " + message, context);
#endif
    }
}
