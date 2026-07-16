using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SoulStatLabelTable", menuName = "JBRogLike/Soul/Soul Stat Label Table")]
public sealed class SoulStatLabelTable : ScriptableObject
{
    [SerializeField] private List<SoulStatLabelEntry> entries = new List<SoulStatLabelEntry>();

    public string GetStatName(SoulStatType stat)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].stat == stat && !string.IsNullOrWhiteSpace(entries[i].koreanName))
                return entries[i].koreanName;
        }

        return stat.ToString();
    }

    public string GetDescription(SoulStatType stat)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].stat == stat)
                return entries[i].description ?? string.Empty;
        }

        return string.Empty;
    }

}

[Serializable]
public struct SoulStatLabelEntry
{
    public SoulStatType stat;
    public string koreanName;
    [TextArea] public string description;
}
