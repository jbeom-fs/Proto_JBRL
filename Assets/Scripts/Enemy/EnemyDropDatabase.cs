using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public enum DropFormScope
{
    CurrentForm,
    Any,
    Specific
}

public enum DropRank
{
    Normal,
    Elite,
    Boss
}

[Serializable]
public struct EnemyDropEntry
{
    public string itemCode;
    [Min(1)] public int minAmount;
    [Min(1)] public int maxAmount;
    [Range(0f, 1f)] public float chance;
}

[Serializable]
public struct EnemyDropChoice
{
    public string itemCode;
    [Min(1)] public int minAmount;
    [Min(1)] public int maxAmount;
    [Min(0f)] public float weight;
}

[Serializable]
public sealed class EnemyDropChoiceGroup
{
    [Range(0f, 1f)] public float chance;
    public List<EnemyDropChoice> choices = new List<EnemyDropChoice>();
}

[Serializable]
public struct EnemyDropQuery
{
    [Range(0f, 1f)] public float chance;
    public DropQueryCategory itemType;
    public DropFormScope formScope;
    public PlayerFormId specificForm;
    [Min(0f)] public float tierWeight0;
    [Min(0f)] public float tierWeight1;
    [Min(0f)] public float tierWeight2;
    [Tooltip("드랍 횟수 가중치. index i = (i+1)회 뽑기의 가중치. 비었거나 총합 0이면 1회.")]
    public float[] rollCountWeights;
}

[Serializable]
public sealed class EnemyDropGroup
{
    public EnemyData enemy;
    public List<EnemyDropEntry> drops = new List<EnemyDropEntry>();
    public List<EnemyDropChoiceGroup> choiceGroups = new List<EnemyDropChoiceGroup>();
    public List<EnemyDropQuery> queries = new List<EnemyDropQuery>();
}

[Serializable]
public sealed class RankDropGroup
{
    public List<EnemyDropEntry> drops = new List<EnemyDropEntry>();
    public List<EnemyDropChoiceGroup> choiceGroups = new List<EnemyDropChoiceGroup>();
    public List<EnemyDropQuery> queries = new List<EnemyDropQuery>();
}

[CreateAssetMenu(fileName = "EnemyDropDatabase", menuName = "JBRogLike/Enemy/Enemy Drop Database")]
public sealed class EnemyDropDatabase : ScriptableObject
{
    [SerializeField] private List<EnemyDropGroup> groups = new List<EnemyDropGroup>();

    [Header("Rank Shared Drops")]
    [SerializeField] private RankDropGroup normalRankDrops = new RankDropGroup();
    [SerializeField] private RankDropGroup eliteRankDrops = new RankDropGroup();
    [SerializeField] private RankDropGroup bossRankDrops = new RankDropGroup();

    [Tooltip("Runtime query candidates and editor validation item source.")]
    [FormerlySerializedAs("itemDatabaseForValidation")]
    [SerializeField] private ItemDatabase itemDatabase;

    private readonly Dictionary<EnemyData, EnemyDropGroup> _byEnemy = new Dictionary<EnemyData, EnemyDropGroup>();
    private bool _cacheBuilt;

    public ItemDatabase ItemDatabase => itemDatabase;

    public EnemyDropGroup GetDropGroup(EnemyData enemy)
    {
        if (enemy == null)
            return null;

        EnsureCache();
        return _byEnemy.TryGetValue(enemy, out EnemyDropGroup group) ? group : null;
    }

    public RankDropGroup GetRankGroup(DropRank rank)
    {
        switch (rank)
        {
            case DropRank.Elite: return eliteRankDrops;
            case DropRank.Boss: return bossRankDrops;
            default: return normalRankDrops;
        }
    }

    private void OnEnable()
    {
        RebuildCache();
    }

    private void OnValidate()
    {
        RebuildCache();
        ValidateGroups();
    }

    private void EnsureCache()
    {
        if (!_cacheBuilt)
            RebuildCache();
    }

    private void RebuildCache()
    {
        _byEnemy.Clear();
        _cacheBuilt = true;

        if (groups == null)
            return;

        for (int i = 0; i < groups.Count; i++)
        {
            EnemyDropGroup group = groups[i];
            if (group == null || group.enemy == null)
                continue;

            if (_byEnemy.ContainsKey(group.enemy))
                continue;

            _byEnemy.Add(group.enemy, group);
        }
    }

    private void ValidateGroups()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        ValidateDropLists(
            normalRankDrops?.drops,
            normalRankDrops?.choiceGroups,
            normalRankDrops?.queries,
            "rank Normal");
        ValidateDropLists(
            eliteRankDrops?.drops,
            eliteRankDrops?.choiceGroups,
            eliteRankDrops?.queries,
            "rank Elite");
        ValidateDropLists(
            bossRankDrops?.drops,
            bossRankDrops?.choiceGroups,
            bossRankDrops?.queries,
            "rank Boss");

        if (groups == null)
            return;

        var seen = new HashSet<EnemyData>();
        for (int i = 0; i < groups.Count; i++)
        {
            EnemyDropGroup group = groups[i];
            if (group == null)
            {
                Debug.LogWarning("[EnemyDropDatabase] Empty group at index " + i + ".", this);
                continue;
            }

            if (group.enemy == null)
            {
                Debug.LogWarning("[EnemyDropDatabase] Empty enemy at index " + i + ".", this);
                continue;
            }

            if (!seen.Add(group.enemy))
                Debug.LogWarning("[EnemyDropDatabase] Duplicate enemy: " + group.enemy.name + ".", this);

            ValidateDropLists(
                group.drops,
                group.choiceGroups,
                group.queries,
                "enemy " + group.enemy.name);
        }
#endif
    }

    private void ValidateDropLists(
        IReadOnlyList<EnemyDropEntry> drops,
        IReadOnlyList<EnemyDropChoiceGroup> choiceGroups,
        IReadOnlyList<EnemyDropQuery> queries,
        string context)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (drops != null)
        {
            for (int i = 0; i < drops.Count; i++)
            {
                EnemyDropEntry drop = drops[i];
                WarnIfEngravingLikeAmountExceedsOne(drop.itemCode, drop.minAmount, drop.maxAmount);
            }
        }

        if (choiceGroups != null)
        {
            for (int i = 0; i < choiceGroups.Count; i++)
            {
                EnemyDropChoiceGroup choiceGroup = choiceGroups[i];
                if (choiceGroup == null || choiceGroup.choices == null)
                    continue;

                for (int j = 0; j < choiceGroup.choices.Count; j++)
                {
                    EnemyDropChoice choice = choiceGroup.choices[j];
                    WarnIfEngravingLikeAmountExceedsOne(choice.itemCode, choice.minAmount, choice.maxAmount);
                }
            }
        }

        if (queries != null)
        {
            for (int i = 0; i < queries.Count; i++)
            {
                EnemyDropQuery query = queries[i];
                if (query.rollCountWeights != null && query.rollCountWeights.Length > 10)
                {
                    Debug.LogWarning(
                        "[EnemyDropDatabase] Query rollCountWeights length is " +
                        query.rollCountWeights.Length + " (>10): " + context +
                        " query " + i + ". Check unintended large drop counts.", this);
                }
            }
        }
#endif
    }

    private void WarnIfEngravingLikeAmountExceedsOne(string itemCode, int minAmount, int maxAmount)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (itemDatabase == null || string.IsNullOrWhiteSpace(itemCode))
            return;

        if (!itemDatabase.TryGetItem(itemCode, out ItemData item) || item == null)
            return;

        // 런타임 roller와 동일하게 실효 최대치 = Max(min, max). min>max 오작성도 잡는다.
        int effectiveMax = Mathf.Max(Mathf.Max(1, minAmount), maxAmount);
        if ((item.ItemType == ItemType.Engraving ||
             item.ItemType == ItemType.PassiveEngraving) &&
            effectiveMax > 1)
        {
            Debug.LogWarning(
                "[EnemyDropDatabase] " + item.ItemType + " drop '" + itemCode +
                "' rolls up to " + effectiveMax + " (>1). Engraving-like items are always quantity 1; " +
                "extra copies are silently dropped on pickup. Set min=max=1.", this);
        }
#endif
    }
}
