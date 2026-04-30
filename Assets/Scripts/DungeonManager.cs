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
using UnityEngine.Serialization;

public class DungeonManager : MonoBehaviour
{
    private static readonly bool AllowForcedGarbageCollectionDuringFloorTransition = false;
    public static DungeonManager Instance { get; private set; }

    // ── Inspector 연결 ───────────────────────────────────────────────

    [Header("Dependencies")]
    [Tooltip("Tilemap 렌더링 담당 컴포넌트")]
    [FormerlySerializedAs("renderer")]
    public DungeonTilemapRenderer dungeonRenderer;

    [Tooltip("이벤트 채널 (ScriptableObject Asset)")]
    public DungeonEventChannel eventChannel;

    [Tooltip("층 이동 시 표시할 로딩 화면 (선택)")]
    public LoadingScreenController loadingScreen;

    [Header("Dungeon Settings")]
    [Tooltip("시드. 0이면 매 생성마다 랜덤 생성 후 저장.")]
    public long seed = 0;

    [Tooltip("현재 층수 (1 ~ maxFloor)")]
    [Range(1, 100)]
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

    [Tooltip("2번째 가까운 방 추가 연결 확률")]
    [Range(0f, 1f)]
    public float extraConnProb = 0.5f;

    [Header("Spawn Region")]
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

    // 층 전환 중복 방지 — 코루틴 실행 중 추가 요청을 차단
    private bool _isTransitioning = false;

    // 그리드 캐싱
    int[,] _originGrid;

    // ── 공개 프로퍼티 ────────────────────────────────────────────────
    public DungeonData   Data     => _data;
    public RoomRegistry  Registry => _registry;
    public bool IsTransitioning => _isTransitioning;
    public RoomInfo? CurrentDoorRoom => _currentDoorRoom;

    private void Awake()
    {
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
        _queryService  = new DungeonQueryService(dungeonRenderer);
        _spawnService  = new SpawnPositionService();
    }

    // ── 생성 파이프라인 ──────────────────────────────────────────────

    [ContextMenu("Generate Dungeon")]
    public void Generate()
    {
        RuntimePerfLogger.MarkEvent("generate_begin",
            "floor=" + floor + " seed=" + seed + " size=" + mapWidth + "x" + mapHeight);

        if (dungeonRenderer == null)
        {
            Debug.LogError("[DungeonManager] DungeonTilemapRenderer가 연결되지 않았습니다.");
            return;
        }

        RunGenerationPipeline();

        // 7. Tilemap 배치
        double stageStart = Time.realtimeSinceStartupAsDouble;
        dungeonRenderer.PlaceTiles(_data);
        RuntimePerfLogger.MarkEvent("generate_stage_place_tiles",
            "elapsedMs=" + ElapsedMs(stageStart));
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

    public void NextFloor() { if (!_isTransitioning) StartCoroutine(FloorTransition(floor + 1)); }
    public void PrevFloor() { if (!_isTransitioning) StartCoroutine(FloorTransition(floor - 1)); }

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
        RuntimePerfLogger.MarkEvent("floor_transition_begin",
            "from=" + floor + " target=" + targetFloor);

        int prev = floor;
        floor = Mathf.Clamp(targetFloor, 1, 100);
        double stageStart = Time.realtimeSinceStartupAsDouble;

        // 1. 로딩 화면 표시
        if (loadingScreen != null)
        {
            RuntimePerfLogger.MarkEvent("floor_transition_loading_show_begin", "floor=" + floor);
            yield return StartCoroutine(loadingScreen.Show());
            RuntimePerfLogger.MarkEvent("floor_transition_loading_show_end",
                "elapsedMs=" + ElapsedMs(stageStart));
        }
        else
        {
            yield return null;
        }

        // 2. 던전 생성 (무거운 연산 — 로딩 화면 뒤에서 수행)
        stageStart = Time.realtimeSinceStartupAsDouble;
        if (useChunkedTilePlacementDuringFloorTransition)
            yield return StartCoroutine(GenerateChunkedForFloorTransition());
        else
            Generate();
        RuntimePerfLogger.MarkEvent("floor_transition_generate_end",
            "elapsedMs=" + ElapsedMs(stageStart) + " floor=" + floor);

        // 3. Unity가 Tilemap 업데이트를 완료할 시간 확보
        stageStart = Time.realtimeSinceStartupAsDouble;
        yield return null;
        RuntimePerfLogger.MarkEvent("floor_transition_post_generate_frame",
            "elapsedMs=" + ElapsedMs(stageStart));

        if (AllowForcedGarbageCollectionDuringFloorTransition &&
            collectGarbageDuringFloorTransition &&
            floorTransitionGcPasses > 0)
        {
            stageStart = Time.realtimeSinceStartupAsDouble;
            RuntimePerfLogger.MarkEvent("floor_transition_gc_begin",
                "floor=" + floor +
                " passes=" + floorTransitionGcPasses +
                " waitFinalizers=" + waitForFinalizersDuringFloorTransition);

            for (int i = 0; i < floorTransitionGcPasses; i++)
                System.GC.Collect();

            if (waitForFinalizersDuringFloorTransition)
                System.GC.WaitForPendingFinalizers();

            RuntimePerfLogger.MarkEvent("floor_transition_gc_end",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " passes=" + floorTransitionGcPasses);
        }

        if (postGenerateSettleSeconds > 0f)
        {
            stageStart = Time.realtimeSinceStartupAsDouble;
            RuntimePerfLogger.MarkEvent("floor_transition_settle_time_begin",
                "seconds=" + postGenerateSettleSeconds.ToString("F3", CultureInfo.InvariantCulture));
            yield return YieldCache.WaitForSecondsRealTime(postGenerateSettleSeconds);
            RuntimePerfLogger.MarkEvent("floor_transition_settle_time_end",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " dtMs=" + (Time.unscaledDeltaTime * 1000f).ToString("F3", CultureInfo.InvariantCulture));
        }

        for (int i = 0; i < postGenerateSettleFrames; i++)
        {
            stageStart = Time.realtimeSinceStartupAsDouble;
            RuntimePerfLogger.MarkEvent("floor_transition_settle_frame_begin",
                "index=" + i);
            yield return null;
            RuntimePerfLogger.MarkEvent("floor_transition_settle_frame",
                "index=" + i +
                " elapsedMs=" + ElapsedMs(stageStart) +
                " dtMs=" + (Time.unscaledDeltaTime * 1000f).ToString("F3", CultureInfo.InvariantCulture));
        }

        // 4. 층 변경 이벤트 발행
        stageStart = Time.realtimeSinceStartupAsDouble;
        RuntimePerfLogger.MarkEvent("floor_transition_event_raise_begin",
            "prev=" + prev + " current=" + floor);
        eventChannel?.RaiseFloorChanged(prev, floor);
        RuntimePerfLogger.MarkEvent("floor_transition_event_raised",
            "prev=" + prev + " current=" + floor +
            " elapsedMs=" + ElapsedMs(stageStart) +
            " dtMs=" + (Time.unscaledDeltaTime * 1000f).ToString("F3", CultureInfo.InvariantCulture));

        // 5. 로딩 화면 숨김
        if (loadingScreen != null)
        {
            stageStart = Time.realtimeSinceStartupAsDouble;
            RuntimePerfLogger.MarkEvent("floor_transition_loading_hide_begin",
                "floor=" + floor +
                " dtMs=" + (Time.unscaledDeltaTime * 1000f).ToString("F3", CultureInfo.InvariantCulture));
            yield return StartCoroutine(loadingScreen.Hide());
            RuntimePerfLogger.MarkEvent("floor_transition_loading_hide_end",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " dtMs=" + (Time.unscaledDeltaTime * 1000f).ToString("F3", CultureInfo.InvariantCulture));
        }

        RuntimePerfLogger.MarkEvent("floor_transition_end", "floor=" + floor);
        _isTransitioning = false;
    }

    private IEnumerator GenerateChunkedForFloorTransition()
    {
        RuntimePerfLogger.MarkEvent("generate_begin",
            "floor=" + floor + " seed=" + seed + " size=" + mapWidth + "x" + mapHeight + " chunked=true");

        if (dungeonRenderer == null)
        {
            Debug.LogError("[DungeonManager] DungeonTilemapRenderer가 연결되지 않았습니다.");
            yield break;
        }

        RunGenerationPipeline();

        // 7. Tilemap 청크 배치
        double stageStart = Time.realtimeSinceStartupAsDouble;
        yield return StartCoroutine(dungeonRenderer.PlaceTilesChunked(_data, tilePlacementChunkRows));
        RuntimePerfLogger.MarkEvent("generate_stage_place_tiles",
            "elapsedMs=" + ElapsedMs(stageStart) +
            " chunkRows=" + tilePlacementChunkRows);
        RuntimePerfLogger.MarkEvent("generate_end",
            "floor=" + floor + " spawn=" + _cachedSpawnPos.x + ":" + _cachedSpawnPos.y + " chunked=true");

        Debug.Log($"[DungeonManager] 생성 완료 — Seed: {seed}, Floor: {floor}");
    }

    // ── 데이터 쿼리 위임 API ─────────────────────────────────────────
    // PlayerController 등이 DungeonManager 하나만 참조해도 되도록 위임합니다.
    // 구현은 DungeonQueryService에 있으며, 시그니처는 하위 호환을 위해 그대로 유지합니다.

    public bool IsWalkable(int col, int row)
        => EnsureQueryService().IsWalkable(col, row);

    public int GetTileType(int col, int row)
        => EnsureQueryService().GetTileType(col, row);

    /// <summary>그리드 좌표가 속한 방을 타입 정보 포함해 반환합니다.</summary>
    public RoomInfo? GetRoomAt(int col, int row)
        => EnsureQueryService().GetRoomAt(col, row);

    /// <summary>스폰 위치를 반환합니다. Generate() 시점에 계산된 캐시를 반환하므로 O(1).</summary>
    public Vector2Int GetSpawnTilePos() => _cachedSpawnPos;

    /// <summary>그리드 좌표를 월드 좌표로 변환합니다 (QueryService → Renderer에 위임).</summary>
    public Vector3 GridToWorld(Vector2Int gridPos)
        => EnsureQueryService().GridToWorld(gridPos);

    /// <summary>월드 좌표를 그리드 좌표로 변환합니다 (QueryService → Renderer에 위임).</summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
        => EnsureQueryService().WorldToGrid(worldPos);

    /// <summary>해당 타입의 계단 위치를 그리드 좌표로 반환합니다.</summary>
    public Vector2Int FindStairPos(int stairType)
        => EnsureQueryService().FindStairPos(stairType);

    /// <summary>방 타입을 변경합니다 (Registry에 위임).</summary>
    public void SetRoomType(RoomInfo room, RoomType type)
        => _registry?.SetRoomType(room, type);

    public void CloseCurrentRoomDoors(RoomInfo room)
    {
        _currentDoorRoom = room;
        dungeonRenderer?.CloseDoorsForRoom(room);
    }

    public void OpenCurrentRoomDoors()
    {
        // 문 개폐는 DungeonManager.Instance를 통해 중앙에서만 처리합니다.
        // RoomSpawner는 방 클리어 상태만 판단하고, 실제 타일맵 문 제어는 여기로 위임합니다.
        if (dungeonRenderer != null && dungeonRenderer.OpenAllDoors())
            _currentDoorRoom = null;
    }

    // ── 내부 빌더 ────────────────────────────────────────────────────

    private void RunGenerationPipeline()
    {
        // 1. 설정 구성
        double stageStart = Time.realtimeSinceStartupAsDouble;
        var settings = BuildSettings();
        RuntimePerfLogger.MarkEvent("generate_stage_build_settings",
            "elapsedMs=" + ElapsedMs(stageStart) +
            " seed=" + settings.Seed +
            " bspDepth=" + settings.BspDepth);

        // 2. 그리드 + 원시 방 목록 생성
        stageStart = Time.realtimeSinceStartupAsDouble;
        DungeonGenerator.RoomRect[] rawRooms;
        int[,] grid = DungeonGenerator.GenerateDungeon(settings, out rawRooms);
        _originGrid = grid;
        RuntimePerfLogger.MarkEvent("generate_stage_generator",
            "elapsedMs=" + ElapsedMs(stageStart) +
            " rawRooms=" + rawRooms.Length +
            " grid=" + grid.GetLength(1) + "x" + grid.GetLength(0));

        // 3. RoomInfo 배열 생성 (타입은 Registry가 결정)
        stageStart = Time.realtimeSinceStartupAsDouble;
        _registry = new RoomRegistry();
        var roomInfos = BuildRoomInfos(rawRooms);
        RuntimePerfLogger.MarkEvent("generate_stage_room_infos",
            "elapsedMs=" + ElapsedMs(stageStart) +
            " roomInfos=" + roomInfos.Length);

        // 4. DungeonData 생성
        stageStart = Time.realtimeSinceStartupAsDouble;
        _data = new DungeonData(grid, roomInfos);
        _data.currentStageRegion = currentStageRegion;
        RuntimePerfLogger.MarkEvent("generate_stage_data_construct",
            "elapsedMs=" + ElapsedMs(stageStart) +
            " walkable=" + CountWalkableTiles(grid));

        // 5. Registry 초기화 (Stair 방 자동 감지)
        stageStart = Time.realtimeSinceStartupAsDouble;
        _registry.Initialize(_data);
        RuntimePerfLogger.MarkEvent("generate_stage_registry_init",
            "elapsedMs=" + ElapsedMs(stageStart));

        // 쿼리 서비스에 최신 데이터 주입 (Registry 초기화 완료 후)
        _queryService?.UpdateData(_data, _registry, _originGrid);

        // 6. 스폰 위치 미리 계산 및 캐싱 (GetSpawnTilePos 호출 시 재계산 불필요)
        stageStart = Time.realtimeSinceStartupAsDouble;
        _cachedSpawnPos = EnsureSpawnService().ComputeSpawnPos(_data, mapWidth, mapHeight);
        RuntimePerfLogger.MarkEvent("generate_stage_spawn_cache",
            "elapsedMs=" + ElapsedMs(stageStart) +
            " spawn=" + _cachedSpawnPos.x + ":" + _cachedSpawnPos.y);
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
        s.Floor         = floor;
        s.MaxFloor      = 100;
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
            result[i] = new RoomInfo { Rect = rawRooms[i], Type = RoomType.Normal };
        return result;
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
        => EnsureQueryService().IsCorr(x, y);

    // ── 내부 초기화 헬퍼 ─────────────────────────────────────────────

    /// <summary>
    /// _queryService를 반환합니다.
    /// Awake 이전 등 예외적으로 null인 경우 즉시 초기화 후 반환합니다.
    /// </summary>
    private DungeonQueryService EnsureQueryService()
    {
        if (_queryService == null)
        {
            Debug.LogWarning("[DungeonManager] _queryService가 Awake 이전에 접근됐습니다 — 즉시 초기화합니다.");
            _queryService = new DungeonQueryService(dungeonRenderer);
        }
        return _queryService;
    }

    /// <summary>
    /// _spawnService를 반환합니다.
    /// Awake 이전 등 예외적으로 null인 경우 즉시 초기화 후 반환합니다.
    /// </summary>
    private SpawnPositionService EnsureSpawnService()
    {
        if (_spawnService == null)
        {
            Debug.LogWarning("[DungeonManager] _spawnService가 Awake 이전에 접근됐습니다 — 즉시 초기화합니다.");
            _spawnService = new SpawnPositionService();
        }
        return _spawnService;
    }

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
