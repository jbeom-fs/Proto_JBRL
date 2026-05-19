using System.Collections.Generic;
using UnityEngine;

public static class LocationRootRegistry
{
    private static readonly Dictionary<string, LocationRoot> RootsById = new();

    public static void Register(LocationRoot root)
    {
        if (root == null)
            return;

        string id = root.LocationRootId;
        if (string.IsNullOrWhiteSpace(id))
        {
            Warn("LocationRootId is empty.", root);
            return;
        }

        if (RootsById.TryGetValue(id, out LocationRoot existing) &&
            existing != null &&
            existing != root)
        {
            Warn("Duplicate LocationRootId '" + id + "'. Keeping first.", root);
            return;
        }

        RootsById[id] = root;
    }

    public static void Unregister(LocationRoot root)
    {
        if (root == null)
            return;

        string id = root.LocationRootId;
        if (string.IsNullOrWhiteSpace(id))
            return;

        if (RootsById.TryGetValue(id, out LocationRoot existing) && existing == root)
            RootsById.Remove(id);
    }

    public static bool TryGet(string locationRootId, out LocationRoot root)
    {
        root = null;

        if (string.IsNullOrWhiteSpace(locationRootId))
        {
            Warn("TryGet called with empty locationRootId.", null);
            return false;
        }

        if (RootsById.TryGetValue(locationRootId, out root) && root != null)
            return true;

        Warn("LocationRootId not registered: " + locationRootId, null);
        root = null;
        return false;
    }

    private static void Warn(string message, Object context)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning("[LocationRootRegistry] " + message, context);
#endif
    }
}
