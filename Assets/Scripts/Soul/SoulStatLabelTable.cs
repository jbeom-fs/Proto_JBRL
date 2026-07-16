using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SoulStatLabelTable", menuName = "JBRogLike/Soul/Soul Stat Label Table")]
public sealed class SoulStatLabelTable : ScriptableObject
{
    [SerializeField] private List<SoulStatLabelEntry> entries = new List<SoulStatLabelEntry>();
    [SerializeField] private List<SoulFormLabelEntry> formNames = new List<SoulFormLabelEntry>();

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

    public string GetFormName(PlayerFormId form)
    {
        for (int i = 0; i < formNames.Count; i++)
        {
            if (formNames[i].form == form && !string.IsNullOrWhiteSpace(formNames[i].koreanName))
                return formNames[i].koreanName;
        }

        return form.ToString();
    }
}

[Serializable]
public struct SoulStatLabelEntry
{
    public SoulStatType stat;
    public string koreanName;
    [TextArea] public string description;
}

[Serializable]
public struct SoulFormLabelEntry
{
    public PlayerFormId form;
    public string koreanName;
}
