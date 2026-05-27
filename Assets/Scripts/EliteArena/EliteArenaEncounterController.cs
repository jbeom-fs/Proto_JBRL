using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class EliteArenaEncounterController : MonoBehaviour
{
    public static EliteArenaEncounterController Active { get; private set; }

    [Header("Teleport")]
    [SerializeField] private LocationTransitionManager transitionManager;
    [SerializeField, TeleportDestinationId] private string arenaDestinationId = "elite_arena";

    [Header("Arena")]
    [SerializeField] private Tilemap arenaWalkTilemap;
    [SerializeField] private Tilemap arenaWallTilemap;
    [SerializeField] private Transform eliteSpawnPoint;
    [SerializeField] private EliteArenaReturnPortal returnPortal;
    [SerializeField] private EliteArenaReturnPortal returnPortalPrefab;
    [SerializeField] private Transform returnPortalSpawnPoint;
    [SerializeField] private Transform returnPortalParent;

    [Tooltip("Arena의 walkable 영역을 query하는 컴포넌트. 같은 GameObject 또는 Arena root에서 연결합니다.")]
    [SerializeField] private WalkabilityArea walkabilityArea;

    [Header("Elite Room Portal")]
    [SerializeField] private EliteArenaPortal entrancePortalInstance;
    [SerializeField] private EliteArenaPortal entrancePortalPrefab;
    [SerializeField] private Transform entrancePortalParent;
    [SerializeField] private Transform eliteRoomReturnPoint;

    private RoomInfo _originRoom;
    private Vector3 _originReturnPosition;
    private EliteArenaPortal _activeEntrancePortal;
    private EliteArenaReturnPortal _activeReturnPortal;
    private EnemyController _activeElite;
    private bool _hasEncounter;
    private bool _eliteDefeated;

    public bool IsEncounterActiveInArena => _hasEncounter && !_eliteDefeated;

    private void Awake()
    {
        if (Active != null && Active != this)
        {
            Destroy(gameObject);
            return;
        }

        Active = this;
        HideReturnPortal();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Active, this))
            Active = null;
    }

    public void PrepareEntrancePortal(RoomInfo room, DungeonManager dungeonManager)
    {
        if (dungeonManager == null || dungeonManager.Data == null)
            return;

        if (_activeEntrancePortal != null && !_activeEntrancePortal.IsCompletedForRoom(room))
            _activeEntrancePortal.gameObject.SetActive(false);

        Vector3 portalPosition;
        if (!TryGetRoomCenterWalkablePosition(room, dungeonManager, out portalPosition))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[EliteArenaEncounterController] Elite Room has no valid portal position.", this);
#endif
            return;
        }

        EliteArenaPortal portal = GetEntrancePortal();
        if (portal == null)
            return;

        portal.transform.position = portalPosition;
        portal.Bind(this, room);
        portal.gameObject.SetActive(true);
        _activeEntrancePortal = portal;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log(
            "[EliteArenaEncounterController] Entrance portal prepared. roomKey=" + room.StableRoomKey +
            " position=" + portalPosition +
            " activeSelf=" + portal.gameObject.activeSelf +
            " activeInHierarchy=" + portal.gameObject.activeInHierarchy,
            portal);
#endif
    }

    public bool TryEnterArenaFromPortal(EliteArenaPortal portal, RoomInfo room, PlayerController player)
    {
        return TryEnterArenaFromPortal(portal, room, player, DungeonManager.Instance, RoomSpawner.Active);
    }

    public bool TryEnterArenaFromPortal(EliteArenaPortal portal, RoomInfo room, PlayerController player, DungeonManager dungeonManager, RoomSpawner roomSpawner)
    {
        if (player == null || dungeonManager == null || roomSpawner == null)
            return false;

        if (_hasEncounter || portal == null || portal.IsCompletedForRoom(room))
            return false;

        if (!TryResolveReturnPosition(room, player, dungeonManager, out _originReturnPosition))
            _originReturnPosition = player.transform.position;

        if (!roomSpawner.TrySelectEliteForArena(room, out EnemyData eliteData))
            return false;

        _originRoom = room;
        _hasEncounter = true;
        _eliteDefeated = false;
        HideReturnPortal();

        transitionManager = transitionManager != null ? transitionManager : LocationTransitionManager.Active;
        if (transitionManager == null)
        {
            Debug.LogWarning("[EliteArenaEncounterController] LocationTransitionManager is missing.", this);
            CancelEncounter();
            return false;
        }

        if (!transitionManager.TryTeleportPlayer(player, arenaDestinationId))
        {
            CancelEncounter();
            return false;
        }

        if (!TrySpawnElite(eliteData))
        {
            CancelEncounter();
            return false;
        }

        portal.SetLocked(true);
        return true;
    }

    public void BeginEncounter(EliteArenaPortal portal, RoomInfo room, PlayerController player, DungeonManager dungeonManager, RoomSpawner roomSpawner)
    {
        TryEnterArenaFromPortal(portal, room, player, dungeonManager, roomSpawner);
    }

    public bool TryReturnFromArena(PlayerController player)
    {
        if (!_hasEncounter || !_eliteDefeated || player == null)
            return false;

        player.TeleportTo(_originReturnPosition);
        DungeonManager.Instance?.OpenCurrentRoomDoors();

        LocationTransitionManager router = transitionManager != null
            ? transitionManager
            : LocationTransitionManager.Active;
        router?.RestoreDungeonMinimapSource();

        if (_activeEntrancePortal != null)
            _activeEntrancePortal.MarkCompletedAndDisable(_originRoom);

        CancelEncounter();
        return true;
    }

    public void ReturnToOriginRoom(PlayerController player)
    {
        TryReturnFromArena(player);
    }

    public void CancelEncounter()
    {
        if (_activeElite != null)
        {
            _activeElite.OnDied -= OnEliteDied;
            _activeElite = null;
        }

        _hasEncounter = false;
        _eliteDefeated = false;
        HideReturnPortal();
    }

    public void ClearRuntimeState()
    {
        CancelEncounter();

        if (_activeEntrancePortal != null)
        {
            _activeEntrancePortal.ResetRuntimeState();
            _activeEntrancePortal.gameObject.SetActive(false);
        }

        HideReturnPortal();

        _originRoom = default;
        _originReturnPosition = default;
    }

    // Developer Console 전용. RoomSpawner.ForceKillCurrentEncounterEnemiesForDebug 에서만 호출됩니다.
    internal int ForceKillActiveEliteForDebug()
    {
        if (!_hasEncounter || _eliteDefeated || _activeElite == null || _activeElite.IsDead || !_activeElite.IsAlive)
            return 0;

        _activeElite.ForceKillForDebug();
        return 1;
    }

    // walkable / wall / LOS / bounds 판정 본체는 WalkabilityArea + WalkabilityQuery에 있습니다.
    // EliteArenaEncounterController는 입장/복귀, Elite spawn, portal lifecycle만 담당합니다.
    // 게임플레이 코드는 WalkabilityQuery / WorldEnvironmentQuery를 통해 같은 본체를 자동 라우팅합니다.

    private bool TrySpawnElite(EnemyData eliteData)
    {
        if (eliteData == null || EnemyPoolManager.Instance == null)
            return false;

        if (!TryResolveEliteSpawnPosition(out Vector3 spawnPosition))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[EliteArenaEncounterController] Elite Arena has no valid elite spawn position.", this);
#endif
            return false;
        }

        EnemyController enemy = EnemyPoolManager.Instance.Request(eliteData);
        if (enemy == null)
            return false;

        enemy.transform.position = spawnPosition;
        enemy.transform.SetParent(null);
        enemy.Initialize(eliteData);
        enemy.OnDied -= OnEliteDied;
        enemy.OnDied += OnEliteDied;
        _activeElite = enemy;
        return true;
    }

    private void OnEliteDied(EnemyController enemy)
    {
        if (enemy != null)
            enemy.OnDied -= OnEliteDied;

        if (_activeElite == enemy)
            _activeElite = null;

        _eliteDefeated = true;
        ShowReturnPortal();
    }

    private EliteArenaPortal GetEntrancePortal()
    {
        if (entrancePortalInstance != null)
            return entrancePortalInstance;

        if (entrancePortalPrefab == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[EliteArenaEncounterController] Entrance portal prefab is missing. Assign Assets/Perfabs/EliteArena/EliteArenaPortal.prefab.", this);
#endif
            return null;
        }

        Transform parent = entrancePortalParent != null ? entrancePortalParent : transform;
        entrancePortalInstance = Instantiate(entrancePortalPrefab, parent);
        return entrancePortalInstance;
    }

    private bool TryResolveReturnPosition(RoomInfo room, PlayerController player, DungeonManager dungeonManager, out Vector3 position)
    {
        if (eliteRoomReturnPoint != null)
        {
            position = eliteRoomReturnPoint.position;
            return true;
        }

        if (TryGetRoomCenterWalkablePosition(room, dungeonManager, out position))
            return true;

        position = player != null ? player.transform.position : default;
        return player != null;
    }

    private static bool TryGetRoomCenterWalkablePosition(RoomInfo room, DungeonManager dungeonManager, out Vector3 position)
    {
        position = default;
        List<Vector2Int> tiles = dungeonManager.Data.GetWalkableTiles(room);
        if (tiles == null || tiles.Count == 0)
            return false;

        Vector2 center = new Vector2(room.CenterX, room.CenterY);
        Vector2Int best = tiles[0];
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < tiles.Count; i++)
        {
            float distance = ((Vector2)tiles[i] - center).sqrMagnitude;
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = tiles[i];
        }

        position = dungeonManager.GridToWorld(best);
        return true;
    }

    private bool TryResolveEliteSpawnPosition(out Vector3 position)
    {
        if (eliteSpawnPoint != null)
        {
            position = eliteSpawnPoint.position;
            return true;
        }

        return TryGetCenterTileWorldPosition(arenaWalkTilemap, out position);
    }

    private void ShowReturnPortal()
    {
        EliteArenaReturnPortal portal = GetReturnPortal();
        if (portal == null)
            return;

        if (TryResolveReturnPortalPosition(out Vector3 position))
            portal.transform.position = position;

        portal.Bind(this);
        portal.gameObject.SetActive(true);
        portal.SetColliderEnabled(true);
    }

    private void HideReturnPortal()
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
            Debug.LogWarning("[EliteArenaEncounterController] Return portal prefab/scene reference is missing.", this);
#endif
            return null;
        }

        Transform parent = returnPortalParent != null ? returnPortalParent : transform;
        _activeReturnPortal = Instantiate(returnPortalPrefab, parent);
        _activeReturnPortal.gameObject.SetActive(false);
        _activeReturnPortal.SetColliderEnabled(false);
        return _activeReturnPortal;
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
            return true;

        position = transform.position;
        return false;
    }

    private static bool TryGetCenterTileWorldPosition(Tilemap tilemap, out Vector3 position)
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
