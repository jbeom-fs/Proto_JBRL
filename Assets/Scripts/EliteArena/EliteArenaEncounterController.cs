using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class EliteArenaEncounterController : MonoBehaviour
{
    public static EliteArenaEncounterController Active { get; private set; }

    [Header("Teleport")]
    [SerializeField] private TownDungeonTransitionManager transitionManager;
    [SerializeField, TeleportDestinationId] private string arenaDestinationId = "elite_arena";

    [Header("Arena")]
    [SerializeField] private Tilemap arenaWalkTilemap;
    [SerializeField] private Tilemap arenaWallTilemap;
    [SerializeField] private Transform eliteSpawnPoint;
    [SerializeField] private EliteArenaReturnPortal returnPortal;

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

    public void BeginEncounter(EliteArenaPortal portal, RoomInfo room, PlayerController player, DungeonManager dungeonManager, RoomSpawner roomSpawner)
    {
        if (player == null || dungeonManager == null || roomSpawner == null)
            return;

        if (_hasEncounter || portal == null || portal.IsCompletedForRoom(room))
            return;

        if (!TryResolveReturnPosition(room, player, dungeonManager, out _originReturnPosition))
            _originReturnPosition = player.transform.position;

        if (!roomSpawner.TrySelectEliteForArena(room, out EnemyData eliteData))
            return;

        _originRoom = room;
        _hasEncounter = true;
        _eliteDefeated = false;
        HideReturnPortal();

        transitionManager = transitionManager != null ? transitionManager : TownDungeonTransitionManager.Active;
        if (transitionManager != null)
            transitionManager.TeleportPlayer(player, arenaDestinationId);
        else
            Debug.LogWarning("[EliteArenaEncounterController] TownDungeonTransitionManager is missing.", this);

        if (!TrySpawnElite(eliteData))
        {
            CancelEncounter();
            return;
        }

        portal.SetLocked(true);
    }

    public void ReturnToOriginRoom(PlayerController player)
    {
        if (!_hasEncounter || !_eliteDefeated || player == null)
            return;

        player.TeleportTo(_originReturnPosition);
        DungeonManager.Instance?.OpenCurrentRoomDoors();

        if (_activeEntrancePortal != null)
            _activeEntrancePortal.MarkCompletedAndDisable(_originRoom);

        CancelEncounter();
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

        _originRoom = default;
        _originReturnPosition = default;
    }

    // walkable / wall / LOS / bounds 판정 본체는 WalkabilityArea + WalkabilityQuery에 있습니다.
    // EliteArenaEncounterController는 입장/복귀, Elite spawn, portal lifecycle만 담당합니다.
    //
    // 아래 5개 API는 디버깅/테스트/명시적 호출용 thin pass-through 입니다.
    // 게임플레이 코드는 WalkabilityQuery / WorldEnvironmentQuery를 통해 같은 본체를 자동 라우팅합니다.

    public WalkabilityArea WalkabilityArea => walkabilityArea;

    public bool IsInsideArenaWorld(Vector2 worldPosition)
        => walkabilityArea != null && walkabilityArea.IsInsideWorld(worldPosition);

    public bool IsWalkableWorld(Vector2 worldPosition)
        => walkabilityArea != null && walkabilityArea.IsWalkableWorld(worldPosition);

    public bool IsFootprintWalkableWorld(Vector2 worldPosition, float radius)
        => walkabilityArea != null && walkabilityArea.IsFootprintWalkableWorld(worldPosition, radius);

    public bool HasLineOfSightWorld(Vector2 fromWorld, Vector2 toWorld)
        => walkabilityArea != null && walkabilityArea.HasLineOfSightWorld(fromWorld, toWorld);

    public bool TryGetNearestWalkableWorldPosition(Vector2 preferred, out Vector2 result)
    {
        if (walkabilityArea == null) { result = preferred; return false; }
        return walkabilityArea.TryGetNearestWalkableWorldPosition(preferred, out result);
    }

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
        if (returnPortal == null)
            return;

        returnPortal.Bind(this);
        returnPortal.gameObject.SetActive(true);
        returnPortal.SetColliderEnabled(true);
    }

    private void HideReturnPortal()
    {
        if (returnPortal == null)
            return;

        returnPortal.SetColliderEnabled(false);
        returnPortal.gameObject.SetActive(false);
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
