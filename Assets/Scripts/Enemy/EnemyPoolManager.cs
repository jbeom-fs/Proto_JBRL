using System.Collections.Generic;
using UnityEngine;

public class EnemyPoolManager : MonoBehaviour
{
    [System.Serializable]
    public class PoolEntry
    {
        public EnemyData data;
        public EnemyController prefab;
        [Min(0)] public int preloadCount = 0;
    }

    public static EnemyPoolManager Instance { get; private set; }

    [SerializeField] private EnemyAilmentProfileDatabase ailmentProfiles;
    [SerializeField] private PoolEntry[] entries;

    private readonly Dictionary<EnemyData, EnemyController> _prefabs = new();
    private readonly Dictionary<EnemyData, Queue<EnemyController>> _pools = new();
    private readonly Dictionary<EnemyController, EnemyData> _activeData = new();

    public bool HasActiveEnemies => _activeData.Count > 0;

    public static void ReleaseAllActiveEnemiesForLocationChange()
    {
        Instance?.ReleaseAllActive();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ValidateAilmentProfiles();
        BuildPools();
    }

    private void ValidateAilmentProfiles()
    {
        if (ailmentProfiles == null)
        {
            Debug.LogError(
                "[EnemyPoolManager] EnemyAilmentProfileDatabase is not assigned. Enemy ailments are disabled.",
                this);
            return;
        }

        if (!ailmentProfiles.TryValidate(out string error))
        {
            Debug.LogError(
                "[EnemyPoolManager] Invalid EnemyAilmentProfileDatabase: " + error,
                ailmentProfiles);
        }
    }

    private void BuildPools()
    {
        _prefabs.Clear();
        _pools.Clear();

        if (entries == null) return;

        foreach (var entry in entries)
        {
            if (entry == null || entry.data == null || entry.prefab == null) continue;

            _prefabs[entry.data] = entry.prefab;

            if (!_pools.TryGetValue(entry.data, out var queue))
            {
                queue = new Queue<EnemyController>();
                _pools.Add(entry.data, queue);
            }

            for (int i = 0; i < entry.preloadCount; i++)
                queue.Enqueue(Create(entry.data));
        }
    }

    public EnemyController Request(EnemyData data)
    {
        if (data == null) return null;

        if (!_pools.TryGetValue(data, out var queue))
        {
            queue = new Queue<EnemyController>();
            _pools.Add(data, queue);
        }

        EnemyController enemy = queue.Count > 0 ? queue.Dequeue() : Create(data);
        if (enemy == null) return null;

        _activeData[enemy] = data;
        enemy.ClearEliteKeyHolder();
        enemy.ClearBossEncounterFlag();
        enemy.ClearDropInventory();
        enemy.OnDeathFinished -= Release;
        enemy.OnDeathFinished += Release;
        if (enemy.TryGetComponent<EnemyBrain>(out var brain))
            brain.ResetRuntimeState();
        return enemy;
    }

    public void GetRegisteredEnemyData(List<EnemyData> results)
    {
        if (results == null) return;

        results.Clear();
        foreach (var data in _prefabs.Keys)
            if (data != null)
                results.Add(data);
    }

    private EnemyController Create(EnemyData data)
    {
        if (!_prefabs.TryGetValue(data, out var prefab) || prefab == null)
        {
            Debug.LogWarning($"[EnemyPoolManager] Pool prefab is missing for {data?.enemyName}");
            return null;
        }

        EnemyController enemy = Instantiate(prefab, transform);
        enemy.ConfigureAilments(ailmentProfiles);
        enemy.gameObject.SetActive(false);
        return enemy;
    }

    private void Release(EnemyController enemy)
    {
        if (enemy == null) return;
        if (!_activeData.TryGetValue(enemy, out var data)) return;

        _activeData.Remove(enemy);
        enemy.OnDeathFinished -= Release;
        enemy.ClearEliteKeyHolder();
        enemy.ClearBossEncounterFlag();
        enemy.ClearDropInventory();
        enemy.transform.SetParent(transform);

        if (!_pools.TryGetValue(data, out var queue))
        {
            queue = new Queue<EnemyController>();
            _pools.Add(data, queue);
        }

        queue.Enqueue(enemy);
    }

    private void ReleaseAllActive()
    {
        if (_activeData.Count == 0)
            return;

        List<EnemyController> activeEnemies = new List<EnemyController>(_activeData.Keys);
        for (int i = 0; i < activeEnemies.Count; i++)
        {
            EnemyController enemy = activeEnemies[i];
            if (enemy == null)
                continue;

            if (!_activeData.TryGetValue(enemy, out EnemyData data))
                continue;

            _activeData.Remove(enemy);
            enemy.OnDeathFinished -= Release;
            enemy.ClearEliteKeyHolder();
            enemy.ClearBossEncounterFlag();
            enemy.ClearDropInventory();
            enemy.transform.SetParent(transform);
            enemy.gameObject.SetActive(false);

            if (!_pools.TryGetValue(data, out Queue<EnemyController> queue))
            {
                queue = new Queue<EnemyController>();
                _pools.Add(data, queue);
            }

            queue.Enqueue(enemy);
        }
    }
}
