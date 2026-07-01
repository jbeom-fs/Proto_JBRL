using UnityEngine;

public sealed class EliteArenaEncounterController : ArenaEncounterBase
{
    public static EliteArenaEncounterController Active { get; private set; }

    [Header("Teleport")]
    [SerializeField, TeleportDestinationId] private string arenaDestinationId = "elite_arena";

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
        if (!DungeonPlacementUtility.TryGetRoomCenterWalkablePosition(room, dungeonManager, out portalPosition))
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

    public bool TryEnterArenaFromPortal(
        EliteArenaPortal portal,
        RoomInfo room,
        PlayerController player,
        DungeonManager dungeonManager,
        RoomSpawner roomSpawner)
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

        if (transitionManager == null && LocationTransitionManager.Active == null)
        {
            Debug.LogWarning("[EliteArenaEncounterController] LocationTransitionManager is missing.", this);
            CancelEncounter();
            return false;
        }

        if (!TryTeleportPlayerToArena(player, arenaDestinationId))
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

    public void BeginEncounter(
        EliteArenaPortal portal,
        RoomInfo room,
        PlayerController player,
        DungeonManager dungeonManager,
        RoomSpawner roomSpawner)
    {
        TryEnterArenaFromPortal(portal, room, player, dungeonManager, roomSpawner);
    }

    public bool TryReturnFromArena(PlayerController player)
    {
        if (!_hasEncounter || !_eliteDefeated || player == null)
            return false;

        player.TeleportTo(_originReturnPosition);
        DungeonManager.Instance?.OpenCurrentRoomDoors();
        RestoreDungeonMinimapSource();

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

    // Developer Console only. Called through RoomSpawner.ForceKillCurrentEncounterEnemiesForDebug.
    internal int ForceKillActiveEliteForDebug()
    {
        if (!_hasEncounter || _eliteDefeated || _activeElite == null || _activeElite.IsDead || !_activeElite.IsAlive)
            return 0;

        _activeElite.ForceKillForDebug();
        return 1;
    }

    // Walkable / wall / LOS / bounds decisions live in WalkabilityArea + WalkabilityQuery.
    // This controller owns entry/return, elite spawn, and portal lifecycle only.

    private bool TrySpawnElite(EnemyData eliteData)
    {
        if (eliteData == null || EnemyPoolManager.Instance == null)
            return false;

        if (!TryResolveArenaEnemySpawnPosition(out Vector3 spawnPosition))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[EliteArenaEncounterController] Elite Arena has no valid elite spawn position.", this);
#endif
            return false;
        }

        System.Random dropRng = CreateEliteDropRng();
        EnemyController enemy = SpawnArenaEnemyAtPosition(eliteData, spawnPosition, OnEliteDied, dropRng);
        if (enemy == null)
            return false;

        _activeElite = enemy;
        return true;
    }

    private System.Random CreateEliteDropRng()
    {
        DungeonManager dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null || dungeonManager.Data == null)
            return null;

        int dropSeed = DeterministicSeedUtility.CreateSeed(
            dungeonManager.seed,
            (int)dungeonManager.Data.currentStageRegion,
            dungeonManager.floor,
            _originRoom.StableRoomKey,
            DeterministicSeedUtility.EnemyDropDomain);
        return new System.Random(dropSeed);
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

    private bool TryResolveReturnPosition(
        RoomInfo room,
        PlayerController player,
        DungeonManager dungeonManager,
        out Vector3 position)
    {
        if (eliteRoomReturnPoint != null)
        {
            position = eliteRoomReturnPoint.position;
            return true;
        }

        if (DungeonPlacementUtility.TryGetRoomCenterWalkablePosition(room, dungeonManager, out position))
            return true;

        position = player != null ? player.transform.position : default;
        return player != null;
    }

    protected override void BindReturnPortal(EliteArenaReturnPortal portal)
    {
        portal.Bind(this);
    }
}
