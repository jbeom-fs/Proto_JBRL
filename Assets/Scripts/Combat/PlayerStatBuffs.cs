using System;
using System.Collections.Generic;
using UnityEngine;

public enum BuffStatType
{
    Attack
}

[Serializable]
public struct BuffApplication
{
    public BuffStatType stat;
    [Min(0f)] public float value;
}

public readonly struct BuffEntrySnapshot
{
    public BuffEntrySnapshot(
        BuffStatType stat,
        float value,
        float remaining,
        float duration,
        bool infinite,
        Sprite icon)
    {
        Stat = stat;
        Value = value;
        Remaining = remaining;
        Duration = duration;
        Infinite = infinite;
        Icon = icon;
    }

    public BuffStatType Stat { get; }
    public float Value { get; }
    public float Remaining { get; }
    public float Duration { get; }
    public bool Infinite { get; }
    public Sprite Icon { get; }
}

public sealed class PlayerStatBuffs
{
    private sealed class Entry
    {
        public object SourceKey;
        public BuffStatType Stat;
        public float Value;
        public float Remaining;
        public float Duration;
        public bool Infinite;
        public Sprite Icon;
    }

    public static readonly object ConsoleSourceKey = new object();

    private readonly List<Entry> _entries = new List<Entry>(4);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool _warnedNullSourceKey;
#endif

    public int EntryCount => _entries.Count;

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
            entry.Duration = entry.Infinite ? 0f : duration;
            entry.Remaining = entry.Duration;
            entry.Icon = icon;
            return;
        }

        _entries.Add(new Entry
        {
            SourceKey = sourceKey,
            Stat = stat,
            Value = value,
            Infinite = duration <= 0f,
            Duration = duration <= 0f ? 0f : duration,
            Remaining = duration <= 0f ? 0f : duration,
            Icon = icon
        });
    }

    public bool TryGetEntry(int index, out BuffEntrySnapshot snapshot)
    {
        if (index < 0 || index >= _entries.Count)
        {
            snapshot = default;
            return false;
        }

        Entry entry = _entries[index];
        snapshot = new BuffEntrySnapshot(
            entry.Stat,
            entry.Value,
            entry.Remaining,
            entry.Duration,
            entry.Infinite,
            entry.Icon);
        return true;
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
