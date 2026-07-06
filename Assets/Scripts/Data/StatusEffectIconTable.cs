using System;
using System.Collections.Generic;
using UnityEngine;

public enum StatusEffectIconType
{
    Poison = 0,
    Bleed = 1,
    Slow = 2,
    Stun = 3
}

[CreateAssetMenu(fileName = "StatusEffectIconTable", menuName = "JBRogLike/UI/Status Effect Icon Table")]
public sealed class StatusEffectIconTable : ScriptableObject
{
    public const string ResourcePath = "UI/StatusEffectIconTable";

    public List<Entry> entries = new List<Entry>();

    private Dictionary<StatusEffectIconType, Sprite> _iconsByType;

    [Serializable]
    public sealed class Entry
    {
        public StatusEffectIconType type;
        public Sprite icon;
    }

    public static StatusEffectIconTable Resolve(StatusEffectIconTable serialized)
    {
        return serialized != null
            ? serialized
            : Resources.Load<StatusEffectIconTable>(ResourcePath);
    }

    public bool TryGetIcon(StatusEffectIconType type, out Sprite icon)
    {
        EnsureCache();
        return _iconsByType.TryGetValue(type, out icon) && icon != null;
    }

    private void EnsureCache()
    {
        if (_iconsByType != null)
            return;

        _iconsByType = new Dictionary<StatusEffectIconType, Sprite>();
        if (entries == null)
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (entry == null || entry.icon == null || _iconsByType.ContainsKey(entry.type))
                continue;

            _iconsByType.Add(entry.type, entry.icon);
        }
    }

    private void OnValidate()
    {
        _iconsByType = null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (entries == null)
            return;

        HashSet<StatusEffectIconType> seen = new HashSet<StatusEffectIconType>();
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (entry == null)
            {
                Debug.LogWarning("[StatusEffectIconTable] Empty entry at index " + i + ".", this);
                continue;
            }

            if (!seen.Add(entry.type))
                Debug.LogWarning("[StatusEffectIconTable] Duplicate type: " + entry.type + ".", this);

            if (entry.icon == null)
                Debug.LogWarning("[StatusEffectIconTable] Missing icon for " + entry.type + ".", this);
        }
#endif
    }
}
