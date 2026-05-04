using System.Collections.Generic;
using UnityEngine;

public class ProjectilePool : MonoBehaviour
{
    private static ProjectilePool _instance;

    private readonly Dictionary<GameObject, Stack<ProjectileController>> _poolByPrefab = new();
    private readonly Dictionary<ProjectileController, GameObject> _prefabByProjectile = new();

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
    }

    public ProjectileController Get(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
            return null;

        if (!_poolByPrefab.TryGetValue(prefab, out Stack<ProjectileController> pool))
        {
            pool = new Stack<ProjectileController>();
            _poolByPrefab.Add(prefab, pool);
        }

        ProjectileController projectile = null;
        while (pool.Count > 0 && projectile == null)
            projectile = pool.Pop();

        if (projectile == null)
        {
            GameObject projectileObject = Instantiate(prefab, position, rotation, transform);
            projectile = projectileObject.GetComponent<ProjectileController>();
            if (projectile == null)
                projectile = projectileObject.AddComponent<ProjectileController>();
        }
        else
        {
            Transform projectileTransform = projectile.transform;
            projectileTransform.SetPositionAndRotation(position, rotation);
            projectile.gameObject.SetActive(true);
        }

        projectile.SetReleaseAction(Return);
        _prefabByProjectile[projectile] = prefab;
        return projectile;
    }

    public void Return(ProjectileController projectile)
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
    }
}
