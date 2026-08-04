using System.Collections.Generic;
using UnityEngine;

public sealed class DamageZoneSpawner : MonoBehaviour
{
    public static DamageZoneSpawner Instance { get; private set; }

    [SerializeField] private DamageZone defaultZonePrefab;

    private readonly Dictionary<DamageZone, List<DamageZone>> _pools =
        new Dictionary<DamageZone, List<DamageZone>>();
    private readonly List<DamageZone> _activeZones = new List<DamageZone>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool SpawnZone(
        Vector3 position,
        Sprite sprite,
        AnimatorOverrideController animation,
        in ZonePayload payload)
    {
        if (defaultZonePrefab == null)
        {
            Debug.LogWarning("[DamageZoneSpawner] Default zone prefab is not assigned.", this);
            return false;
        }

        if (!payload.HasAnyEffect)
        {
            Debug.LogWarning("[DamageZoneSpawner] ZonePayload has no effect.", this);
            return false;
        }

        DamageZone zone = GetZone();
        zone.transform.position = position;
        zone.Initialize(sprite, animation, in payload);
        zone.gameObject.SetActive(true);
        _activeZones.Add(zone);
        return true;
    }

    public void ClearAllActiveZones()
    {
        for (int i = _activeZones.Count - 1; i >= 0; i--)
            Release(_activeZones[i]);
    }

    internal void Release(DamageZone zone)
    {
        if (zone == null)
            return;

        _activeZones.Remove(zone);
        zone.gameObject.SetActive(false);
    }

    private DamageZone GetZone()
    {
        if (!_pools.TryGetValue(defaultZonePrefab, out List<DamageZone> pool))
        {
            pool = new List<DamageZone>();
            _pools.Add(defaultZonePrefab, pool);
        }

        for (int i = 0; i < pool.Count; i++)
        {
            DamageZone zone = pool[i];
            if (zone != null && !zone.gameObject.activeSelf)
                return zone;
        }

        DamageZone created = Instantiate(defaultZonePrefab, transform);
        created.SetPoolOwner(this);
        created.gameObject.SetActive(false);
        pool.Add(created);
        return created;
    }
}
