using System;
using System.Collections.Generic;
using UnityEngine;

public enum BossPhaseExit
{
    HpRatio,
    Depletion
}

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

            ValidatePhases(entry);
        }
    }

    private void ValidatePhases(BossEncounterEntry entry)
    {
        IReadOnlyList<BossPhase> phases = entry.Phases;
        if (phases == null || phases.Count == 0)
            return;

        bool hasPreviousHpRatio = false;
        float previousHpRatio = 0f;
        for (int i = 0; i < phases.Count; i++)
        {
            BossPhase phase = phases[i];
            if (phase == null)
            {
                Debug.LogWarning(
                    $"[BossEncounterTable] Floor {entry.Floor} has an empty phase at index {i}.",
                    this);
                continue;
            }

            if (phase.PatternSet == null)
            {
                Debug.LogWarning(
                    $"[BossEncounterTable] Floor {entry.Floor} phase {i} has no pattern set.",
                    this);
            }

            if (phase.Exit == BossPhaseExit.HpRatio &&
                (phase.ExitHpRatio < 0f || phase.ExitHpRatio > 1f))
            {
                Debug.LogWarning(
                    $"[BossEncounterTable] Floor {entry.Floor} phase {i} exit HP ratio must be between 0 and 1.",
                    this);
            }

            bool isLast = i == phases.Count - 1;
            if (!isLast)
            {
                if (phase.Exit == BossPhaseExit.Depletion)
                {
                    hasPreviousHpRatio = false;
                }
                else
                {
                    if (hasPreviousHpRatio && phase.ExitHpRatio > previousHpRatio)
                    {
                        Debug.LogWarning(
                            $"[BossEncounterTable] Floor {entry.Floor} phase {i} exit HP ratio increases within the same HP pool.",
                            this);
                    }

                    previousHpRatio = phase.ExitHpRatio;
                    hasPreviousHpRatio = true;
                }
            }

            if (i > 0 &&
                phase.MaxHpOverride > 0 &&
                phases[i - 1] != null &&
                phases[i - 1].Exit == BossPhaseExit.HpRatio)
            {
                Debug.LogWarning(
                    $"[BossEncounterTable] Floor {entry.Floor} phase {i} changes Max HP without a depletion refill.",
                    this);
            }

            if (isLast &&
                (phase.Exit != BossPhaseExit.HpRatio || !Mathf.Approximately(phase.ExitHpRatio, 0f)))
            {
                Debug.LogWarning(
                    $"[BossEncounterTable] Floor {entry.Floor} final phase exit settings are ignored.",
                    this);
            }
        }
    }
#endif
}

[Serializable]
public sealed class BossPhase
{
    [SerializeField] private EnemyPatternSet patternSet;
    [SerializeField] private BossPhaseExit exit;
    [SerializeField, Range(0f, 1f)] private float exitHpRatio;
    [SerializeField, Min(0)] private int maxHpOverride;

    public EnemyPatternSet PatternSet => patternSet;
    public BossPhaseExit Exit => exit;
    public float ExitHpRatio => exitHpRatio;
    public int MaxHpOverride => maxHpOverride;
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
    [SerializeField] private List<BossPhase> phases = new();
    // TODO: Add shared map reference when boss area asset type is decided.

    public int Floor => floor;
    public EnemyData Boss => boss;
    public string BossAreaDestinationId => bossAreaDestinationId;
    public string AreaId => areaId;
    public bool IsFinal => isFinal;
    public IReadOnlyList<BossPhase> Phases => phases;
}
