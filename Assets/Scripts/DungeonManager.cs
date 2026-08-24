// ═══════════════════════════════════════════════════════════════════
//  DungeonManager.cs
//  Application Layer — 던전 생애주기 조율
//
//  책임:
//    • 던전 설정값 보유
//    • 생성 파이프라인 조율 (Generator → Data → Registry → Renderer)
//    • 층 이동 (NextFloor, PrevFloor)
//    • 외부에서 데이터 쿼리를 위한 위임 API 제공
//    • 직접 구현하지 않고 각 전담 클래스에 위임
// ═══════════════════════════════════════════════════════════════════

using System.Collections;
using System.Globalization;
using UnityEngine;

public class DungeonManager : MonoBehaviour
{
    private static readonly bool AllowForcedGarbageCollectionDuringFloorTransition = false;
    private const int MIN_FLOOR = 1;
    private const int MAX_FLOOR = 100;
    public static DungeonManager Instance { get; private set; }

    // ── Inspector 연결 ───────────────────────────────────────────────

    [Header("Dependencies")]
    [Tooltip("Tilemap 렌더링 담당 컴포넌트")]
    public DungeonTilemapRenderer dungeonRenderer;

    [Tooltip("이벤트 채널 (ScriptableObject Asset)")]
    public DungeonEventChannel eventChannel;

    [Tooltip("층 이동 시 표시할 로딩 화면 (선택)")]
    public LoadingScreenController loadingScreen;

    [SerializeField, Tooltip("방 전투 시작/초기화 상태를 관리하는 RoomSpawner")]
    private RoomSpawner roomSpawner;

    [SerializeField, Tooltip("보스층 매핑 테이블")]
    private BossEncounterTable bossTable;

    [Header("Dungeon Settings")]
    [Tooltip("시드. 0이면 매 생성마다 랜덤 생성 후 저장.")]
    public long seed = 0;

    [Tooltip("현재 층수 (1 ~ maxFloor)")]
    [Range(MIN_FLOOR, MAX_FLOOR)]
    public int floor = 1;

    [Tooltip("맵 너비 (타일 수)")]
    public int mapWidth = 80;

    [Tooltip("맵 높이 (타일 수)")]
    public int mapHeight = 50;

    [Tooltip("방 최소 크기")]
    public int minRoomSize = 5;

    [Tooltip("방 최대 크기")]
    public int maxRoomSize = 14;

    [Tooltip("BSP 분할 깊이")]
    [Range(1, 7)]
    public int bspDepth = 4;

    [Tooltip("MST 완료 후 각 EXTRA 엣지 후보에서 통로 생성을 시도할 확률")]
    [Range(0f, 1f)]
    public float extraConnProb = 0.5f;

    [Tooltip("각 방 pair마다 생성/점수화할 EXTRA path 후보 수")]
    [Min(0)]
    public int extraCandidateCount = 12;

    [Tooltip("각 방마다 EXTRA 통로 엣지 후보로 고려할 최근접 이웃 방 수 (k)")]
    [Min(0)]
    public int extraNeighborCount = 3;

    [Tooltip("기존 corridor와 겹치는 cell 1개당 EXTRA 후보 점수 보너스")]
    [Min(0)]
    public int extraOverlapScoreWeight = 20;

    [Tooltip("path cell 1개당 EXTRA 후보 점수 감점")]
    [Min(0)]
    public int extraPathLengthPenaltyWeight = 8;

    [Tooltip("두 방 중심 거리 제곱 감점 divisor. 값이 클수록 거리 감점이 약해집니다.")]
    [Min(1)]
    public int extraCenterDistancePenaltyDivisor = 20;

    [Header("Monster Den")]
    [Tooltip("출현 확률, 떨어지면 0개")]
    [Range(0f, 1f)]
    public float monsterDenChance = 0.05f;

    [Tooltip("층당 최대 개수")]
    [Min(0)]
    public int maxMonsterDenCount = 1;

    [Header("Stair")]
    [SerializeField, Tooltip("계단이 들어가면 안 되는 방 타입입니다. Spawn/Elite는 항상 제외됩니다.")]
    private RoomType[] stairAvoidTypes;

    [Header("Spawn Region")]
    [Tooltip("현재 층/스테이지 지역입니다. EnemyData.allowedRegions는 여러 지역을 허용할 수 있지만, 이 값은 단일 지역만 사용합니다.")]
    public SpawnRegion currentStageRegion = SpawnRegion.Dungeon;

    [Header("Floor Transition Stabilization")]
    [Tooltip("After generation, keep the loading screen visible while Unity settles Tilemap/render work.")]
    [Min(0f)]
    public float postGenerateSettleSeconds = 0.25f;

    [Tooltip("Extra frames to wait under the loading screen after generation and optional GC.")]
    [Min(0)]
    public int postGenerateSettleFrames = 2;

    [Tooltip("Run a full GC during the loading screen. Leave off unless logs show gameplay GC after long runs.")]
    public bool collectGarbageDuringFloorTransition = false;

    [Tooltip("Full GC passes during loading. One pass is usually enough; higher values can add long loading hitches.")]
    [Range(0, 2)]
    public int floorTransitionGcPasses = 0;

    [Tooltip("Wait for pending finalizers during floor loading. Enable only when logs show managed finalizers are the issue.")]
    public bool waitForFinalizersDuringFloorTransition = false;

    [Header("Tilemap Chunked Loading")]
    [Tooltip("During floor transitions, split Tilemap SetTilesBlock into row chunks across multiple frames.")]
    public bool useChunkedTilePlacementDuringFloorTransition = true;

    [Tooltip("Rows per Tilemap chunk during floor transitions. Smaller values reduce per-frame hitches but add more loading frames.")]
    [Range(1, 50)]
    public int tilePlacementChunkRows = 8;

    // ── 도메인 객체 ─────────────────────────────────────────────────
    private DungeonData            _data;
    private RoomRegistry           _registry;
    private Vector2Int             _cachedSpawnPos;   // Generate 시 계산 후 캐싱
    private RoomInfo?              _currentDoorRoom;
    private DungeonQueryService    _queryService;
    private SpawnPositionService   _spawnService;
    private FloorTransitionService _transitionService;
    private BossEncounterController _subscribedBossController;
    private BossEncounterController _pendingBossProceedController;
    private int _pendingBossProceedTargetFloor;

    // 층 전환 중복 방지 — 코루틴 실행 중 추가 요청을 차단
    private bool _isTransitioning = false;
    private bool _warnedMissingRoomSpawner;

    // 그리드 캐싱
    int[,] _originGrid;

    // ── 공개 프로퍼티 ────────────────────────────────────────────────
    public DungeonData   Data     => _data;
    public RoomRegistry  Registry => _registry;
    public bool IsTransitioning => _isTransitioning;
    public RoomInfo? CurrentDoorRoom => _currentDoorRoom;
    public int CurrentFloor => floor;
    public int MinFloor => MIN_FLOOR;
    public int MaxFloor => MAX_FLOOR;

    private void Awake()
    {
        WarnIfInvalidCurrentStageRegion();

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 전역 던전 접근 지점입니다. 풀링된 적/스폰 시스템은 인스펙터 참조 대신 이 싱글톤을 사용합니다.
        Instance = this;

        // 쿼리 서비스 초기화 — dungeonRenderer는 Inspector에서 주입된 상태
        if (dungeonRenderer == null)
            Debug.LogWarning("[DungeonManager] Awake: dungeonRenderer가 없습니다 — 좌표 변환이 동작하지 않습니다.");
        if (eventChannel == null)
            Debug.LogWarning("[DungeonManager] Awake: eventChannel이 없습니다 — 층 변경 이벤트가 발행되지 않습니다.");

        EnsureServices();
    }

    private void OnEnable()
    {
        TrySubscribeBossEncounter();
    }

    private void OnDisable()
    {
        UnsubscribeBossEncounter();
    }

    // ── 생성 파이프라인 ──────────────────────────────────────────────

    [ContextMenu("Generate Dungeon")]
    public void Generate()
    {
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("generate_begin",
                "floor=" + floor + " seed=" + seed + " size=" + mapWidth + "x" + mapHeight);

        if (dungeonRenderer == null)
        {
            Debug.LogError("[DungeonManager] DungeonTilemapRenderer가 연결되지 않았습니다.");
            return;
        }

        ResetRoomEncounterState();
        RunGenerationPipeline();

        // 7. Tilemap 배치
        using (PerfStage.Begin("generate_stage_place_tiles"))
        {
            dungeonRenderer.PlaceTiles(_data);
        }
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("generate_end",
                "floor=" + floor + " spawn=" + _cachedSpawnPos.x + ":" + _cachedSpawnPos.y);

        Debug.Log($"[DungeonManager] 생성 완료 — Seed: {seed}, Floor: {floor}");
    }

    [ContextMenu("Generate With New Seed")]
    public void GenerateWithNewSeed()
    {
        seed = DungeonGenerator.GenerateSeed();
        Generate();
        Debug.Log($"[DungeonManager] New Seed: {seed}");
    }

    public void NextFloor() { TryTransitionToFloor(floor + 1, out _); }
    public void PrevFloor() { TryTransitionToFloor(floor - 1, out _); }

    public bool TryTransitionToFloor(int targetFloor, out string message)
    {
        LocationTransitionManager locationManager = LocationTransitionManager.Active;
        if (locationManager != null && !locationManager.IsInDungeon)
        {
            message = "Floor command can only be used in Dungeon.";
            return false;
        }

        if (_isTransitioning)
        {
            message = "Floor transition is already in progress.";
            return false;
        }

        if (targetFloor < MIN_FLOOR || targetFloor > MAX_FLOOR)
        {
            message = "Invalid floor: floor must be between " + MIN_FLOOR + " and " + MAX_FLOOR + ".";
            return false;
        }

        if (targetFloor == floor)
        {
            message = "Already on floor " + floor + ".";
            return false;
        }

        int bossSelectSeed = DeterministicSeedUtility.CreateSeed(
            seed,
            _data != null ? (int)_data.currentStageRegion : 0,
            targetFloor,
            0,
            DeterministicSeedUtility.BossSelectDomain);
        var bossSelectRng = new System.Random(bossSelectSeed);
        if (bossTable != null && bossTable.TryGetBoss(targetFloor, bossSelectRng, out BossEncounterEntry bossEntry))
            return TryEnterBossFloor(targetFloor, bossEntry, out message);

        StartCoroutine(FloorTransition(targetFloor));
        message = "Moving to floor " + targetFloor + ".";
        return true;
    }

    public void GenerateAt(long dungeonSeed, int dungeonFloor)
    {
        seed  = dungeonSeed;
        floor = dungeonFloor;
        Generate();
    }

    /// <summary>
    /// 층 이동 코루틴.
    ///
    /// 실행 순서:
    ///   1. 로딩 화면 페이드 인          (UI 반응)
    ///   2. 던전 생성 + Tilemap 배치      (무거운 연산 — 로딩 중 수행)
    ///   3. 한 프레임 대기                (렌더러가 타일을 처리할 시간)
    ///   4. 플레이어 스폰 이벤트 발행
    ///   5. 로딩 화면 페이드 아웃
    /// </summary>
    private System.Collections.IEnumerator FloorTransition(int targetFloor)
    {
        _isTransitioning = true;
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("floor_transition_begin",
                "from=" + floor + " target=" + targetFloor);

        CleanupDungeonRuntimeObjectsForFloorTransition();
        CleanupPlayerInventoryForFloorTransition();

        int prev = floor;
        floor = targetFloor;
        double stageStart = Time.realtimeSinceStartupAsDouble;

        // 1. 로딩 화면 표시
        if (loadingScreen != null)
        {
            if (RuntimePerfLogger.IsActive)
                RuntimePerfLogger.MarkEvent("floor_transition_loading_show_begin", "floor=" + floor);
            yield return StartCoroutine(loadingScreen.Show());
            if (RuntimePerfLogger.IsActive)
                RuntimePerfLogger.MarkEvent("floor_transition_loading_show_end",
                    "elapsedMs=" + ElapsedMs(stageStart));
        }
        else
        {
            yield return null;
        }

        // 2. 던전 생성 (무거운 연산 — 로딩 화면 뒤에서 수행)
        stageStart = Time.realtimeSinceStartupAsDouble;
        yield return GenerateForFloorTransition(useChunkedTilePlacementDuringFloorTransition);
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("floor_transition_generate_end",
                "elapsedMs=" + ElapsedMs(stageStart) + " floor=" + floor);

        yield return _transitionService.RunPostGenerateSettle(
            postGenerateSettleSeconds,
            postGenerateSettleFrames,
            AllowForcedGarbageCollectionDuringFloorTransition,
            collectGarbageDuringFloorTransition,
            floorTransitionGcPasses,
            waitForFinalizersDuringFloorTransition,
            floor);

        // 4. 층 변경 이벤트 발행
        stageStart = Time.realtimeSinceStartupAsDouble;
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("floor_transition_event_raise_begin",
                "prev=" + prev + " current=" + floor);
        eventChannel?.RaiseFloorChanged(prev, floor);
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("floor_transition_event_raised",
                "prev=" + prev + " current=" + floor +
                " elapsedMs=" + ElapsedMs(stageStart) +
                " dtMs=" + (Time.unscaledDeltaTime * 1000f).ToString("F3", CultureInfo.InvariantCulture));

        // 5. 로딩 화면 숨김
        if (loadingScreen != null)
        {
            stageStart = Time.realtimeSinceStartupAsDouble;
            if (RuntimePerfLogger.IsActive)
                RuntimePerfLogger.MarkEvent("floor_transition_loading_hide_begin",
                    "floor=" + floor +
                    " dtMs=" + (Time.unscaledDeltaTime * 1000f).ToString("F3", CultureInfo.InvariantCulture));
            yield return StartCoroutine(loadingScreen.Hide());
            if (RuntimePerfLogger.IsActive)
                RuntimePerfLogger.MarkEvent("floor_transition_loading_hide_end",
                    "elapsedMs=" + ElapsedMs(stageStart) +
                    " dtMs=" + (Time.unscaledDeltaTime * 1000f).ToString("F3", CultureInfo.InvariantCulture));
        }

        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("floor_transition_end", "floor=" + floor);

        CompletePendingBossProceedIfNeeded(targetFloor);
        _isTransitioning = false;
    }

    private bool TryEnterBossFloor(int targetFloor, BossEncounterEntry entry, out string message)
    {
        PlayerController player = PlayerController.Active;
        if (player == null)
        {
            message = "Player controller is not active.";
            return false;
        }

        TrySubscribeBossEncounter();

        RestAreaController restArea = RestAreaController.Active;
        if (restArea != null)
        {
            int previousRestFloor = floor;
            floor = targetFloor;
            if (!restArea.Begin(entry, player))
            {
                floor = previousRestFloor;
                message = "Failed to enter rest area for floor " + targetFloor + ".";
                return false;
            }

            message = "Entering rest area before boss floor " + targetFloor + ".";
            return true;
        }

        BossEncounterController bossController = BossEncounterController.Active;
        if (bossController == null)
        {
            message = "Boss encounter controller is not active.";
            return false;
        }

        int previousFloor = floor;
        floor = targetFloor;
        if (!bossController.Begin(entry, player))
        {
            floor = previousFloor;
            message = "Failed to enter boss area for floor " + targetFloor + ".";
            return false;
        }

        message = "Entering boss area for floor " + targetFloor + ".";
        return true;
    }

    private void TrySubscribeBossEncounter()
    {
        BossEncounterController controller = BossEncounterController.Active;
        if (controller == null || controller == _subscribedBossController)
            return;

        UnsubscribeBossEncounter();
        _subscribedBossController = controller;
        _subscribedBossController.ProceedRequested += HandleBossProceedRequested;
    }

    private void UnsubscribeBossEncounter()
    {
        if (_subscribedBossController == null)
            return;

        _subscribedBossController.ProceedRequested -= HandleBossProceedRequested;
        _subscribedBossController = null;
    }

    private void HandleBossProceedRequested(BossEncounterEntry entry, PlayerController player)
    {
        if (entry == null || entry.IsFinal)
            return;

        BossEncounterController controller = _subscribedBossController != null
            ? _subscribedBossController
            : BossEncounterController.Active;
        int targetFloor = floor + 1;

        if (!TryTransitionToFloor(targetFloor, out string message))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[DungeonManager] Boss proceed failed: " + message, this);
#endif
            controller?.ResetProceedRequest();
            return;
        }

        _pendingBossProceedController = controller;
        _pendingBossProceedTargetFloor = targetFloor;
    }

    private void CompletePendingBossProceedIfNeeded(int completedFloor)
    {
        if (_pendingBossProceedController == null ||
            _pendingBossProceedTargetFloor != completedFloor)
        {
            return;
        }

        BossEncounterController controller = _pendingBossProceedController;
        _pendingBossProceedController = null;
        _pendingBossProceedTargetFloor = 0;
        controller.CompleteProceedToNextFloor();
    }

    private IEnumerator GenerateForFloorTransition(bool useChunked)
    {
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("generate_begin",
                "floor=" + floor + " seed=" + seed + " size=" + mapWidth + "x" + mapHeight +
                (useChunked ? " chunked=true" : ""));

        if (dungeonRenderer == null)
        {
            Debug.LogError("[DungeonManager] DungeonTilemapRenderer가 연결되지 않았습니다.");
            yield break;
        }

        RunGenerationPipeline();

        // 7. Tilemap 배치
        double stageStart = Time.realtimeSinceStartupAsDouble;
        if (useChunked)
            yield return StartCoroutine(dungeonRenderer.PlaceTilesChunked(_data, tilePlacementChunkRows));
        else
            dungeonRenderer.PlaceTiles(_data);

        if (RuntimePerfLogger.IsActive)
        {
            RuntimePerfLogger.MarkEvent("generate_stage_place_tiles",
                "elapsedMs=" + ElapsedMs(stageStart) +
                (useChunked ? " chunkRows=" + tilePlacementChunkRows : ""));
            RuntimePerfLogger.MarkEvent("generate_end",
                "floor=" + floor + " spawn=" + _cachedSpawnPos.x + ":" + _cachedSpawnPos.y +
                (useChunked ? " chunked=true" : ""));
        }

        Debug.Log($"[DungeonManager] 생성 완료 — Seed: {seed}, Floor: {floor}");
    }

    // ── 데이터 쿼리 위임 API ─────────────────────────────────────────
    // PlayerController 등이 DungeonManager 하나만 참조해도 되도록 위임합니다.
    // 구현은 DungeonQueryService에 있으며, 시그니처는 하위 호환을 위해 그대로 유지합니다.

    public bool IsWalkable(int col, int row)
        => _queryService.IsWalkable(col, row);

    /// <summary>
    /// 월드 좌표를 중심으로 한 정사각형 footprint(반경 radius)의 4 코너가 모두 walkable인지 검사합니다.
    /// 공통 라우팅(<see cref="WalkabilityQuery"/>)에 위임해 Area/Dungeon 모두 동일한 의미로 동작합니다.
    /// </summary>
    public bool IsFootprintWalkable(Vector3 worldPosition, float radius)
        => WalkabilityQuery.IsFootprintWalkable(worldPosition, radius);

    public int GetTileType(int col, int row)
        => _queryService.GetTileType(col, row);

    /// <summary>그리드 좌표가 속한 방을 타입 정보 포함해 반환합니다.</summary>
    public RoomInfo? GetRoomAt(int col, int row)
        => _queryService.GetRoomAt(col, row);

    /// <summary>스폰 위치를 반환합니다. Generate() 시점에 계산된 캐시를 반환하므로 O(1).</summary>
    public Vector2Int GetSpawnTilePos() => _cachedSpawnPos;

    /// <summary>그리드 좌표를 월드 좌표로 변환합니다 (QueryService → Renderer에 위임).</summary>
    public Vector3 GridToWorld(Vector2Int gridPos)
        => _queryService.GridToWorld(gridPos);

    /// <summary>월드 좌표를 그리드 좌표로 변환합니다 (QueryService → Renderer에 위임).</summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
        => _queryService.WorldToGrid(worldPos);

    /// <summary>방 타입을 변경합니다 (Registry에 위임).</summary>
    public void SetRoomType(RoomInfo room, RoomType type)
        => _registry?.SetRoomType(room, type);

    public void CloseCurrentRoomDoors(RoomInfo room)
    {
        _currentDoorRoom = room;
        if (dungeonRenderer == null) return;

        dungeonRenderer.CloseDoorsForRoom(room);
        eventChannel?.RaiseRoomDoorsClosed(room);
    }

    public void OpenCurrentRoomDoors()
    {
        // 문 개폐는 DungeonManager.Instance를 통해 중앙에서만 처리합니다.
        // RoomSpawner는 방 클리어 상태만 판단하고, 실제 타일맵 문 제어는 여기로 위임합니다.
        ClearPendingRoomStart();

        if (dungeonRenderer == null || !dungeonRenderer.OpenAllDoors())
            return;

        RoomInfo? openedRoom = _currentDoorRoom;
        _currentDoorRoom = null;
        if (openedRoom.HasValue)
            eventChannel?.RaiseRoomDoorsOpened(openedRoom.Value);
    }

    // Developer Console 전용. DeveloperConsoleCommandExecutor 외부 호출 금지.
    internal int OpenDebugNormalDoors()
    {
        ClearPendingRoomStart();

        if (dungeonRenderer == null)
            return 0;

        int openedCount = dungeonRenderer.OpenNormalDoors();
        if (openedCount <= 0)
            return 0;

        RoomInfo? openedRoom = _currentDoorRoom;
        _currentDoorRoom = null;
        if (openedRoom.HasValue)
            eventChannel?.RaiseRoomDoorsOpened(openedRoom.Value);

        return openedCount;
    }

    // Developer Console 전용. DeveloperConsoleCommandExecutor 외부 호출 금지.
    internal int OpenDebugEliteDoors()
    {
        if (dungeonRenderer == null)
            return 0;

        return dungeonRenderer.OpenAllEliteDoors();
    }

    private void ClearPendingRoomStart()
    {
        if (!TryGetRoomSpawner(out RoomSpawner spawner))
            return;

        spawner.ClearPendingRoomStart();
    }

    private static void CleanupPlayerInventoryForFloorTransition()
    {
        PlayerController pc = PlayerController.Active;
        if (pc == null) return;
        PlayerInventory inv = pc.Inventory;
        if (inv != null)
            inv.RemoveItemsOnFloorTransition();
    }

    private void CleanupDungeonRuntimeObjectsForFloorTransition()
    {
        if (TryGetRoomSpawner(out RoomSpawner spawner))
            spawner.ClearRuntimeEncounterState();
        EnemyPoolManager.ReleaseAllActiveEnemiesForLocationChange();
        ProjectilePool.ReleaseAllActiveProjectiles(ProjectileReleaseReason.FloorTransition);
        if (DropItemSpawner.Instance != null)
            DropItemSpawner.Instance.ClearAllActiveDrops();
        DamageZoneSpawner.Instance?.ClearAllActiveZones();
        PlayerController.Active?.GetComponent<PlayerCombatController>()?.ClearAllProcSkillSequences();
    }

    private void ResetRoomEncounterState()
    {
        if (!TryGetRoomSpawner(out RoomSpawner spawner))
            return;

        spawner.ResetRoomEncounterState();
    }

    private void PrepareEliteKeyPlan()
    {
        if (!TryGetRoomSpawner(out RoomSpawner spawner))
            return;

        spawner.PrepareEliteKeyPlan(this);
    }

    private bool TryGetRoomSpawner(out RoomSpawner spawner)
    {
        spawner = roomSpawner;
        if (spawner != null)
            return true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_warnedMissingRoomSpawner)
        {
            Debug.LogWarning(
                "[DungeonManager] roomSpawner 참조가 없습니다. Room encounter 상태 초기화/대기 해제가 생략됩니다.",
                this);
            _warnedMissingRoomSpawner = true;
        }
#endif
        return false;
    }

    // ── 내부 빌더 ────────────────────────────────────────────────────

    private void RunGenerationPipeline()
    {
        // Edit Mode ContextMenu / CustomEditor 버튼이 Awake를 거치지 않고 Generate를 호출할 수 있으므로
        // 여기서 한 번 더 서비스를 보장합니다. Awake 경로에서는 idempotent noop.
        EnsureServices();

        // 1. 설정 구성
        double stageStart = Time.realtimeSinceStartupAsDouble;
        var settings = BuildSettings();
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("generate_stage_build_settings",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " seed=" + settings.Seed +
                " bspDepth=" + settings.BspDepth);

        // 2. 그리드 + 원시 방 목록 생성
        stageStart = Time.realtimeSinceStartupAsDouble;
        DungeonGenerator.RoomRect[] rawRooms;
        DungeonGenerator.DungeonLayoutInfo layoutInfo;
        int[,] grid = DungeonGenerator.GenerateDungeon(settings, out rawRooms, out layoutInfo);
        WarnIfEliteRoomFallback(layoutInfo);
        _originGrid = grid;
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("generate_stage_generator",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " rawRooms=" + rawRooms.Length +
                " grid=" + grid.GetLength(1) + "x" + grid.GetLength(0));

        // 3. RoomInfo 배열 생성 (타입은 Registry가 결정)
        stageStart = Time.realtimeSinceStartupAsDouble;
        _registry = new RoomRegistry();
        var roomInfos = BuildRoomInfos(rawRooms);
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("generate_stage_room_infos",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " roomInfos=" + roomInfos.Length);

        // 4. DungeonData 생성
        stageStart = Time.realtimeSinceStartupAsDouble;
        _data = new DungeonData(grid, roomInfos);
        WarnIfInvalidCurrentStageRegion();
        _data.currentStageRegion = currentStageRegion;
        // CountWalkableTiles는 O(W*H) 비용 — 가드 안에서만 호출해 OFF 상태에서 회피합니다.
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("generate_stage_data_construct",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " walkable=" + CountWalkableTiles(grid));

        // 5. Registry 초기화
        using (PerfStage.Begin("generate_stage_registry_init"))
        {
            _registry.Initialize(_data);
        }

        // 쿼리 서비스에 최신 데이터 주입 (Registry 초기화 완료 후)
        _queryService?.UpdateData(_data, _registry, _originGrid);

        // 6. 스폰 위치 미리 계산 및 캐싱 (GetSpawnTilePos 호출 시 재계산 불필요)
        stageStart = Time.realtimeSinceStartupAsDouble;
        _cachedSpawnPos = _spawnService.ComputeSpawnPos(_data, mapWidth, mapHeight);
        var spawnRoom = GetRoomAt(_cachedSpawnPos.x, _cachedSpawnPos.y);
        var excludeSpawnKey = spawnRoom.HasValue
            ? (spawnRoom.Value.X, spawnRoom.Value.Y)
            : (int.MinValue, int.MinValue);
        var denRng = new System.Random(DeterministicSeedUtility.CreateSeed(
            seed, (int)currentStageRegion, floor, 0, DeterministicSeedUtility.MonsterDenDomain));
        _registry.AssignMonsterDens(_data, denRng, monsterDenChance, maxMonsterDenCount, excludeSpawnKey);
        PrepareEliteKeyPlan();
        PlaceStairForFloor(settings, roomInfos, grid, excludeSpawnKey);
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("generate_stage_spawn_cache",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " spawn=" + _cachedSpawnPos.x + ":" + _cachedSpawnPos.y);
    }

    private void PlaceStairForFloor(
        DungeonSettings settings,
        RoomInfo[] roomInfos,
        int[,] grid,
        (int x, int y) excludeSpawnKey)
    {
        if (settings.Floor >= settings.MaxFloor)
            return;

        var stairRng = new System.Random(DeterministicSeedUtility.CreateSeed(
            seed, (int)currentStageRegion, floor, 0, DeterministicSeedUtility.StairSelectDomain));
        int[] order = BuildShuffledIndices(roomInfos.Length, stairRng);

        if (TrySelectAndCarveStair(order, roomInfos, grid, settings, stairRng, excludeSpawnKey, true))
            return;
        if (TrySelectAndCarveStair(order, roomInfos, grid, settings, stairRng, excludeSpawnKey, false))
            return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning("[DungeonManager] Stair placement failed: no valid room.", this);
#endif
    }

    private bool TrySelectAndCarveStair(
        int[] order,
        RoomInfo[] roomInfos,
        int[,] grid,
        DungeonSettings settings,
        System.Random rng,
        (int x, int y) excludeSpawnKey,
        bool applyExclusions)
    {
        for (int i = 0; i < order.Length; i++)
        {
            RoomInfo room = roomInfos[order[i]];
            if (applyExclusions && IsStairExcluded(room, excludeSpawnKey))
                continue;

            if (!DungeonGenerator.TryFindStairPosition(
                grid, room.X, room.Y, room.W, room.H, settings, rng, out int sx, out int sy))
            {
                continue;
            }

            _data.SetTileValue(sx, sy, DungeonGenerator.STAIR_UP);
            _registry.SetRoomType(room, RoomType.Stair);
            return true;
        }

        return false;
    }

    private bool IsStairExcluded(RoomInfo room, (int x, int y) excludeSpawnKey)
    {
        if ((room.X, room.Y) == excludeSpawnKey)
            return true;
        if (room.IsElite)
            return true;
        if (stairAvoidTypes != null)
        {
            RoomType type = _registry.GetRoomType(room);
            for (int i = 0; i < stairAvoidTypes.Length; i++)
            {
                if (stairAvoidTypes[i] == type)
                    return true;
            }
        }

        return false;
    }

    private static int[] BuildShuffledIndices(int count, System.Random rng)
    {
        var indices = new int[count];
        for (int i = 0; i < count; i++)
            indices[i] = i;

        for (int i = count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int temp = indices[i];
            indices[i] = indices[j];
            indices[j] = temp;
        }

        return indices;
    }

    private DungeonSettings BuildSettings()
    {
        if (seed == 0)
            seed = DungeonGenerator.GenerateSeed();

        var s = DungeonSettings.Default;
        s.MapWidth      = mapWidth;
        s.MapHeight     = mapHeight;
        s.MinRoomSize   = minRoomSize;
        s.MaxRoomSize   = maxRoomSize;
        s.BspDepth      = bspDepth;
        s.ExtraConnProb = extraConnProb;
        s.ExtraCandidateCount = extraCandidateCount;
        s.ExtraNeighborCount = extraNeighborCount;
        s.ExtraOverlapScoreWeight = extraOverlapScoreWeight;
        s.ExtraPathLengthPenaltyWeight = extraPathLengthPenaltyWeight;
        s.ExtraCenterDistancePenaltyDivisor = extraCenterDistancePenaltyDivisor;
        s.Floor         = floor;
        s.MaxFloor      = MAX_FLOOR;
        s.Seed          = (int)(seed % int.MaxValue);
        return s;
    }

    /// <summary>
    /// RoomRect 배열을 RoomInfo 배열로 변환합니다.
    /// 타입 초기화는 Registry.Initialize()에서 수행됩니다.
    /// </summary>
    private static RoomInfo[] BuildRoomInfos(DungeonGenerator.RoomRect[] rawRooms)
    {
        var result = new RoomInfo[rawRooms.Length];
        for (int i = 0; i < rawRooms.Length; i++)
            result[i] = new RoomInfo
            {
                Rect = rawRooms[i],
                Type = RoomType.Normal,
                StableRoomKey = DeterministicSeedUtility.CreateStableRoomKey(rawRooms[i]),
                IsElite = rawRooms[i].IsElite
            };
        return result;
    }

    private void WarnIfEliteRoomFallback(DungeonGenerator.DungeonLayoutInfo layoutInfo)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!layoutInfo.ShouldHaveEliteRoom || string.IsNullOrWhiteSpace(layoutInfo.EliteRoomWarning))
            return;

        Debug.LogWarning("[DungeonManager] " + layoutInfo.EliteRoomWarning, this);
#endif
    }

    private static string ElapsedMs(double startTime)
    {
        return ((Time.realtimeSinceStartupAsDouble - startTime) * 1000.0)
            .ToString("F3", CultureInfo.InvariantCulture);
    }

    private static int CountWalkableTiles(int[,] grid)
    {
        int height = grid.GetLength(0);
        int width = grid.GetLength(1);
        int count = 0;

        for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++)
                if (grid[row, col] != DungeonGenerator.EMPTY)
                    count++;

        return count;
    }


    public bool IsCorr(int x, int y)
        => _queryService.IsCorr(x, y);

    // ── 내부 초기화 헬퍼 ─────────────────────────────────────────────

    /// <summary>
    /// 모든 도메인 서비스를 보장합니다. Awake가 호출하며,
    /// Edit Mode ContextMenu / CustomEditor가 Awake를 거치지 않고 Generate를 호출하는
    /// 진입점(RunGenerationPipeline)에서도 한 번 호출해 invariant를 유지합니다.
    /// 호출은 idempotent하며 (null이면 생성, 아니면 noop) 중복 비용은 거의 0입니다.
    /// </summary>
    private void EnsureServices()
    {
        if (_queryService == null)
            _queryService = new DungeonQueryService(dungeonRenderer);
        if (_spawnService == null)
            _spawnService = new SpawnPositionService();
        if (_transitionService == null)
            _transitionService = new FloorTransitionService();
    }

    private void WarnIfInvalidCurrentStageRegion()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (currentStageRegion == SpawnRegion.None)
        {
            Debug.LogWarning(
                "[DungeonManager] currentStageRegion이 None입니다. Enemy spawn region 필터가 후보를 찾지 못할 수 있습니다.",
                this);
            return;
        }

        if (!IsSingleSpawnRegion(currentStageRegion))
        {
            Debug.LogWarning(
                $"[DungeonManager] currentStageRegion은 단일 SpawnRegion을 권장합니다. 현재 값: {currentStageRegion}",
                this);
        }
#endif
    }

    private static bool IsSingleSpawnRegion(SpawnRegion region)
    {
        int value = (int)region;
        return value != 0 && (value & (value - 1)) == 0;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        WarnIfInvalidCurrentStageRegion();
    }
#endif

#if UNITY_EDITOR
    // ── Editor 버튼 ──────────────────────────────────────────────────
    [UnityEditor.CustomEditor(typeof(DungeonManager))]
    public class DungeonManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var mgr = (DungeonManager)target;

            UnityEditor.EditorGUILayout.Space(10);
            if (GUILayout.Button("▶  Generate Dungeon", GUILayout.Height(32)))
                mgr.Generate();

            UnityEditor.EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🎲  New Seed",    GUILayout.Height(28))) mgr.GenerateWithNewSeed();
            if (GUILayout.Button("◀  Prev Floor",  GUILayout.Height(28))) mgr.PrevFloor();
            if (GUILayout.Button("▶  Next Floor",  GUILayout.Height(28))) mgr.NextFloor();
            UnityEditor.EditorGUILayout.EndHorizontal();
        }
    }
#endif
}
