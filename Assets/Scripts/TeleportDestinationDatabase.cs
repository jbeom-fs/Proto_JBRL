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
    [SerializeField, Tooltip("LocationMinimapRegistry の登録 ID と一致させる。空欄の場合は id をそのまま使用する。")]
    private string minimapLocationId;

    public string Id => id;
    public string DisplayName => displayName;
    public string Description => description;
    public GameLocationType LocationType => locationType;

    /// <summary>
    /// TilemapMinimapSource に設定した locationId と合わせる。
    /// 複数の destination (town_start / town_return) が同じ場所を指す場合、
    /// 同一の minimapLocationId を設定することでテクスチャを共有できる。
    /// 空欄時は id フォールバック。
    /// </summary>
    public string MinimapLocationId =>
        string.IsNullOrWhiteSpace(minimapLocationId) ? id : minimapLocationId;
}
