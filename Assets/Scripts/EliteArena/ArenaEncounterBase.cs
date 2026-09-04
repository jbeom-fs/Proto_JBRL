using System;
using UnityEngine;
using UnityEngine.Tilemaps;

public abstract class ArenaEncounterBase : MonoBehaviour
{
    [Header("Teleport")]
    [SerializeField] protected LocationTransitionManager transitionManager;

    [Header("Arena")]
    [SerializeField] protected EnemyDropDatabase dropDatabase;

    private ArenaSpace _activeSpace;
    private bool _warnedMissingArenaDoor;
    private bool _warnedMissingArenaSpace;

    protected abstract DropRank EncounterDropRank { get; }
    protected ArenaSpace ActiveSpace => _activeSpace;
    protected ArenaDoor ActiveArenaDoor => _activeSpace != null ? _activeSpace.ArenaDoor : null;
    protected Tilemap ActiveWalkTilemap => _activeSpace != null ? _activeSpace.WalkTilemap : null;
    protected WalkabilityArea ActiveWalkabilityArea => _activeSpace != null ? _activeSpace.WalkabilityArea : null;
    protected Transform ActiveEnemySpawnPoint => _activeSpace != null ? _activeSpace.EnemySpawnPoint : null;
    protected Portal ActiveClearedPortal => _activeSpace != null ? _activeSpace.ClearedPortal : null;
    protected Transform ActiveClearedPortalSpawnPoint =>
        _activeSpace != null ? _activeSpace.ClearedPortalSpawnPoint : null;
    // 폴백 좌표 기준은 컨트롤러가 아니라 방 루트다. 컨트롤러는 방 밖의 별개 GameObject다.
    protected Vector3 ActiveSpaceOrigin =>
        _activeSpace != null ? _activeSpace.transform.position : transform.position;

    protected void ResolveArenaSpace(string destinationId)
    {
        transitionManager = transitionManager != null
            ? transitionManager
            : LocationTransitionManager.Active;

        _activeSpace = null;
        if (transitionManager != null &&
            transitionManager.TryResolveLocationRoot(destinationId, out LocationRoot root))
        {
            _activeSpace = root.GetComponent<ArenaSpace>();
        }

        if (_activeSpace != null || _warnedMissingArenaSpace)
            return;

        _warnedMissingArenaSpace = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning(
            "[" + GetType().Name + "] ArenaSpace could not be resolved for destination '" +
            destinationId + "'.",
            this);
#endif
    }

    protected void ClearArenaSpace()
    {
        _activeSpace = null;
    }

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

    protected void CloseArenaDoor()
    {
        if (ActiveArenaDoor == null)
        {
            WarnMissingArenaDoor();
            return;
        }

        ActiveArenaDoor.Close();
    }

    protected void OpenArenaDoor()
    {
        if (ActiveArenaDoor == null)
        {
            WarnMissingArenaDoor();
            return;
        }

        ActiveArenaDoor.Open();
    }

    protected void WarnMissingArenaDoor()
    {
        // 조우 밖에서는 공간이 없는 게 정상이다. 공간이 있는데 문 슬롯이 빈 경우만 결선 누락이다.
        if (ActiveSpace == null || _warnedMissingArenaDoor)
            return;

        Debug.LogWarning("[" + GetType().Name + "] ArenaDoor reference is missing.", this);
        _warnedMissingArenaDoor = true;
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
            enemy.RollDrops(dropDatabase, dropDatabase.GetDropGroup(enemyData), EncounterDropRank, dropRng);
        DropQueryResolver.FlushWarnings();

        if (deathHandler != null)
        {
            enemy.OnDied -= deathHandler;
            enemy.OnDied += deathHandler;
        }

        return enemy;
    }

    protected bool TryResolveArenaEnemySpawnPosition(out Vector3 position)
    {
        if (ActiveEnemySpawnPoint != null)
        {
            position = ActiveEnemySpawnPoint.position;
            return true;
        }

        return TryGetCenterTileWorldPosition(ActiveWalkTilemap, out position);
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
