using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Teleport/Destination Database", fileName = "TeleportDestinationDatabase")]
public sealed class TeleportDestinationDatabase : ScriptableObject
{
    [SerializeField] private List<TeleportLocationData> locations = new();

    [NonSerialized] private Dictionary<string, TeleportLocationData> _locationsById;

    public bool TryGetLocation(string destinationId, out TeleportLocationData location)
    {
        EnsureCache();

        if (string.IsNullOrWhiteSpace(destinationId))
        {
            location = null;
            return false;
        }

        return _locationsById.TryGetValue(destinationId, out location);
    }

    private void OnEnable()
    {
        RebuildCache();
    }

    private void OnValidate()
    {
        RebuildCache();
    }

    private void EnsureCache()
    {
        if (_locationsById == null)
            RebuildCache();
    }

    private void RebuildCache()
    {
        if (_locationsById == null)
            _locationsById = new Dictionary<string, TeleportLocationData>(StringComparer.Ordinal);
        else
            _locationsById.Clear();

        if (locations == null)
            return;

        for (int i = 0; i < locations.Count; i++)
        {
            TeleportLocationData location = locations[i];
            if (location == null)
                continue;

            string id = location.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                Warn("Teleport location id is empty at index " + i + ".");
                continue;
            }

            if (id != id.Trim())
                Warn("Teleport location id has leading or trailing whitespace at index " + i + ": '" + id + "'.");

            if (string.IsNullOrWhiteSpace(location.LocationRootId))
                Warn("Teleport location root id is empty for destination '" + id + "'.");
            else if (location.LocationRootId != location.LocationRootId.Trim())
                Warn("Teleport location root id has leading or trailing whitespace for destination '" + id + "': '" + location.LocationRootId + "'.");

            if (_locationsById.ContainsKey(id))
            {
                Warn("Duplicate teleport location id '" + id + "'. Keeping first entry.");
                continue;
            }

            _locationsById.Add(id, location);
        }
    }

    private void Warn(string message)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning("[TeleportDestinationDatabase] " + message, this);
#endif
    }
}

[Serializable]
public sealed class TeleportLocationData
{
    [SerializeField] private string id;
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private GameLocationType locationType;
    [SerializeField] private string locationRootId;
    [SerializeField] private Vector3 localSpawnPosition;
    [SerializeField, Tooltip("LocationMinimapRegistry에 등록된 ID와 맞춥니다. 비어 있으면 id를 그대로 사용합니다.")]
    private string minimapLocationId;

    public string Id => id;
    public string DisplayName => displayName;
    public string Description => description;
    public GameLocationType LocationType => locationType;
    public string LocationRootId => locationRootId;
    public Vector3 LocalSpawnPosition => localSpawnPosition;

    /// <summary>
    /// TilemapMinimapSource에 설정한 locationId와 맞춥니다.
    /// 여러 destination(town_start / town_return)이 같은 장소를 가리키면,
    /// 같은 minimapLocationId를 설정해 미니맵 텍스처를 공유합니다.
    /// 비어 있으면 id를 fallback으로 사용합니다.
    /// </summary>
    public string MinimapLocationId =>
        string.IsNullOrWhiteSpace(minimapLocationId) ? id : minimapLocationId;
}
