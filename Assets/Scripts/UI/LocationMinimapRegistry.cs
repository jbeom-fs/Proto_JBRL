using System.Collections.Generic;
using UnityEngine;

public static class LocationMinimapRegistry
{
    private static readonly Dictionary<string, TilemapMinimapSource> SourcesById = new();

    public static void Register(TilemapMinimapSource source)
    {
        if (source == null)
            return;

        string id = source.LocationId;
        if (string.IsNullOrWhiteSpace(id))
        {
            Warn("LocationId is empty.", source);
            return;
        }

        if (SourcesById.TryGetValue(id, out TilemapMinimapSource existing) &&
            existing != null &&
            existing != source)
        {
            Warn("Duplicate LocationId '" + id + "'. Keeping first.", source);
            return;
        }

        SourcesById[id] = source;
    }

    public static void Unregister(TilemapMinimapSource source)
    {
        if (source == null)
            return;

        string id = source.LocationId;
        if (string.IsNullOrWhiteSpace(id))
            return;

        if (SourcesById.TryGetValue(id, out TilemapMinimapSource existing) && existing == source)
            SourcesById.Remove(id);
    }

    public static bool TryGet(string locationId, out TilemapMinimapSource source)
    {
        source = null;

        if (string.IsNullOrWhiteSpace(locationId))
        {
            Warn("TryGet called with empty locationId.", null);
            return false;
        }

        if (SourcesById.TryGetValue(locationId, out source) && source != null)
            return true;

        Warn("LocationId not registered: " + locationId, null);
        source = null;
        return false;
    }

    /// <summary>
    /// 등록 여부만 조용히 확인합니다. (로그 X) Teleport 흐름에서 minimap 모드 자동 감지에 사용합니다.
    /// </summary>
    public static bool Contains(string locationId)
    {
        if (string.IsNullOrWhiteSpace(locationId))
            return false;

        return SourcesById.TryGetValue(locationId, out TilemapMinimapSource source) && source != null;
    }

    private static void Warn(string message, Object context)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning("[LocationMinimapRegistry] " + message, context);
#endif
    }
}
