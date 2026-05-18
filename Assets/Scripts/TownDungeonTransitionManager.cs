using UnityEngine;
using UnityEngine.InputSystem;

public class TownDungeonTransitionManager : MonoBehaviour
{
    public static TownDungeonTransitionManager Active { get; private set; }

    [Header("Roots")]
    [SerializeField] private GameObject townRoot;
    [SerializeField] private GameObject dungeonRoot;
    [SerializeField] private GameObject minimapRoot;

    [Header("Destinations")]
    [SerializeField] private TeleportDestinationDatabase destinationDatabase;
    [SerializeField] private string startDestinationId = "town_start";
    [SerializeField] private string debugDungeonEntranceDestinationId = "dungeon_entrance";
    [SerializeField] private string debugReturnDestinationId = "town_return";

    [Header("Dependencies")]
    [SerializeField] private PlayerController player;
    [SerializeField] private DungeonManager dungeonManager;
    [SerializeField] private FogOfWarController fogOfWar;
    [SerializeField] private RoomSpawner roomSpawner;
    [SerializeField] private MinimapController minimap;

    [Header("Flow")]
    [SerializeField] private bool generateNewDungeonOnEnter = true;
    [SerializeField] private bool resetFloorOnNewDungeonRun = true;
    [SerializeField] private bool disableCombatInTown = true;
    [SerializeField] private bool enableDebugReturnKey = true;

    private bool _warnedMissingReferences;
    private bool _isChangingLocation;

    public GameLocationType CurrentLocation { get; private set; }
    public bool IsInTown => CurrentLocation == GameLocationType.Town;
    public bool IsInDungeon => CurrentLocation == GameLocationType.Dungeon;
    public bool ShouldBlockCombat => disableCombatInTown && IsInTown;
    public bool StartsInTown => !TryGetLocation(startDestinationId, out TeleportLocationData startLocation) ||
                                startLocation.LocationType == GameLocationType.Town;

    private void Awake()
    {
        if (Active != null && Active != this)
        {
            Destroy(gameObject);
            return;
        }

        Active = this;
        WarnIfMissingReferences();
    }

    private void Start()
    {
        if (!string.IsNullOrWhiteSpace(startDestinationId))
            TeleportPlayer(player, startDestinationId);
        else
            EnterLocationWithoutPoint(GameLocationType.Town);
    }

    private void Update()
    {
        if (!enableDebugReturnKey || !IsInDungeon)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.tKey.wasPressedThisFrame)
            TeleportPlayer(player, debugReturnDestinationId);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Active, this))
            Active = null;
    }

    public void TeleportPlayer(PlayerController targetPlayer, string destinationId)
    {
        if (targetPlayer == null || _isChangingLocation)
            return;

        if (!TryGetLocation(destinationId, out TeleportLocationData destination))
            return;

        GameLocationType from = CurrentLocation;
        GameLocationType to = destination.LocationType;
        bool enteringDungeon = from != GameLocationType.Dungeon && to == GameLocationType.Dungeon;
        bool leavingDungeon = from == GameLocationType.Dungeon && to != GameLocationType.Dungeon;

        _isChangingLocation = true;

        if (leavingDungeon)
            CleanupDungeonRuntime();

        ApplyLocationRoots(to);
        CurrentLocation = to;

        bool hasPoint = TeleportDestinationRegistry.TryGetPoint(destinationId, out TeleportDestinationPoint point);
        if (enteringDungeon)
        {
            minimap?.SetDungeonSource();
            StartNewDungeonRun(targetPlayer);
        }
        else
        {
            minimap?.SetTilemapSource(destination.MinimapLocationId);
            if (hasPoint)
                targetPlayer.TeleportTo(point.transform.position);
        }

        _isChangingLocation = false;
    }

    public void EnterDungeon()
    {
        TeleportPlayer(player, debugDungeonEntranceDestinationId);
    }

    public void EnterTown()
    {
        TeleportPlayer(player, debugReturnDestinationId);
    }

    private bool TryGetLocation(string destinationId, out TeleportLocationData location)
    {
        location = null;

        if (destinationDatabase == null)
        {
            Warn("Destination database is missing.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(destinationId))
        {
            Warn("Destination id is empty.");
            return false;
        }

        if (destinationDatabase.TryGetLocation(destinationId, out location))
            return true;

        Warn("Destination id not found in database: " + destinationId);
        return false;
    }

    private void EnterLocationWithoutPoint(GameLocationType locationType)
    {
        _isChangingLocation = true;
        ApplyLocationRoots(locationType);
        CurrentLocation = locationType;
        _isChangingLocation = false;
    }

    private void StartNewDungeonRun(PlayerController targetPlayer)
    {
        if (generateNewDungeonOnEnter && resetFloorOnNewDungeonRun)
            dungeonManager.floor = 1;

        if (generateNewDungeonOnEnter || dungeonManager.Data == null)
            dungeonManager.Generate();

        fogOfWar?.RequestFullInitialize();
        roomSpawner?.ResetRoomEncounterState();
        targetPlayer?.SpawnAtStart();
    }

    private void CleanupDungeonRuntime()
    {
        ProjectilePool.ReleaseAllActiveProjectiles(ProjectileReleaseReason.Manual);
        EnemyPoolManager.ReleaseAllActiveEnemiesForLocationChange();
        roomSpawner?.ClearRuntimeEncounterState();
    }

    private void ApplyLocationRoots(GameLocationType locationType)
    {
        bool isDungeon = locationType == GameLocationType.Dungeon;
        SetRootActive(townRoot, !isDungeon);
        SetRootActive(dungeonRoot, isDungeon);
        SetRootActive(minimapRoot, true); // always visible — source switches on teleport
    }

    private static void SetRootActive(GameObject root, bool active)
    {
        if (root != null && root.activeSelf != active)
            root.SetActive(active);
    }

    private void WarnIfMissingReferences()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedMissingReferences)
            return;

        if (townRoot != null &&
            dungeonRoot != null &&
            destinationDatabase != null &&
            !string.IsNullOrWhiteSpace(startDestinationId) &&
            player != null &&
            dungeonManager != null)
            return;

        Warn("Required references are incomplete.");
        _warnedMissingReferences = true;
#endif
    }

    private void Warn(string message)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning("[TownDungeonTransitionManager] " + message, this);
#endif
    }
}
