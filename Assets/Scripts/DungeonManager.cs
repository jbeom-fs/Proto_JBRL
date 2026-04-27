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
using UnityEngine;

public class DungeonManager : MonoBehaviour
{
    // ── Inspector 연결 ───────────────────────────────────────────────

    [Header("Dependencies")]
    [Tooltip("Tilemap 렌더링 담당 컴포넌트")]
    public DungeonTilemapRenderer renderer;

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

    // ── 도메인 객체 ─────────────────────────────────────────────────
    private DungeonData    _data;
    private RoomRegistry   _registry;
    private Vector2Int     _cachedSpawnPos;   // Generate 시 계산 후 캐싱

    // 그리드 캐싱
    int[,] _originGrid;

    // ── 공개 프로퍼티 ────────────────────────────────────────────────
    public DungeonData   Data     => _data;
    public RoomRegistry  Registry => _registry;

    // ── 생성 파이프라인 ──────────────────────────────────────────────

    [ContextMenu("Generate Dungeon")]
    public void Generate()
    {
        if (renderer == null)
        {
            Debug.LogError("[DungeonManager] DungeonTilemapRenderer가 연결되지 않았습니다.");
            return;
        }

        // 1. 설정 구성
        var settings = BuildSettings();

        // 2. 그리드 + 원시 방 목록 생성
        DungeonGenerator.RoomRect[] rawRooms;
        int[,] grid = DungeonGenerator.GenerateDungeon(settings, out rawRooms);
        _originGrid = grid;
        // 3. RoomInfo 배열 생성 (타입은 Registry가 결정)
        _registry = new RoomRegistry();
        var roomInfos = BuildRoomInfos(rawRooms);

        // 4. DungeonData 생성
        _data = new DungeonData(grid, roomInfos);

        // 5. Registry 초기화 (Stair 방 자동 감지)
        _registry.Initialize(_data);

        // 6. 스폰 위치 미리 계산 및 캐싱 (GetSpawnTilePos 호출 시 재계산 불필요)
        _cachedSpawnPos = ComputeSpawnPos();

        // 7. Tilemap 배치
        renderer.PlaceTiles(_data);

        Debug.Log($"[DungeonManager] 생성 완료 — Seed: {seed}, Floor: {floor}");
    }

    [ContextMenu("Generate With New Seed")]
    public void GenerateWithNewSeed()
    {
        seed = DungeonGenerator.GenerateSeed();
        Generate();
        Debug.Log($"[DungeonManager] 새 시드: {seed}");
    }

    public void NextFloor() => StartCoroutine(FloorTransition(floor + 1));
    public void PrevFloor() => StartCoroutine(FloorTransition(floor - 1));

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
        int prev = floor;
        floor = Mathf.Clamp(targetFloor, 1, 100);

        // 1. 로딩 화면 표시
        if (loadingScreen != null)
            yield return StartCoroutine(loadingScreen.Show());
        else
            yield return null;

        // 2. 던전 생성 (무거운 연산 — 로딩 화면 뒤에서 수행)
        Generate();

        // 3. Unity가 Tilemap 업데이트를 완료할 시간 확보
        yield return null;

        // 4. 층 변경 이벤트 발행
        eventChannel?.RaiseFloorChanged(prev, floor);

        // 5. 로딩 화면 숨김
        if (loadingScreen != null)
            yield return StartCoroutine(loadingScreen.Hide());
    }

    // ── 데이터 쿼리 위임 API ─────────────────────────────────────────
    // PlayerController 등이 DungeonManager 하나만 참조해도 되도록 위임합니다.

    public bool IsWalkable(int col, int row)
        => _data?.IsWalkable(col, row) ?? false;

    public int GetTileType(int col, int row)
        => _data?.GetTileType(col, row) ?? DungeonGenerator.EMPTY;

    /// <summary>그리드 좌표가 속한 방을 타입 정보 포함해 반환합니다.</summary>
    public RoomInfo? GetRoomAt(int col, int row)
    {
        if (_data == null || _registry == null) return null;
        var room = _data.GetRoomAt(col, row);
        if (!room.HasValue) return null;
        return _registry.Resolve(room.Value);
    }

    /// <summary>
    /// 스폰 위치를 반환합니다.
    /// Generate() 시점에 계산된 캐시를 반환하므로 O(1).
    /// </summary>
    public Vector2Int GetSpawnTilePos() => _cachedSpawnPos;

    /// <summary>
    /// 맵 중앙에 가장 가까운 방 내부 타일 좌표를 계산합니다.
    /// Generate() 내부에서만 호출됩니다.
    /// </summary>
    private Vector2Int ComputeSpawnPos()
    {
        if (_data == null) return Vector2Int.zero;

        int midX     = mapWidth  / 2;
        int midY     = mapHeight / 2;
        int bestDist = int.MaxValue;
        int spawnCol = midX, spawnRow = midY;

        for (int row = 0; row < _data.MapHeight; row++)
            for (int col = 0; col < _data.MapWidth; col++)
            {
                if (_data.GetTileType(col, row) != DungeonGenerator.ROOM) continue;
                int dist = Mathf.Abs(col - midX) + Mathf.Abs(row - midY);
                if (dist >= bestDist) continue;
                bestDist = dist;
                spawnCol = col;
                spawnRow = row;
            }

        return new Vector2Int(spawnCol, spawnRow);
    }

    /// <summary>그리드 좌표를 월드 좌표로 변환합니다 (Renderer에 위임).</summary>
    public Vector3 GridToWorld(Vector2Int gridPos)
        => renderer.GridToWorld(gridPos);

    /// <summary>월드 좌표를 그리드 좌표로 변환합니다 (Renderer에 위임).</summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
        => renderer.WorldToGrid(worldPos);

    /// <summary>해당 타입의 계단 위치를 그리드 좌표로 반환합니다.</summary>
    public Vector2Int FindStairPos(int stairType)
    {
        if (_data == null) return new Vector2Int(-1, -1);
        for (int row = 0; row < _data.MapHeight; row++)
            for (int col = 0; col < _data.MapWidth; col++)
                if (_data.GetTileType(col, row) == stairType)
                    return new Vector2Int(col, row);
        return new Vector2Int(-1, -1);
    }

    /// <summary>방 타입을 변경합니다 (Registry에 위임).</summary>
    public void SetRoomType(RoomInfo room, RoomType type)
        => _registry?.SetRoomType(room, type);

    // ── 내부 빌더 ────────────────────────────────────────────────────

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


    public bool IsCorr(int x, int y)
    {
        return _originGrid[x,y] == 2 ? true : false;
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