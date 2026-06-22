using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct EnemyDropEntry
{
    public string itemCode;
    [Min(1)] public int minAmount;
    [Min(1)] public int maxAmount;
    [Range(0f, 1f)] public float chance;
}

[Serializable]
public sealed class EnemyDropGroup
{
    public EnemyData enemy;
    public List<EnemyDropEntry> drops = new List<EnemyDropEntry>();
}

[CreateAssetMenu(fileName = "EnemyDropDatabase", menuName = "JBRogLike/Enemy/Enemy Drop Database")]
public sealed class EnemyDropDatabase : ScriptableObject
{
    [SerializeField] private List<EnemyDropGroup> groups = new List<EnemyDropGroup>();

    private static readonly EnemyDropEntry[] s_EmptyDrops = Array.Empty<EnemyDropEntry>();

    private readonly Dictionary<EnemyData, EnemyDropGroup> _byEnemy = new Dictionary<EnemyData, EnemyDropGroup>();
    private bool _cacheBuilt;

    public IReadOnlyList<EnemyDropEntry> GetDrops(EnemyData enemy)
    {
        if (enemy == null)
            return s_EmptyDrops;

        EnsureCache();
        return _byEnemy.TryGetValue(enemy, out EnemyDropGroup group) && group.drops != null
            ? group.drops
            : s_EmptyDrops;
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
        }
#endif
    }
}
