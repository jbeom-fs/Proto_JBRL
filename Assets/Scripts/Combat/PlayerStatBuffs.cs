using System.Collections.Generic;
using UnityEngine;

public enum BuffStatType
{
    Attack
}

public sealed class PlayerStatBuffs
{
    private sealed class Entry
    {
        public object SourceKey;
        public BuffStatType Stat;
        public float Value;
        public float Remaining;
        public bool Infinite;
        public Sprite Icon;
    }

    public static readonly object ConsoleSourceKey = new object();

    private readonly List<Entry> _entries = new List<Entry>(4);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool _warnedNullSourceKey;
#endif

    public void Grant(
        object sourceKey,
        BuffStatType stat,
        float value,
        float duration,
        Sprite icon)
    {
        if (value <= 0f)
            return;

        if (sourceKey == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_warnedNullSourceKey)
            {
                _warnedNullSourceKey = true;
                Debug.LogWarning("[PlayerStatBuffs] Null source key ignored.");
            }
#endif
            return;
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            Entry entry = _entries[i];
            if (!ReferenceEquals(entry.SourceKey, sourceKey) || entry.Stat != stat)
                continue;

            entry.Value = Mathf.Max(entry.Value, value);
            entry.Infinite = duration <= 0f;
            entry.Remaining = entry.Infinite ? 0f : duration;
            entry.Icon = icon;
            return;
        }

        _entries.Add(new Entry
        {
            SourceKey = sourceKey,
            Stat = stat,
            Value = value,
            Infinite = duration <= 0f,
            Remaining = duration <= 0f ? 0f : duration,
            Icon = icon
        });
    }

    public float GetBonus(BuffStatType stat)
    {
        float total = 0f;
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry entry = _entries[i];
            if (entry.Stat == stat)
                total += entry.Value;
        }

        return total;
    }

    public void Tick(float dt)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry entry = _entries[i];
            if (entry.Infinite)
                continue;

            entry.Remaining -= dt;
            if (entry.Remaining <= 0f)
                _entries.RemoveAt(i);
        }
    }

    public void Clear()
    {
        _entries.Clear();
    }
}
