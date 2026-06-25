using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerSoulEnhancements : MonoBehaviour
{
    [SerializeField] private List<SoulEnhancementLevel> levels = new List<SoulEnhancementLevel>();

    private readonly Dictionary<int, SoulEnhancementLevel> _levelsByKey = new Dictionary<int, SoulEnhancementLevel>();
    private bool _cacheBuilt;

    public event Action OnChanged;

    public int GetLevel(PlayerFormId form, SoulStatType stat)
    {
        EnsureCache();
        return _levelsByKey.TryGetValue(MakeKey(form, stat), out SoulEnhancementLevel entry)
            ? Mathf.Max(0, entry.level)
            : 0;
    }

    public void GetLevels(List<SoulEnhancementLevelSnapshot> output)
    {
        if (output == null)
            return;

        EnsureCache();

        if (levels == null)
            return;

        for (int i = 0; i < levels.Count; i++)
        {
            SoulEnhancementLevel entry = levels[i];
            if (entry == null)
                continue;

            int level = Mathf.Max(0, entry.level);
            if (level <= 0)
                continue;

            output.Add(new SoulEnhancementLevelSnapshot(entry.form, entry.stat, level));
        }
    }

    public void AddLevel(PlayerFormId form, SoulStatType stat, int delta)
    {
        if (delta == 0)
            return;

        SetLevel(form, stat, GetLevel(form, stat) + delta);
    }

    public void SetLevel(PlayerFormId form, SoulStatType stat, int level)
    {
        EnsureCache();

        int clampedLevel = Mathf.Max(0, level);
        int key = MakeKey(form, stat);
        if (_levelsByKey.TryGetValue(key, out SoulEnhancementLevel entry))
        {
            int currentLevel = Mathf.Max(0, entry.level);
            if (currentLevel == clampedLevel)
                return;

            if (clampedLevel == 0)
            {
                levels.Remove(entry);
                _levelsByKey.Remove(key);
            }
            else
            {
                entry.level = clampedLevel;
            }

            OnChanged?.Invoke();
            return;
        }

        if (clampedLevel == 0)
            return;

        if (levels == null)
            levels = new List<SoulEnhancementLevel>();

        entry = new SoulEnhancementLevel
        {
            form = form,
            stat = stat,
            level = clampedLevel
        };
        levels.Add(entry);
        _levelsByKey.Add(key, entry);
        OnChanged?.Invoke();
    }

    public void Clear()
    {
        EnsureCache();
        if ((levels == null || levels.Count == 0) && _levelsByKey.Count == 0)
            return;

        if (levels != null)
            levels.Clear();

        _levelsByKey.Clear();
        _cacheBuilt = true;
        OnChanged?.Invoke();
    }

    private void OnEnable()
    {
        RebuildCache();
    }

    private void OnValidate()
    {
        if (levels == null)
            return;

        for (int i = 0; i < levels.Count; i++)
        {
            SoulEnhancementLevel entry = levels[i];
            if (entry != null && entry.level < 0)
                entry.level = 0;
        }

        RebuildCache();
    }

    private void EnsureCache()
    {
        if (!_cacheBuilt)
            RebuildCache();
    }

    private void RebuildCache()
    {
        _levelsByKey.Clear();
        _cacheBuilt = true;

        if (levels == null)
            return;

        for (int i = 0; i < levels.Count; i++)
        {
            SoulEnhancementLevel entry = levels[i];
            if (entry == null)
                continue;

            if (entry.level < 0)
                entry.level = 0;

            if (entry.level <= 0)
                continue;

            int key = MakeKey(entry.form, entry.stat);
            if (_levelsByKey.ContainsKey(key))
                continue;

            _levelsByKey.Add(key, entry);
        }
    }

    private static int MakeKey(PlayerFormId form, SoulStatType stat)
    {
        return ((int)form << 16) ^ (int)stat;
    }

    [Serializable]
    private sealed class SoulEnhancementLevel
    {
        public PlayerFormId form;
        public SoulStatType stat;
        [Min(0)] public int level;
    }
}

public readonly struct SoulEnhancementLevelSnapshot
{
    public SoulEnhancementLevelSnapshot(PlayerFormId form, SoulStatType stat, int level)
    {
        Form = form;
        Stat = stat;
        Level = level;
    }

    public PlayerFormId Form { get; }
    public SoulStatType Stat { get; }
    public int Level { get; }
}
