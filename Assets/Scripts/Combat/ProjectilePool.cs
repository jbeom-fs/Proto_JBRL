using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;

public enum ProjectilePoolDisableMode
{
    SetActive,
    DisableComponents
}

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

    [Tooltip("Return/Get 시 비활성화 방식.\n" +
             "SetActive: 기존 동작 (GameObject.SetActive 토글).\n" +
             "DisableComponents: GameObject는 active 유지, ProjectileController/SpriteRenderer만 enabled 토글.")]
    [SerializeField] private ProjectilePoolDisableMode disableMode = ProjectilePoolDisableMode.DisableComponents;

    private readonly Dictionary<GameObject, Stack<ProjectileController>> _poolByPrefab = new();
    private readonly Dictionary<ProjectileController, GameObject> _prefabByProjectile = new();
    private readonly Dictionary<GameObject, int> _createdCountByPrefab = new();
    private int _activeCount;

    private static readonly ProfilerMarker s_GetMarker = new ProfilerMarker("ProjectilePool.Get");
    private static readonly ProfilerMarker s_ReturnMarker = new ProfilerMarker("ProjectilePool.Return");
    private static readonly ProfilerMarker s_SetActiveOnMarker = new ProfilerMarker("ProjectilePool.SetActiveOn");
    private static readonly ProfilerMarker s_SetActiveOffMarker = new ProfilerMarker("ProjectilePool.SetActiveOff");

    public ProjectilePoolDisableMode DisableMode => disableMode;

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

        if (!RuntimePerfTraceLogger.IsEnabled)
            return GetFast(prefab, position, rotation);

        return GetMeasured(prefab, position, rotation);
    }

    private ProjectileController GetFast(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        Stack<ProjectileController> pool = GetOrCreatePool(prefab);

        ProjectileController projectile = null;
        while (pool.Count > 0 && projectile == null)
            projectile = pool.Pop();

        if (projectile == null)
        {
            projectile = CreateProjectile(prefab, position, rotation, true);
        }
        else
        {
            Transform projectileTransform = projectile.transform;
            projectileTransform.SetPositionAndRotation(position, rotation);
            ActivateForUse(projectile, measure: false, out _);
        }

        projectile.SetReleaseAction(Return);
        _prefabByProjectile[projectile] = prefab;
        _activeCount++;
        return projectile;
    }

    private ProjectileController GetMeasured(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        long totalStart = Stopwatch.GetTimestamp();
        long instantiateTicks = 0L;
        long setActiveOnTicks = 0L;

        s_GetMarker.Begin();

        Stack<ProjectileController> pool = GetOrCreatePool(prefab);

        ProjectileController projectile = null;
        while (pool.Count > 0 && projectile == null)
            projectile = pool.Pop();

        bool instantiated = false;
        if (projectile == null)
        {
            long instantiateStart = Stopwatch.GetTimestamp();
            projectile = CreateProjectile(prefab, position, rotation, true);
            instantiateTicks = Stopwatch.GetTimestamp() - instantiateStart;
            instantiated = true;
        }
        else
        {
            Transform projectileTransform = projectile.transform;
            projectileTransform.SetPositionAndRotation(position, rotation);
            ActivateForUse(projectile, measure: true, out setActiveOnTicks);
        }

        projectile.SetReleaseAction(Return);
        _prefabByProjectile[projectile] = prefab;
        _activeCount++;

        s_GetMarker.End();

        long totalTicks = Stopwatch.GetTimestamp() - totalStart;
        RuntimePerfTraceLogger.RecordPoolGet(
            prefab.name,
            instantiated,
            _activeCount,
            pool.Count,
            totalTicks,
            instantiateTicks,
            setActiveOnTicks);
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

        if (!RuntimePerfTraceLogger.IsEnabled)
        {
            ReturnFast(projectile);
            return;
        }

        ReturnMeasured(projectile, reason);
    }

    private void ReturnFast(ProjectileController projectile)
    {
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
        DeactivateForPool(projectile, measure: false, out _);
        pool.Push(projectile);
        if (_activeCount > 0)
            _activeCount--;
    }

    private void ReturnMeasured(ProjectileController projectile, ProjectileReleaseReason reason)
    {
        long totalStart = Stopwatch.GetTimestamp();
        long setActiveOffTicks = 0L;

        s_ReturnMarker.Begin();

        if (!_prefabByProjectile.TryGetValue(projectile, out GameObject prefab) || prefab == null)
        {
            Destroy(projectile.gameObject);
            s_ReturnMarker.End();
            long fallbackTicks = Stopwatch.GetTimestamp() - totalStart;
            RuntimePerfTraceLogger.RecordPoolReturn(reason, _activeCount, 0, fallbackTicks, 0L);
            return;
        }

        if (!_poolByPrefab.TryGetValue(prefab, out Stack<ProjectileController> pool))
        {
            pool = new Stack<ProjectileController>();
            _poolByPrefab.Add(prefab, pool);
        }

        projectile.transform.SetParent(transform);
        DeactivateForPool(projectile, measure: true, out setActiveOffTicks);

        pool.Push(projectile);
        if (_activeCount > 0)
            _activeCount--;

        s_ReturnMarker.End();

        long totalTicks = Stopwatch.GetTimestamp() - totalStart;
        RuntimePerfTraceLogger.RecordPoolReturn(reason, _activeCount, pool.Count, totalTicks, setActiveOffTicks);
    }

    private void ActivateForUse(ProjectileController projectile, bool measure, out long setActiveOnTicks)
    {
        setActiveOnTicks = 0L;
        GameObject go = projectile.gameObject;

        if (disableMode == ProjectilePoolDisableMode.SetActive)
        {
            if (measure)
            {
                long start = Stopwatch.GetTimestamp();
                s_SetActiveOnMarker.Begin();
                go.SetActive(true);
                s_SetActiveOnMarker.End();
                setActiveOnTicks = Stopwatch.GetTimestamp() - start;
            }
            else
            {
                go.SetActive(true);
            }
            return;
        }

        // DisableComponents: 일반 경로에서는 GameObject는 이미 active 상태.
        // 모드 전환 등으로 inactive 상태인 경우만 안전하게 SetActive(true)를 호출한다.
        if (!go.activeSelf)
        {
            if (measure)
            {
                long start = Stopwatch.GetTimestamp();
                s_SetActiveOnMarker.Begin();
                go.SetActive(true);
                s_SetActiveOnMarker.End();
                setActiveOnTicks = Stopwatch.GetTimestamp() - start;
            }
            else
            {
                go.SetActive(true);
            }
        }

        projectile.PrepareFromPool();
    }

    private void DeactivateForPool(ProjectileController projectile, bool measure, out long setActiveOffTicks)
    {
        setActiveOffTicks = 0L;
        GameObject go = projectile.gameObject;

        if (disableMode == ProjectilePoolDisableMode.SetActive)
        {
            if (measure)
            {
                long start = Stopwatch.GetTimestamp();
                s_SetActiveOffMarker.Begin();
                go.SetActive(false);
                s_SetActiveOffMarker.End();
                setActiveOffTicks = Stopwatch.GetTimestamp() - start;
            }
            else
            {
                go.SetActive(false);
            }
            return;
        }

        // DisableComponents: GameObject.SetActive(false)를 호출하지 않는다.
        // ProjectileController.enabled / SpriteRenderer.enabled만 토글한다.
        projectile.HideForPool();
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

        if (active)
        {
            // Get fallback 경로: 즉시 사용. Instantiate된 GameObject가 prefab 정의상 active이므로 추가 SetActive 불필요.
            return projectile;
        }

        // Prewarm 경로: 모드별로 비활성화 방식이 다르다.
        if (disableMode == ProjectilePoolDisableMode.DisableComponents)
        {
            projectile.HideForPool();
        }
        else
        {
            projectileObject.SetActive(false);
        }

        return projectile;
    }
}
