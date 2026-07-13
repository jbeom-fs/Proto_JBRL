using System;
using UnityEngine;
using UnityEngine.Tilemaps;

public abstract class ArenaEncounterBase : MonoBehaviour
{
    [Header("Teleport")]
    [SerializeField] protected LocationTransitionManager transitionManager;

    [Header("Arena")]
    [SerializeField] protected Tilemap arenaWalkTilemap;
    [SerializeField] protected Tilemap arenaWallTilemap;
    [SerializeField] protected EnemyDropDatabase dropDatabase;
    [SerializeField] protected Transform eliteSpawnPoint;
    [SerializeField] protected EliteArenaReturnPortal returnPortal;
    [SerializeField] protected EliteArenaReturnPortal returnPortalPrefab;
    [SerializeField] protected Transform returnPortalSpawnPoint;
    [SerializeField] protected Transform returnPortalParent;

    [Tooltip("Walkability query component for this arena. Assign from this GameObject or the arena root.")]
    [SerializeField] protected WalkabilityArea walkabilityArea;

    private EliteArenaReturnPortal _activeReturnPortal;

    protected bool TryTeleportPlayerToArena(PlayerController player, string destinationId)
    {
        transitionManager = transitionManager != null ? transitionManager : LocationTransitionManager.Active;
        if (transitionManager == null)
            return false;

        return transitionManager.TryTeleportPlayer(player, destinationId);
    }

    protected void RestoreDungeonMinimapSource()
    {
        LocationTransitionManager router = transitionManager != null
            ? transitionManager
            : LocationTransitionManager.Active;
        router?.RestoreDungeonMinimapSource();
    }

    protected EnemyController SpawnArenaEnemyAtPosition(
        EnemyData enemyData,
        Vector3 spawnPosition,
        Action<EnemyController> deathHandler,
        System.Random dropRng)
    {
        if (enemyData == null || EnemyPoolManager.Instance == null)
            return null;

        EnemyController enemy = EnemyPoolManager.Instance.Request(enemyData);
        if (enemy == null)
            return null;

        enemy.transform.position = spawnPosition;
        enemy.transform.SetParent(null);
        enemy.Initialize(enemyData);
        enemy.GetComponent<EnemyHealthBar>()?.SetBarSuppressed(true);
        enemy.GetComponent<EnemyAilmentIndicator>()?.SetSuppressed(true);
        if (dropDatabase != null && dropRng != null)
            enemy.RollDrops(dropDatabase.GetDropGroup(enemyData), dropRng);

        if (deathHandler != null)
        {
            enemy.OnDied -= deathHandler;
            enemy.OnDied += deathHandler;
        }

        return enemy;
    }

    protected virtual void BindReturnPortal(EliteArenaReturnPortal portal)
    {
    }

    protected void ShowReturnPortal()
    {
        EliteArenaReturnPortal portal = GetReturnPortal();
        if (portal == null)
            return;

        if (TryResolveReturnPortalPosition(out Vector3 position))
            portal.transform.position = position;

        BindReturnPortal(portal);
        portal.gameObject.SetActive(true);
        portal.SetColliderEnabled(true);
    }

    protected void HideReturnPortal()
    {
        EliteArenaReturnPortal portal = _activeReturnPortal != null ? _activeReturnPortal : returnPortal;
        if (portal == null)
            return;

        portal.SetColliderEnabled(false);
        portal.gameObject.SetActive(false);
    }

    private EliteArenaReturnPortal GetReturnPortal()
    {
        if (_activeReturnPortal != null)
            return _activeReturnPortal;

        if (returnPortal != null)
        {
            _activeReturnPortal = returnPortal;
            return _activeReturnPortal;
        }

        if (returnPortalPrefab == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[" + GetType().Name + "] Return portal prefab/scene reference is missing.", this);
#endif
            return null;
        }

        Transform parent = returnPortalParent != null ? returnPortalParent : transform;
        _activeReturnPortal = Instantiate(returnPortalPrefab, parent);
        _activeReturnPortal.gameObject.SetActive(false);
        _activeReturnPortal.SetColliderEnabled(false);
        return _activeReturnPortal;
    }

    protected bool TryResolveArenaEnemySpawnPosition(out Vector3 position)
    {
        if (eliteSpawnPoint != null)
        {
            position = eliteSpawnPoint.position;
            return true;
        }

        return TryGetCenterTileWorldPosition(arenaWalkTilemap, out position);
    }

    private bool TryResolveReturnPortalPosition(out Vector3 position)
    {
        if (returnPortalSpawnPoint != null)
        {
            position = returnPortalSpawnPoint.position;
            return true;
        }

        if (TryGetCenterTileWorldPosition(arenaWalkTilemap, out position))
            return true;

        if (walkabilityArea != null &&
            walkabilityArea.TryGetNearestWalkableWorldPosition(transform.position, out position))
        {
            return true;
        }

        position = transform.position;
        return false;
    }

    protected static bool TryGetCenterTileWorldPosition(Tilemap tilemap, out Vector3 position)
    {
        position = default;
        if (tilemap == null)
            return false;

        BoundsInt bounds = tilemap.cellBounds;
        Vector3Int center = new Vector3Int(
            Mathf.FloorToInt(bounds.center.x),
            Mathf.FloorToInt(bounds.center.y),
            0);

        Vector3Int bestCell = default;
        float bestDistance = float.PositiveInfinity;
        bool found = false;

        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            if (!tilemap.HasTile(cell))
                continue;

            float distance = (cell - center).sqrMagnitude;
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestCell = cell;
            found = true;
        }

        if (!found)
            return false;

        position = tilemap.GetCellCenterWorld(bestCell);
        return true;
    }
}
