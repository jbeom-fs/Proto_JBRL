using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BossEncounterTable", menuName = "JBRogLike/Boss/Encounter Table")]
public sealed class BossEncounterTable : ScriptableObject
{
    [SerializeField] private List<BossEncounterEntry> entries = new();

    public IReadOnlyList<BossEncounterEntry> Entries => entries;

    public bool TryGetBoss(int floor, out BossEncounterEntry entry)
    {
        entry = null;
        if (entries == null)
            return false;

        for (int i = 0; i < entries.Count; i++)
        {
            BossEncounterEntry candidate = entries[i];
            if (candidate == null || candidate.Floor != floor)
                continue;

            entry = candidate;
            return true;
        }

        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (entries == null)
            return;

        var seenFloors = new HashSet<int>();
        for (int i = 0; i < entries.Count; i++)
        {
            BossEncounterEntry entry = entries[i];
            if (entry == null)
            {
                Debug.LogWarning("[BossEncounterTable] Empty entry at index " + i + ".", this);
                continue;
            }

            if (!seenFloors.Add(entry.Floor))
                Debug.LogWarning("[BossEncounterTable] Duplicate floor: " + entry.Floor + ".", this);
        }
    }
#endif
}

[Serializable]
public sealed class BossEncounterEntry
{
    [SerializeField] private int floor;
    [SerializeField] private EnemyData boss;
    [SerializeField, TeleportDestinationId] private string bossAreaDestinationId;
    [SerializeField, Tooltip("Matches WalkabilityArea.Id and TilemapMinimapSource.LocationId for fixed boss areas.")]
    private string areaId;
    [SerializeField] private bool isFinal;
    // TODO: Add shared map reference when boss area asset type is decided.

    public int Floor => floor;
    public EnemyData Boss => boss;
    public string BossAreaDestinationId => bossAreaDestinationId;
    public string AreaId => areaId;
    public bool IsFinal => isFinal;
}
