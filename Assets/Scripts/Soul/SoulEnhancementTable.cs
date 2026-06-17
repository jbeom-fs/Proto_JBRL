using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SoulEnhancementTable", menuName = "JBRogLike/Soul/Soul Enhancement Table")]
public sealed class SoulEnhancementTable : ScriptableObject
{
    [SerializeField] private List<FormEnhancementEntry> forms = new List<FormEnhancementEntry>();

    private readonly Dictionary<int, SoulStatGrowth> _growthsByKey = new Dictionary<int, SoulStatGrowth>();
    private bool _cacheBuilt;

    public bool TryGetPerLevel(PlayerFormId form, SoulStatType stat, out float perLevel)
    {
        EnsureCache();
        if (_growthsByKey.TryGetValue(MakeKey(form, stat), out SoulStatGrowth growth))
        {
            perLevel = growth.perLevel;
            return true;
        }

        perLevel = 0f;
        return false;
    }

    public bool TryGetMaxLevel(PlayerFormId form, SoulStatType stat, out int maxLevel)
    {
        EnsureCache();
        if (_growthsByKey.TryGetValue(MakeKey(form, stat), out SoulStatGrowth growth))
        {
            maxLevel = Mathf.Max(0, growth.maxLevel);
            return true;
        }

        maxLevel = 0;
        return false;
    }

    public void GetGrowths(PlayerFormId form, List<SoulStatGrowth> output)
    {
        if (output == null)
            return;

        EnsureCache();
        if (forms == null)
            return;

        for (int i = 0; i < forms.Count; i++)
        {
            FormEnhancementEntry entry = forms[i];
            if (entry == null || entry.form != form || entry.growths == null)
                continue;

            for (int j = 0; j < entry.growths.Count; j++)
            {
                SoulStatGrowth growth = entry.growths[j];
                if (_growthsByKey.ContainsKey(MakeKey(form, growth.stat)))
                    output.Add(growth);
            }

            return;
        }
    }

    private void OnEnable()
    {
        RebuildCache();
    }

    private void OnValidate()
    {
        RebuildCache();
        ValidateForms();
    }

    private void EnsureCache()
    {
        if (!_cacheBuilt)
            RebuildCache();
    }

    private void RebuildCache()
    {
        _growthsByKey.Clear();
        _cacheBuilt = true;

        if (forms == null)
            return;

        for (int i = 0; i < forms.Count; i++)
        {
            FormEnhancementEntry entry = forms[i];
            if (entry == null || entry.growths == null)
                continue;

            for (int j = 0; j < entry.growths.Count; j++)
            {
                SoulStatGrowth growth = entry.growths[j];
                int key = MakeKey(entry.form, growth.stat);
                if (_growthsByKey.ContainsKey(key))
                    continue;

                _growthsByKey.Add(key, growth);
            }
        }
    }

    private void ValidateForms()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (forms == null)
            return;

        var seen = new HashSet<int>();
        for (int i = 0; i < forms.Count; i++)
        {
            FormEnhancementEntry entry = forms[i];
            if (entry == null)
            {
                Debug.LogWarning("[SoulEnhancementTable] Empty form entry at index " + i + ".", this);
                continue;
            }

            if (entry.growths == null)
                continue;

            for (int j = 0; j < entry.growths.Count; j++)
            {
                SoulStatGrowth growth = entry.growths[j];
                int key = MakeKey(entry.form, growth.stat);
                if (!seen.Add(key))
                    Debug.LogWarning("[SoulEnhancementTable] Duplicate growth: " + entry.form + "/" + growth.stat + ".", this);

                if (growth.maxLevel < 0)
                    Debug.LogWarning("[SoulEnhancementTable] Negative maxLevel: " + entry.form + "/" + growth.stat + ".", this);
            }
        }
#endif
    }

    private static int MakeKey(PlayerFormId form, SoulStatType stat)
    {
        return ((int)form << 16) ^ (int)stat;
    }
}

[Serializable]
public struct SoulStatGrowth
{
    public SoulStatType stat;
    public float perLevel;
    [Min(0)] public int maxLevel;
}

[Serializable]
public sealed class FormEnhancementEntry
{
    public PlayerFormId form;
    public List<SoulStatGrowth> growths = new List<SoulStatGrowth>();
}
