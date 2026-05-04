using System.Collections.Generic;
using UnityEngine;

public class ProjectilePool : MonoBehaviour
{
    [System.Serializable]
    public class ProjectilePoolEntry
    {
        public ProjectileController prefab;
        [Min(0)] public int prewarmCount = 32;
    }

    private static ProjectilePool _instance;

    [SerializeField] private ProjectilePoolEntry[] prewarmEntries;

    private readonly Dictionary<GameObject, Stack<ProjectileController>> _poolByPrefab = new();
    private readonly Dictionary<ProjectileController, GameObject> _prefabByProjectile = new();
    private readonly Dictionary<GameObject, int> _createdCountByPrefab = new();
    private int _activeCount;

    public static ProjectilePool Instance
    {
        get
        {
            if (_instance != null)
                return _instance;

            GameObject poolObject = new GameObject(nameof(ProjectilePool));
            _instance = poolObject.AddComponent<ProjectilePool>();
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        PrewarmConfiguredEntries();
    }

    public ProjectileController Get(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
            return null;

        Stack<ProjectileController> pool = GetOrCreatePool(prefab);

        ProjectileController projectile = null;
        while (pool.Count > 0 && projectile == null)
            projectile = pool.Pop();

        bool instantiated = false;
        if (projectile == null)
        {
            projectile = CreateProjectile(prefab, position, rotation, true);
            instantiated = true;
        }
        else
        {
            Transform projectileTransform = projectile.transform;
            projectileTransform.SetPositionAndRotation(position, rotation);
            projectile.gameObject.SetActive(true);
        }

        projectile.SetReleaseAction(Return);
        _prefabByProjectile[projectile] = prefab;
        _activeCount++;
        RuntimePerfTraceLogger.RecordPoolGet(prefab.name, instantiated, _activeCount, pool.Count);
        return projectile;
    }

    public void Prewarm(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0)
            return;

        Stack<ProjectileController> pool = GetOrCreatePool(prefab);
        _createdCountByPrefab.TryGetValue(prefab, out int createdCount);

        while (createdCount < count)
        {
            ProjectileController projectile = CreateProjectile(prefab, Vector3.zero, Quaternion.identity, false);
            if (projectile == null)
                break;

            pool.Push(projectile);
            createdCount++;
        }
    }

    public void Return(ProjectileController projectile, ProjectileReleaseReason reason)
    {
        if (projectile == null)
            return;

        if (!_prefabByProjectile.TryGetValue(projectile, out GameObject prefab) || prefab == null)
        {
            Destroy(projectile.gameObject);
            return;
        }

        if (!_poolByPrefab.TryGetValue(prefab, out Stack<ProjectileController> pool))
        {
            pool = new Stack<ProjectileController>();
            _poolByPrefab.Add(prefab, pool);
        }

        projectile.transform.SetParent(transform);
        projectile.gameObject.SetActive(false);
        pool.Push(projectile);
        if (_activeCount > 0)
            _activeCount--;
        RuntimePerfTraceLogger.RecordPoolReturn(reason, _activeCount, pool.Count);
    }

    private Stack<ProjectileController> GetOrCreatePool(GameObject prefab)
    {
        if (!_poolByPrefab.TryGetValue(prefab, out Stack<ProjectileController> pool))
        {
            pool = new Stack<ProjectileController>();
            _poolByPrefab.Add(prefab, pool);
        }

        return pool;
    }

    private void PrewarmConfiguredEntries()
    {
        if (prewarmEntries == null || prewarmEntries.Length == 0)
            return;

        for (int i = 0; i < prewarmEntries.Length; i++)
        {
            ProjectilePoolEntry entry = prewarmEntries[i];
            if (entry == null || entry.prefab == null)
                continue;

            int requestedCount = Mathf.Max(0, entry.prewarmCount);
            if (requestedCount <= 0)
                continue;

            Prewarm(entry.prefab.gameObject, requestedCount);
        }
    }

    private ProjectileController CreateProjectile(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        bool active)
    {
        GameObject projectileObject = Instantiate(prefab, position, rotation, transform);
        ProjectileController projectile = projectileObject.GetComponent<ProjectileController>();
        if (projectile == null)
            projectile = projectileObject.AddComponent<ProjectileController>();

        projectile.SetReleaseAction(Return);
        _prefabByProjectile[projectile] = prefab;
        _createdCountByPrefab.TryGetValue(prefab, out int createdCount);
        _createdCountByPrefab[prefab] = createdCount + 1;

        projectileObject.SetActive(active);
        return projectile;
    }
}
