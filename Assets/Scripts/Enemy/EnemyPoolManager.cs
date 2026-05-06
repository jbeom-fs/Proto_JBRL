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

    [SerializeField] private PoolEntry[] entries;

    private readonly Dictionary<EnemyData, EnemyController> _prefabs = new();
    private readonly Dictionary<EnemyData, Queue<EnemyController>> _pools = new();
    private readonly Dictionary<EnemyController, EnemyData> _activeData = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildPools();
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
        enemy.gameObject.SetActive(false);
        return enemy;
    }

    private void Release(EnemyController enemy)
    {
        if (enemy == null) return;
        if (!_activeData.TryGetValue(enemy, out var data)) return;

        _activeData.Remove(enemy);
        enemy.OnDeathFinished -= Release;
        enemy.transform.SetParent(transform);

        if (!_pools.TryGetValue(data, out var queue))
        {
            queue = new Queue<EnemyController>();
            _pools.Add(data, queue);
        }

        queue.Enqueue(enemy);
    }
}
