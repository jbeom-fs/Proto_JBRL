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

    private static readonly List<int> s_CandidateIndexBuffer = new(4);
    private static float[] s_CandidateWeightBuffer = Array.Empty<float>();

    public IReadOnlyList<BossEncounterEntry> Entries => entries;

    public bool TryGetBoss(int floor, System.Random rng, out BossEncounterEntry entry)
    {
        entry = null;
        if (rng == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[BossEncounterTable] Boss selection RNG is null for floor " + floor + ".", this);
#endif
            return false;
        }

        if (entries == null)
            return false;

        s_CandidateIndexBuffer.Clear();
        for (int i = 0; i < entries.Count; i++)
        {
            BossEncounterEntry candidate = entries[i];
            if (candidate == null || candidate.Floor != floor || !(candidate.Weight > 0f))
                continue;

            s_CandidateIndexBuffer.Add(i);
        }

        int candidateCount = s_CandidateIndexBuffer.Count;
        if (candidateCount == 0)
            return false;

        if (candidateCount == 1)
        {
            entry = entries[s_CandidateIndexBuffer[0]];
            return true;
        }

        if (s_CandidateWeightBuffer.Length < candidateCount)
            Array.Resize(ref s_CandidateWeightBuffer, candidateCount);

        for (int i = 0; i < candidateCount; i++)
            s_CandidateWeightBuffer[i] = entries[s_CandidateIndexBuffer[i]].Weight;
        if (candidateCount < s_CandidateWeightBuffer.Length)
            Array.Clear(s_CandidateWeightBuffer, candidateCount, s_CandidateWeightBuffer.Length - candidateCount);

        if (!DropQueryResolver.TryChooseWeightedIndex(s_CandidateWeightBuffer, rng, out int selectedIndex) ||
            selectedIndex < 0 || selectedIndex >= candidateCount)
        {
            return false;
        }

        entry = entries[s_CandidateIndexBuffer[selectedIndex]];
        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (entries == null)
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            BossEncounterEntry entry = entries[i];
            if (entry == null)
            {
                Debug.LogWarning("[BossEncounterTable] Empty entry at index " + i + ".", this);
                continue;
            }

            ValidatePhases(entry);
        }

        for (int i = 0; i < entries.Count; i++)
        {
            BossEncounterEntry first = entries[i];
            if (first == null)
                continue;

            bool isFirstForFloor = true;
            for (int j = 0; j < i; j++)
            {
                BossEncounterEntry previous = entries[j];
                if (previous != null && previous.Floor == first.Floor)
                {
                    isFirstForFloor = false;
                    break;
                }
            }

            if (!isFirstForFloor)
                continue;

            bool hasPositiveWeight = first.Weight > 0f;
            bool hasMismatchedFinalFlag = false;
            for (int j = i + 1; j < entries.Count; j++)
            {
                BossEncounterEntry candidate = entries[j];
                if (candidate == null || candidate.Floor != first.Floor)
                    continue;

                hasPositiveWeight |= candidate.Weight > 0f;
                hasMismatchedFinalFlag |= candidate.IsFinal != first.IsFinal;
            }

            if (hasMismatchedFinalFlag)
            {
                Debug.LogWarning(
                    "[BossEncounterTable] Floor " + first.Floor + " candidates have mismatched isFinal values.",
                    this);
            }

            if (!hasPositiveWeight)
            {
                Debug.LogWarning(
                    "[BossEncounterTable] Floor " + first.Floor + " has no positive-weight boss candidates.",
                    this);
            }
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
    [SerializeField, Min(0f)] private float weight = 1f;
    [SerializeField] private EnemyData boss;
    [SerializeField, TeleportDestinationId] private string bossAreaDestinationId;
    [SerializeField] private bool isFinal;
    [SerializeField] private List<BossPhase> phases = new();
    // TODO: Add shared map reference when boss area asset type is decided.

    public int Floor => floor;
    public float Weight => weight;
    public EnemyData Boss => boss;
    public string BossAreaDestinationId => bossAreaDestinationId;
    public bool IsFinal => isFinal;
    public IReadOnlyList<BossPhase> Phases => phases;
}
