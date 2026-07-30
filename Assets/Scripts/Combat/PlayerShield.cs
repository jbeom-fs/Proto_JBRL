using System.Collections.Generic;
using UnityEngine;

public enum ShieldSource
{
    Relic,
    PassiveEngraving,
    LifestealConversion,
    Console
}

public sealed class PlayerShield
{
    private sealed class Entry
    {
        public ShieldSource Source;
        public int Amount;
        public float Remaining;
        public bool Infinite;
    }

    private readonly List<Entry> _entries = new List<Entry>(4);

    public int CurrentAmount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _entries.Count; i++)
                total += _entries[i].Amount;

            return total;
        }
    }

    public bool IsActive => _entries.Count > 0;
    public event System.Action OnChanged;

    public void Grant(ShieldSource source, int amount, float duration)
    {
        if (amount <= 0)
            return;

        Entry entry = GetOrCreateEntry(source);
        entry.Amount = Mathf.Max(entry.Amount, amount);
        RefillDuration(entry, duration);
        OnChanged?.Invoke();
    }

    public void Add(ShieldSource source, int amount, float duration)
    {
        if (amount <= 0)
            return;

        Entry entry = GetOrCreateEntry(source);
        entry.Amount += amount;
        RefillDuration(entry, duration);
        OnChanged?.Invoke();
    }

    public int Absorb(int damage)
    {
        if (damage <= 0 || _entries.Count == 0)
            return damage;

        int remainingDamage = damage;
        while (remainingDamage > 0 && _entries.Count > 0)
        {
            int entryIndex = FindNextAbsorbEntryIndex();
            Entry entry = _entries[entryIndex];
            int absorbed = Mathf.Min(entry.Amount, remainingDamage);
            entry.Amount -= absorbed;
            remainingDamage -= absorbed;

            if (entry.Amount <= 0)
                _entries.RemoveAt(entryIndex);
        }

        OnChanged?.Invoke();
        return remainingDamage;
    }

    public void Tick(float dt)
    {
        if (_entries.Count == 0)
            return;

        bool changed = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry entry = _entries[i];
            if (entry.Infinite)
                continue;

            entry.Remaining -= dt;
            if (entry.Remaining > 0f)
                continue;

            _entries.RemoveAt(i);
            changed = true;
        }

        if (changed)
            OnChanged?.Invoke();
    }

    public void Clear()
    {
        if (_entries.Count == 0)
            return;

        _entries.Clear();
        OnChanged?.Invoke();
    }

    private Entry GetOrCreateEntry(ShieldSource source)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Source == source)
                return _entries[i];
        }

        var entry = new Entry
        {
            Source = source
        };
        _entries.Add(entry);
        return entry;
    }

    private static void RefillDuration(Entry entry, float duration)
    {
        entry.Infinite = duration <= 0f;
        entry.Remaining = entry.Infinite ? 0f : duration;
    }

    private int FindNextAbsorbEntryIndex()
    {
        int bestIndex = 0;
        for (int i = 1; i < _entries.Count; i++)
        {
            Entry candidate = _entries[i];
            Entry best = _entries[bestIndex];
            if (best.Infinite && !candidate.Infinite)
            {
                bestIndex = i;
                continue;
            }

            if (!candidate.Infinite &&
                !best.Infinite &&
                candidate.Remaining < best.Remaining)
            {
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
