using UnityEngine;
using UnityEngine.InputSystem;

public class TownDungeonTransitionManager : MonoBehaviour
{
    public static TownDungeonTransitionManager Active { get; private set; }

    [Header("Roots")]
    [SerializeField] private GameObject townRoot;
    [SerializeField] private GameObject dungeonRoot;
    [SerializeField] private GameObject minimapRoot;

    [Header("Spawn Points")]
    [SerializeField] private Transform townSpawnPoint;
    [SerializeField] private Transform townReturnSpawnPoint;

    [Header("Dependencies")]
    [SerializeField] private PlayerController player;
    [SerializeField] private DungeonManager dungeonManager;
    [SerializeField] private FogOfWarController fogOfWar;
    [SerializeField] private RoomSpawner roomSpawner;

    [Header("Flow")]
    [SerializeField] private GameLocationType startLocation = GameLocationType.Town;
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
    public bool StartsInTown => startLocation == GameLocationType.Town;

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
        if (startLocation == GameLocationType.Town)
            EnterTown(spawnAtReturnPoint: false);
        else
            EnterDungeon();
    }

    private void Update()
    {
        if (!enableDebugReturnKey || !IsInDungeon)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.tKey.wasPressedThisFrame)
            EnterTown(spawnAtReturnPoint: true);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Active, this))
            Active = null;
    }

    public void EnterDungeon()
    {
        if (_isChangingLocation)
            return;

        _isChangingLocation = true;
        CurrentLocation = GameLocationType.Dungeon;
        SetRootActive(townRoot, false);
        SetRootActive(dungeonRoot, true);
        SetRootActive(minimapRoot, true);

        if (generateNewDungeonOnEnter && resetFloorOnNewDungeonRun)
            dungeonManager.floor = 1;

        if (generateNewDungeonOnEnter || dungeonManager.Data == null)
            dungeonManager.Generate();

        fogOfWar?.RequestFullInitialize();
        roomSpawner?.ResetRoomEncounterState();
        player?.SpawnAtStart();
        _isChangingLocation = false;
    }

    public void EnterTown()
    {
        EnterTown(spawnAtReturnPoint: true);
    }

    private void EnterTown(bool spawnAtReturnPoint)
    {
        if (_isChangingLocation)
            return;

        _isChangingLocation = true;
        CurrentLocation = GameLocationType.Town;

        ProjectilePool.ReleaseAllActiveProjectiles(ProjectileReleaseReason.Manual);
        EnemyPoolManager.ReleaseAllActiveEnemiesForLocationChange();
        roomSpawner?.ClearRuntimeEncounterState();

        SetRootActive(dungeonRoot, false);
        SetRootActive(townRoot, true);
        SetRootActive(minimapRoot, false);

        MovePlayerToTownSpawn(spawnAtReturnPoint);
        _isChangingLocation = false;
    }

    private void MovePlayerToTownSpawn(bool spawnAtReturnPoint)
    {
        if (player == null)
            return;

        Transform spawn = spawnAtReturnPoint && townReturnSpawnPoint != null
            ? townReturnSpawnPoint
            : townSpawnPoint;
        if (spawn == null)
            return;

        player.TeleportTo(spawn.position);
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
            townSpawnPoint != null &&
            player != null &&
            dungeonManager != null)
            return;

        Debug.LogWarning("[TownDungeonTransitionManager] Required references are incomplete.", this);
        _warnedMissingReferences = true;
#endif
    }
}
