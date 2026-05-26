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

    public static bool TryIsArenaFootprintWalkable(Vector3 worldPosition, float radius, out bool isWalkable)
    {
        isWalkable = true;
        EliteArenaEncounterController active = Active;
        if (active == null || !active._hasEncounter)
            return false;

        return active.TryIsFootprintWalkable(worldPosition, radius, out isWalkable);
    }

    public static bool TryIsArenaPointWalkable(Vector3 worldPosition, out bool isWalkable)
    {
        isWalkable = true;
        EliteArenaEncounterController active = Active;
        if (active == null || !active._hasEncounter || active.arenaWalkTilemap == null)
            return false;

        isWalkable = active.IsArenaPointWalkable(worldPosition);
        return true;
    }

    public static bool TryIsArenaWorldPosition(Vector3 worldPosition, out bool isInArena)
    {
        isInArena = false;
        EliteArenaEncounterController active = Active;
        if (active == null || !active._hasEncounter || active.arenaWalkTilemap == null)
            return false;

        isInArena = HasTileAtWorld(active.arenaWalkTilemap, worldPosition);
        return true;
    }

    public static bool TryHasArenaLineOfSight(Vector3 from, Vector3 to, out bool hasLineOfSight)
    {
        hasLineOfSight = true;
        EliteArenaEncounterController active = Active;
        if (active == null || !active._hasEncounter || active.arenaWalkTilemap == null)
            return false;

        hasLineOfSight = active.HasArenaLineOfSight(from, to);
        return true;
    }

    public static bool TryFindNearestArenaWalkableWorld(
        Vector3 desired,
        Vector3 origin,
        float maxDistanceFromOrigin,
        float footprintRadius,
        int maxSearchRadius,
        out Vector3 position)
    {
        position = desired;
        EliteArenaEncounterController active = Active;
        if (active == null || !active._hasEncounter || active.arenaWalkTilemap == null)
            return false;

        return active.TryFindNearestWalkableWorld(
            desired,
            origin,
            maxDistanceFromOrigin,
            footprintRadius,
            maxSearchRadius,
            out position);
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

    private bool TryIsFootprintWalkable(Vector3 worldPosition, float radius, out bool isWalkable)
    {
        isWalkable = true;
        if (arenaWalkTilemap == null)
            return false;

        if (!HasTileAtWorld(arenaWalkTilemap, worldPosition))
            return false;

        Vector3 c0 = new Vector3(worldPosition.x - radius, worldPosition.y - radius, 0f);
        Vector3 c1 = new Vector3(worldPosition.x + radius, worldPosition.y - radius, 0f);
        Vector3 c2 = new Vector3(worldPosition.x - radius, worldPosition.y + radius, 0f);
        Vector3 c3 = new Vector3(worldPosition.x + radius, worldPosition.y + radius, 0f);

        isWalkable =
            IsArenaPointWalkable(c0) &&
            IsArenaPointWalkable(c1) &&
            IsArenaPointWalkable(c2) &&
            IsArenaPointWalkable(c3);
        return true;
    }

    private bool IsArenaPointWalkable(Vector3 world)
    {
        if (!HasTileAtWorld(arenaWalkTilemap, world))
            return false;

        return arenaWallTilemap == null || !HasTileAtWorld(arenaWallTilemap, world);
    }

    private bool IsArenaCellWalkable(Vector3Int cell)
    {
        if (arenaWalkTilemap == null || !arenaWalkTilemap.HasTile(cell))
            return false;

        return arenaWallTilemap == null || !arenaWallTilemap.HasTile(cell);
    }

    private bool HasArenaLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3Int fromCell = arenaWalkTilemap.WorldToCell(from);
        Vector3Int toCell = arenaWalkTilemap.WorldToCell(to);

        int dx = toCell.x - fromCell.x;
        int dy = toCell.y - fromCell.y;
        int steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        if (steps == 0)
            return IsArenaCellWalkable(fromCell);

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            int x = Mathf.RoundToInt(fromCell.x + dx * t);
            int y = Mathf.RoundToInt(fromCell.y + dy * t);
            if (!IsArenaCellWalkable(new Vector3Int(x, y, fromCell.z)))
                return false;
        }

        return true;
    }

    private bool TryFindNearestWalkableWorld(
        Vector3 desired,
        Vector3 origin,
        float maxDistanceFromOrigin,
        float footprintRadius,
        int maxSearchRadius,
        out Vector3 position)
    {
        position = desired;
        Vector3Int center = arenaWalkTilemap.WorldToCell(desired);
        float maxDistanceSqr = maxDistanceFromOrigin * maxDistanceFromOrigin;
        int searchRadius = Mathf.Max(0, maxSearchRadius);

        for (int radius = 0; radius <= searchRadius; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (radius > 0 && Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius)
                        continue;

                    Vector3Int cell = new Vector3Int(center.x + dx, center.y + dy, center.z);
                    if (!IsArenaCellWalkable(cell))
                        continue;

                    Vector3 world = arenaWalkTilemap.GetCellCenterWorld(cell);
                    if ((world - origin).sqrMagnitude > maxDistanceSqr)
                        continue;

                    if (!TryIsFootprintWalkable(world, footprintRadius, out bool canOccupy) || !canOccupy)
                        continue;

                    position = world;
                    return true;
                }
            }
        }

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

    private static bool HasTileAtWorld(Tilemap tilemap, Vector3 world)
    {
        return tilemap != null && tilemap.HasTile(tilemap.WorldToCell(world));
    }
}
