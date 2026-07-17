using UnityEngine;
public class LocationTransitionManager : MonoBehaviour
{
    public static LocationTransitionManager Active { get; private set; }

    [Header("Roots")]
    [SerializeField] private GameObject townRoot;
    [SerializeField] private GameObject dungeonRoot;
    [SerializeField] private GameObject minimapRoot;

    [Header("Destinations")]
    [SerializeField] private TeleportDestinationDatabase destinationDatabase;
    [SerializeField, TeleportDestinationId] private string startDestinationId = "town_start";
    [SerializeField, TeleportDestinationId] private string debugDungeonEntranceDestinationId = "dungeon_entrance";
    [SerializeField, TeleportDestinationId] private string debugReturnDestinationId = "town_return";

    [Header("Dependencies")]
    [SerializeField] private PlayerController player;
    [SerializeField] private DungeonManager dungeonManager;
    [SerializeField] private FogOfWarController fogOfWar;
    [SerializeField] private RoomSpawner roomSpawner;
    [SerializeField] private EngravingStationPlacer engravingStationPlacer;
    [SerializeField] private MinimapController minimap;
    [SerializeField] private TeleportFadeOverlay teleportFadeOverlay;

    [Header("Flow")]
    [SerializeField] private bool generateNewDungeonOnEnter = true;
    [SerializeField] private bool resetFloorOnNewDungeonRun = true;
    [SerializeField] private bool disableCombatInTown = true;

    private bool _warnedMissingReferences;
    private bool _warnedMissingRunCore;
    private bool _isChangingLocation;
    private TeleportLocationData _currentDestination;

    public GameLocationType CurrentLocation { get; private set; }
    public bool IsInTown => CurrentLocation == GameLocationType.Town;
    public bool IsInDungeon => CurrentLocation == GameLocationType.Dungeon;
    public bool ShouldBlockCombat => disableCombatInTown && IsInTown;
    public bool StartsInTown => !TryGetLocation(startDestinationId, out TeleportLocationData startLocation) ||
                                startLocation.LocationType == GameLocationType.Town;
    internal string DebugDungeonEntranceDestinationId => debugDungeonEntranceDestinationId;
    internal string DebugReturnDestinationId => debugReturnDestinationId;

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

    private void OnDestroy()
    {
        if (ReferenceEquals(Active, this))
            Active = null;
    }

    public void TeleportPlayer(PlayerController targetPlayer, string destinationId)
    {
        TryTeleportPlayer(targetPlayer, destinationId);
    }

    public bool TryTeleportPlayer(PlayerController targetPlayer, string destinationId)
    {
        if (targetPlayer == null || _isChangingLocation)
            return false;

        if (!TryGetLocation(destinationId, out TeleportLocationData destination))
            return false;

        GameLocationType from = CurrentLocation;
        GameLocationType to = destination.LocationType;
        bool enteringDungeon = from != GameLocationType.Dungeon && to == GameLocationType.Dungeon;
        bool leavingDungeon = from == GameLocationType.Dungeon && to != GameLocationType.Dungeon;

        _isChangingLocation = true;

        if (leavingDungeon)
            CleanupDungeonRuntime();

        ApplyLocationRoots(to);
        CurrentLocation = to;
        bool moved = false;

        if (to == GameLocationType.Dungeon)
        {
            if (enteringDungeon)
            {
                StartNewDungeonRun(targetPlayer);
                moved = true;
            }
            else
            {
                moved = TryMovePlayerToDestination(targetPlayer, destination);
            }

        }
        else
        {
            moved = TryMovePlayerToDestination(targetPlayer, destination);
        }

        _currentDestination = destination;
        ApplyMinimapSourceForLocation(destination);
        if (moved)
            teleportFadeOverlay?.TriggerFade();

        _isChangingLocation = false;
        return moved;
    }

    public void RefreshMinimapForCurrentLocation()
    {
        if (_currentDestination != null)
            ApplyMinimapSourceForLocation(_currentDestination);
        else if (CurrentLocation == GameLocationType.Dungeon)
            minimap?.SetDungeonSource();
    }

    private void ApplyMinimapSourceForLocation(TeleportLocationData destination)
    {
        if (minimap == null)
            return;

        if (destination == null)
        {
            if (CurrentLocation == GameLocationType.Dungeon)
                minimap.SetDungeonSource();
            return;
        }

        if (destination.LocationType != GameLocationType.Dungeon)
        {
            minimap.SetTilemapSource(destination.MinimapLocationId);
            return;
        }

        if (ShouldUseTilemapMinimap(destination))
            minimap.SetTilemapSource(destination.MinimapLocationId);
        else
            minimap.SetDungeonSource();
    }

    private static bool ShouldUseTilemapMinimap(TeleportLocationData destination)
    {
        if (destination == null)
            return false;

        string minimapId = destination.MinimapLocationId;
        return !string.IsNullOrWhiteSpace(minimapId) &&
               (destination.UseTilemapMinimap || LocationMinimapRegistry.Contains(minimapId));
    }

    public void RestoreDungeonMinimapSource()
    {
        if (CurrentLocation != GameLocationType.Dungeon)
            return;

        _currentDestination = null;
        minimap?.SetDungeonSource();
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
        RefreshMinimapForCurrentLocation();
        _isChangingLocation = false;
    }

    private bool TryMovePlayerToDestination(PlayerController targetPlayer, TeleportLocationData destination)
    {
        if (targetPlayer == null || destination == null)
            return false;

        if (!LocationRootRegistry.TryGet(destination.LocationRootId, out LocationRoot root))
            return false;

        Vector3 worldPosition = root.transform.TransformPoint(destination.LocalSpawnPosition);
        targetPlayer.TeleportTo(worldPosition);
        return true;
    }

    private void StartNewDungeonRun(PlayerController targetPlayer)
    {
        engravingStationPlacer?.ClearRuntimeState();

        if (generateNewDungeonOnEnter && resetFloorOnNewDungeonRun)
            dungeonManager.floor = 1;

        if (generateNewDungeonOnEnter || dungeonManager.Data == null)
            dungeonManager.Generate();

        fogOfWar?.RequestFullInitialize();
        roomSpawner?.ResetRoomEncounterState();
        GrantRunCoreIfNeeded(targetPlayer);
        targetPlayer?.SpawnAtStart();
    }

    private void GrantRunCoreIfNeeded(PlayerController targetPlayer)
    {
        PlayerInventory inventory = targetPlayer != null ? targetPlayer.Inventory : null;
        if (inventory == null || HasInventoryItemCode(inventory, ItemCodes.RunCore))
            return;

        if (!inventory.TryGetDatabaseItem(ItemCodes.RunCore, out ItemData coreTemplate) || coreTemplate == null)
        {
            WarnMissingRunCore();
            return;
        }

        inventory.AddItem(coreTemplate.CreateRuntimeClone(), 1);
    }

    private static bool HasInventoryItemCode(PlayerInventory inventory, string itemCode)
    {
        if (inventory == null || string.IsNullOrWhiteSpace(itemCode))
            return false;

        System.Collections.Generic.IReadOnlyList<InventoryItemStack> items = inventory.Items;
        for (int i = 0; i < items.Count; i++)
        {
            ItemData item = items[i]?.Item;
            if (item != null && item.ItemCode == itemCode)
                return true;
        }

        return false;
    }

    private void WarnMissingRunCore()
    {
        if (_warnedMissingRunCore)
            return;

        _warnedMissingRunCore = true;
        Debug.LogWarning("[LocationTransitionManager] Core item not found in item database.", this);
    }

    private void CleanupDungeonRuntime()
    {
        if (player != null)
        {
            PlayerInventory inv = player.Inventory;
            if (inv != null)
                inv.RemoveItemsOnDungeonExit();

            EngravingLoadout engravings = player.GetComponent<EngravingLoadout>();
            if (engravings != null)
                engravings.ClearAll();

            PlayerCombatController combat = player.GetComponent<PlayerCombatController>();
            if (combat != null)
            {
                combat.ResetCombo();
                combat.ClearAllProcSkillSequences();
            }

            PlayerFormController forms = player.GetComponent<PlayerFormController>();
            if (forms != null)
                forms.SetCurrentForm(PlayerFormId.Normal);
        }
        ProjectilePool.ReleaseAllActiveProjectiles(ProjectileReleaseReason.Manual);
        EnemyPoolManager.ReleaseAllActiveEnemiesForLocationChange();
        DropItemSpawner.Instance?.ClearAllActiveDrops();
        DamageZoneSpawner.Instance?.ClearAllActiveZones();
        roomSpawner?.ClearRuntimeEncounterState();
        engravingStationPlacer?.ClearRuntimeState();
    }

    private void ApplyLocationRoots(GameLocationType locationType)
    {
        bool isDungeon = locationType == GameLocationType.Dungeon;
        SetRootActive(townRoot, !isDungeon);
        SetRootActive(dungeonRoot, isDungeon);
        SetRootActive(minimapRoot, true);
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
        Debug.LogWarning("[LocationTransitionManager] " + message, this);
#endif
    }
}
