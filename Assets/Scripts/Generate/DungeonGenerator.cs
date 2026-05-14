using System;
using System.Collections.Generic;

// ═══════════════════════════════════════════════════════════════
//  던전 생성 설정값 구조체
//  GenerateDungeon() 호출 시 파라미터로 전달합니다.
//  DungeonSettings.Default 로 기본값을 가져온 뒤 원하는 항목만 수정하세요.
// ═══════════════════════════════════════════════════════════════
public struct DungeonSettings
{
    /// <summary>맵 너비 (타일 수)</summary>
    public int MapWidth;

    /// <summary>맵 높이 (타일 수)</summary>
    public int MapHeight;

    /// <summary>방 최소 너비·높이 (5 이상 권장)</summary>
    public int MinRoomSize;

    /// <summary>방 최대 너비·높이</summary>
    public int MaxRoomSize;

    /// <summary>BSP 분할 깊이 — 클수록 방이 많아짐</summary>
    public int BspDepth;

    /// <summary>방과 BSP 경계 사이 최소 여백</summary>
    public int Padding;

    /// <summary>2번째로 가까운 방에 추가 통로를 연결할 확률 (0.0 ~ 1.0)</summary>
    public float ExtraConnProb;

    /// <summary>
    /// 던전 시드. null 이면 실행마다 다른 결과.
    /// 정수 지정 시 → 같은 Seed + 같은 Floor = 항상 동일한 지형.
    /// </summary>
    public int? Seed;

    /// <summary>
    /// 현재 층수 (1 ~ MaxFloor).
    /// Seed와 함께 결정론적 난수 시드를 파생시켜 층마다 다른 지형을 보장합니다.
    /// </summary>
    public int Floor;

    /// <summary>최대 층수 (기본 100)</summary>
    public int MaxFloor;

    /// <summary>방 테두리에서 통로 꺾임까지 최소 직선 거리 (스텁 길이)</summary>
    public int MinStraight;

    // ─── 기본 설정값 ─────────────────────────────────────────────
    public static DungeonSettings Default => new DungeonSettings
    {
        MapWidth      = 80,
        MapHeight     = 50,
        MinRoomSize   = 5,
        MaxRoomSize   = 14,
        BspDepth      = 4,
        Padding       = 2,
        ExtraConnProb = 0.5f,
        Seed          = null,
        Floor         = 1,
        MaxFloor      = 100,
        MinStraight   = 2,
    };

    /// <summary>
    /// Seed + Floor 조합으로 결정론적 난수 시드를 계산합니다.
    ///
    /// 보장:
    ///   같은 Seed + 같은 Floor  → 항상 동일한 값  (재현 가능)
    ///   같은 Seed + 다른 Floor  → 다른 값          (층마다 다른 지형)
    ///   다른 Seed + 같은 Floor  → 다른 값          (시드마다 다른 지형)
    /// </summary>
    public int DeriveSeed()
    {
        int s = Seed ?? 0;
        unchecked
        {
            int mixed = (s ^ (Floor * (int)2654435761u)) * (int)2246822519u;
            return mixed & 0x7FFFFFFF;
        }
    }
}

// ═══════════════════════════════════════════════════════════════
//  던전 생성기
//
//  사용 예시:
//    // 기본 설정으로 생성
//    int[,] map = DungeonGenerator.GenerateDungeon(DungeonSettings.Default);
//
//    // 설정 일부 수정 후 생성
//    var settings = DungeonSettings.Default;
//    settings.MapWidth  = 100;
//    settings.BspDepth  = 5;
//    settings.Seed      = 42;
//    int[,] map = DungeonGenerator.GenerateDungeon(settings);
//
//  반환값:
//    int[y, x]  — 0 = 이동 불가 / 1 = 이동 가능
// ═══════════════════════════════════════════════════════════════
public static class DungeonGenerator
{
    // ── 타일 타입 상수 ────────────────────────────────────────────
    public const int EMPTY       = 0;   // 이동 불가 (빈 공간)
    public const int ROOM        = 1;   // 방 바닥
    public const int CORRIDOR    = 2;   // 통로
    public const int STAIR_UP    = 3;   // 올라가는 계단 (다음 층)
    public const int DOOR_CLOSED = 5;   // 닫힌 문 (통로 차단)

    // ── 디버그 (carving 진단용 — 평소 OFF) ─────────────────────────
    // RuntimePerfLogger와 별개의 toggle. Unity 측에서 켜려면 DebugSink에
    // Debug.Log를 구독시키고 DebugCorridorCarving=true 로 설정한다.
    public static bool DebugCorridorCarving = false;
    public static System.Action<string> DebugSink = null;

    private static List<Room> _debugRooms;
    private static int _debugSrcIdx = -1;
    private static int _debugDstIdx = -1;

    private static void DebugEmit(string msg)
    {
        if (!DebugCorridorCarving) return;
        DebugSink?.Invoke(msg);
    }

    // ── 공개 방 정보 구조체 ───────────────────────────────────────
    /// <summary>방의 좌상단 좌표와 크기를 담는 구조체입니다.</summary>
    public struct RoomRect
    {
        public int X, Y, W, H;
        public int Right  => X + W;
        public int Bottom => Y + H;

        public bool Contains(int col, int row)
            => col >= X && col < X + W && row >= Y && row < Y + H;
    }

    // ── 내부 구조체 ────────────────────────────────────────────

    private struct Room
    {
        public int Cx, Cy;
        public int X, Y, W, H;
    }

    private class BSPNode
    {
        public int X, Y, W, H;
        public BSPNode Left, Right;
        public bool IsLeaf => Left == null && Right == null;
        public BSPNode(int x, int y, int w, int h) { X=x; Y=y; W=w; H=h; }
    }

    // ══════════════════════════════════════════════════════════
    //  공개 메서드
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 던전을 생성하고 그리드를 반환합니다. (기존 API 유지)
    /// </summary>
    public static int[,] GenerateDungeon(DungeonSettings settings)
        => GenerateDungeon(settings, out _);

    /// <summary>
    /// 던전을 생성하고 그리드와 방 목록을 함께 반환합니다.
    /// </summary>
    /// <param name="settings">생성 설정값</param>
    /// <param name="outRooms">생성된 방 목록 (문 제어에 활용)</param>
    public static int[,] GenerateDungeon(DungeonSettings settings, out RoomRect[] outRooms)
    {
        ValidateSettings(ref settings);

        var rng = settings.Seed.HasValue
            ? new Random(settings.DeriveSeed())
            : new Random();
        var grid         = new int[settings.MapHeight, settings.MapWidth];
        var corridorTiles = new HashSet<(int x, int y)>();
        var rooms        = new List<Room>();

        var root = new BSPNode(1, 1, settings.MapWidth - 2, settings.MapHeight - 2);
        BspSplit(root, 0, settings, rng);
        CollectRooms(root, settings, rng, rooms);

        foreach (var room in rooms)
            FillRoom(grid, room);

        ConnectAll(grid, rooms, corridorTiles, settings, rng);
        PlaceStairs(grid, rooms, settings, rng);

        // Room → RoomRect 변환 후 반환
        outRooms = new RoomRect[rooms.Count];
        for (int i = 0; i < rooms.Count; i++)
            outRooms[i] = new RoomRect { X=rooms[i].X, Y=rooms[i].Y,
                                         W=rooms[i].W, H=rooms[i].H };
        return grid;
    }

    // ══════════════════════════════════════════════════════════
    //  Step 1 — BSP 공간 분할 및 방 배치
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// BSP 재귀 분할.
    /// 가로·세로 비율에 따라 분할 방향을 선택하여
    /// 방이 균등하게 분포될 영역을 확보합니다.
    /// </summary>
    private static void BspSplit(BSPNode node, int depth, DungeonSettings s, Random rng)
    {
        if (depth >= s.BspDepth) return;

        int minSplit = s.MinRoomSize * 2 + s.Padding * 4;
        bool canH = node.H >= minSplit;
        bool canV = node.W >= minSplit;
        if (!canH && !canV) return;

        // 긴 쪽 우선 분할 (비슷하면 랜덤)
        bool horiz;
        if (canH && canV)
            horiz = node.H > node.W * 1.25 ||
                    (node.W <= node.H * 1.25 && rng.NextDouble() < 0.5);
        else
            horiz = canH;

        if (horiz)
        {
            int lo = node.Y + s.MinRoomSize + s.Padding * 2;
            int hi = node.Y + node.H - s.MinRoomSize - s.Padding * 2;
            if (lo > hi) return;
            int sp = rng.Next(lo, hi + 1);
            node.Left  = new BSPNode(node.X, node.Y, node.W, sp - node.Y);
            node.Right = new BSPNode(node.X, sp,     node.W, node.Y + node.H - sp);
        }
        else
        {
            int lo = node.X + s.MinRoomSize + s.Padding * 2;
            int hi = node.X + node.W - s.MinRoomSize - s.Padding * 2;
            if (lo > hi) return;
            int sp = rng.Next(lo, hi + 1);
            node.Left  = new BSPNode(node.X, node.Y, sp - node.X,          node.H);
            node.Right = new BSPNode(sp,     node.Y, node.X + node.W - sp, node.H);
        }

        BspSplit(node.Left,  depth + 1, s, rng);
        BspSplit(node.Right, depth + 1, s, rng);
    }

    /// <summary>
    /// BSP 리프 노드에 방을 배치하고 rooms 리스트에 수집합니다.
    /// </summary>
    private static void CollectRooms(BSPNode node, DungeonSettings s, Random rng, List<Room> rooms)
    {
        if (node.IsLeaf)
        {
            int p    = s.Padding;
            int maxW = Math.Min(node.W - p * 2, s.MaxRoomSize);
            int maxH = Math.Min(node.H - p * 2, s.MaxRoomSize);
            if (maxW < s.MinRoomSize || maxH < s.MinRoomSize) return;

            int rw = rng.Next(s.MinRoomSize, maxW + 1);
            int rh = rng.Next(s.MinRoomSize, maxH + 1);

            // 패딩 범위 안에서 방 위치를 랜덤 결정
            int rxRange = node.W - rw - p;
            int ryRange = node.H - rh - p;
            int rx = node.X + (rxRange > p ? rng.Next(p, rxRange + 1) : p);
            int ry = node.Y + (ryRange > p ? rng.Next(p, ryRange + 1) : p);

            rooms.Add(new Room
            {
                X = rx, Y = ry, W = rw, H = rh,
                Cx = rx + rw / 2,
                Cy = ry + rh / 2,
            });
        }
        else
        {
            if (node.Left  != null) CollectRooms(node.Left,  s, rng, rooms);
            if (node.Right != null) CollectRooms(node.Right, s, rng, rooms);
        }
    }

    private static void FillRoom(int[,] grid, Room room)
    {
        for (int y = room.Y; y < room.Y + room.H; y++)
            for (int x = room.X; x < room.X + room.W; x++)
                grid[y, x] = ROOM;
    }

    // ══════════════════════════════════════════════════════════
    //  Step 3 — MST + 추가 연결 (Prim's Algorithm)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Prim's MST로 모든 방이 연결되도록 통로를 생성합니다.
    /// 각 단계에서 같은 소스 방을 기준으로 2번째 가까운 방에도
    /// ExtraConnProb 확률로 추가 통로를 연결합니다.
    /// </summary>
    private static void ConnectAll(
        int[,] grid, List<Room> rooms,
        HashSet<(int, int)> corridorTiles,
        DungeonSettings s, Random rng)
    {
        int n = rooms.Count;
        if (n < 2) return;

        var connected      = new HashSet<int> { 0 };
        var remaining      = new HashSet<int>();
        for (int k = 1; k < n; k++) remaining.Add(k);

        // 이미 직접 연결된 방 쌍 추적 — 중복/병렬 통로 방지
        var connectedPairs = new HashSet<(int, int)>();

        // L corridor path 미리계산용 재사용 버퍼 (allocation 1회).
        var pathBuf = new List<(int x, int y)>(64);
        int debugStep = 0;

        while (remaining.Count > 0)
        {
            debugStep++;
            // ── 1st: MST — 가장 가까운 미연결 방 ──────────────────
            double bestDist = double.MaxValue;
            int srcIdx = -1, dstIdx = -1;

            foreach (int i in connected)
                foreach (int j in remaining)
                {
                    double d = EuclideanDist(rooms[i], rooms[j]);
                    if (d < bestDist) { bestDist = d; srcIdx = i; dstIdx = j; }
                }

            if (DebugCorridorCarving)
            {
                _debugRooms = rooms;
                _debugSrcIdx = srcIdx;
                _debugDstIdx = dstIdx;
                DebugEmit("--- MST corridor: src=R" + srcIdx + " dst=R" + dstIdx + " ---");
                DebugEmit("  src.Rect: X=" + rooms[srcIdx].X + " Y=" + rooms[srcIdx].Y +
                          " W=" + rooms[srcIdx].W + " H=" + rooms[srcIdx].H);
                DebugEmit("  dst.Rect: X=" + rooms[dstIdx].X + " Y=" + rooms[dstIdx].Y +
                          " W=" + rooms[dstIdx].W + " H=" + rooms[dstIdx].H);
                DebugConnectState("  before step=" + debugStep + " type=MST",
                    grid, rooms, connected, remaining, connectedPairs, s);
            }
            bool mstCarved = DrawLCorridor(grid, rooms, srcIdx, dstIdx, corridorTiles, s, /*isMandatoryEdge*/ true, pathBuf);
            if (!mstCarved && DebugCorridorCarving)
                DebugEmit("  warning=MST returned false");
            bool mstPairAlreadyConnected = connectedPairs.Contains((Math.Min(srcIdx, dstIdx), Math.Max(srcIdx, dstIdx)));
            connectedPairs.Add((Math.Min(srcIdx, dstIdx), Math.Max(srcIdx, dstIdx)));
            connected.Add(dstIdx);
            remaining.Remove(dstIdx);
            if (DebugCorridorCarving)
            {
                DebugEmit("  state-update type=MST pairAdded=" + (!mstPairAlreadyConnected) +
                          " connectedAdd=R" + dstIdx + " remainingRemove=R" + dstIdx);
                DebugConnectState("  after  step=" + debugStep + " type=MST",
                    grid, rooms, connected, remaining, connectedPairs, s);
            }

            // ── 2nd: 추가 연결 — srcIdx 기준 진짜 2번째로 가까운 방 ──
            // 전체 방 탐색 (dstIdx 제외).
            // 단, 이미 직접 연결된 쌍은 제외 → 중복/병렬 통로 방지
            if (rng.NextDouble() < s.ExtraConnProb)
            {
                double bestDist2 = double.MaxValue;
                int bestK = -1;

                for (int k = 0; k < n; k++)
                {
                    if (k == srcIdx || k == dstIdx) continue;
                    var pair = (Math.Min(srcIdx, k), Math.Max(srcIdx, k));
                    if (connectedPairs.Contains(pair)) continue; // 이미 직접 연결 → 스킵
                    double d = EuclideanDist(rooms[srcIdx], rooms[k]);
                    if (d < bestDist2) { bestDist2 = d; bestK = k; }
                }

                if (bestK >= 0)
                {
                    if (DebugCorridorCarving)
                    {
                        _debugRooms = rooms;
                        _debugSrcIdx = srcIdx;
                        _debugDstIdx = bestK;
                        DebugEmit("--- EXTRA corridor: src=R" + srcIdx + " dst=R" + bestK + " ---");
                        DebugEmit("  src.Rect: X=" + rooms[srcIdx].X + " Y=" + rooms[srcIdx].Y +
                                  " W=" + rooms[srcIdx].W + " H=" + rooms[srcIdx].H);
                        DebugEmit("  dst.Rect: X=" + rooms[bestK].X + " Y=" + rooms[bestK].Y +
                                  " W=" + rooms[bestK].W + " H=" + rooms[bestK].H);
                        DebugConnectState("  before step=" + debugStep + " type=EXTRA",
                            grid, rooms, connected, remaining, connectedPairs, s);
                    }
                    bool extraCarved = DrawLCorridor(grid, rooms, srcIdx, bestK, corridorTiles, s, /*isMandatoryEdge*/ false, pathBuf);
                    bool extraPairAlreadyConnected = connectedPairs.Contains((Math.Min(srcIdx, bestK), Math.Max(srcIdx, bestK)));
                    bool extraDstWasRemaining = remaining.Contains(bestK);
                    if (extraCarved)
                    {
                        connectedPairs.Add((Math.Min(srcIdx, bestK), Math.Max(srcIdx, bestK)));
                        if (remaining.Contains(bestK))
                        {
                            connected.Add(bestK);
                            remaining.Remove(bestK);
                        }
                    }
                    if (DebugCorridorCarving)
                    {
                        DebugEmit("  state-update type=EXTRA carved=" + extraCarved +
                                  " pairAdded=" + (extraCarved && !extraPairAlreadyConnected) +
                                  " dstWasRemaining=" + extraDstWasRemaining +
                                  " connectedAdd=" + (extraCarved && extraDstWasRemaining ? ("R" + bestK) : "none") +
                                  " remainingRemove=" + (extraCarved && extraDstWasRemaining ? ("R" + bestK) : "none"));
                        DebugConnectState("  after  step=" + debugStep + " type=EXTRA",
                            grid, rooms, connected, remaining, connectedPairs, s);
                    }
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  Step 4 — 계단 배치
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 각 층에 계단을 배치합니다.
    ///
    /// 배치 규칙:
    ///   - 1층  : STAIR_DOWN 없음
    ///   - 최고층: STAIR_UP 없음
    ///   - 올라가는 계단과 내려가는 계단은 반드시 서로 다른 방에 배치
    ///   - 계단은 통로(CORRIDOR)와 4방향으로 인접하지 않아야 함
    ///   - 방 내부(테두리 제외) 중 랜덤 위치에 배치
    /// </summary>
    private static void DebugConnectState(
        string prefix, int[,] grid, List<Room> rooms,
        HashSet<int> connected, HashSet<int> remaining,
        HashSet<(int, int)> connectedPairs, DungeonSettings s)
    {
        if (!DebugCorridorCarving) return;

        var reachable = DebugReachableRoomsFromR0(grid, rooms, s);
        DebugEmit(prefix +
                  " connected=" + DebugFormatSet(connected) +
                  " remaining=" + DebugFormatSet(remaining) +
                  " reachable=" + DebugFormatSet(reachable) +
                  " pairs=" + connectedPairs.Count +
                  " logicalOnly=" + DebugFormatSetDifference(connected, reachable) +
                  " gridOnly=" + DebugFormatSetDifference(reachable, connected));
    }

    private static HashSet<int> DebugReachableRoomsFromR0(
        int[,] grid, List<Room> rooms, DungeonSettings s)
    {
        var reachableRooms = new HashSet<int>();
        if (rooms.Count == 0) return reachableRooms;

        var start = rooms[0];
        int startX = start.Cx;
        int startY = start.Cy;
        if (!InBounds(s, startX, startY)) return reachableRooms;

        var visited = new bool[s.MapHeight, s.MapWidth];
        var queue = new Queue<(int x, int y)>();
        visited[startY, startX] = true;
        queue.Enqueue((startX, startY));

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            for (int rIdx = 0; rIdx < rooms.Count; rIdx++)
            {
                var r = rooms[rIdx];
                if (p.x >= r.X && p.x < r.X + r.W &&
                    p.y >= r.Y && p.y < r.Y + r.H)
                    reachableRooms.Add(rIdx);
            }

            for (int i = 0; i < 4; i++)
            {
                int nx = p.x + dx[i];
                int ny = p.y + dy[i];
                if (!InBounds(s, nx, ny) || visited[ny, nx]) continue;
                if (grid[ny, nx] == EMPTY) continue;

                visited[ny, nx] = true;
                queue.Enqueue((nx, ny));
            }
        }

        return reachableRooms;
    }

    private static string DebugFormatSet(HashSet<int> values)
    {
        var sorted = new List<int>(values);
        sorted.Sort();
        if (sorted.Count == 0) return "{}";

        string text = "{";
        for (int i = 0; i < sorted.Count; i++)
        {
            if (i > 0) text += ",";
            text += "R" + sorted[i];
        }

        return text + "}";
    }

    private static string DebugFormatSetDifference(HashSet<int> left, HashSet<int> right)
    {
        var diff = new HashSet<int>();
        foreach (int value in left)
            if (!right.Contains(value))
                diff.Add(value);

        return DebugFormatSet(diff);
    }

    private static void PlaceStairs(
        int[,] grid, List<Room> rooms,
        DungeonSettings s, Random rng)
    {
        if (rooms.Count == 0) return;

        // 최고층에는 올라가는 계단 없음
        if (s.Floor >= s.MaxFloor) return;

        // 방 인덱스를 섞어 랜덤 순서로 탐색
        var indices = new List<int>();
        for (int i = 0; i < rooms.Count; i++) indices.Add(i);
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int tmp = indices[i]; indices[i] = indices[j]; indices[j] = tmp;
        }

        // ── STAIR_UP 배치 ────────────────────────────────────────
        foreach (int idx in indices)
        {
            if (TryFindStairPos(grid, rooms[idx], s, rng, out int sx, out int sy))
            {
                grid[sy, sx] = STAIR_UP;
                break;
            }
        }
    }

    /// <summary>
    /// 방 내부에서 계단을 놓을 수 있는 유효한 위치를 랜덤으로 선택합니다.
    /// </summary>
    private static bool TryFindStairPos(
        int[,] grid, Room room, DungeonSettings s, Random rng,
        out int sx, out int sy)
    {
        // 방 테두리(1줄)를 제외한 내부 타일만 후보로 수집
        var candidates = new List<(int x, int y)>();

        for (int row = room.Y + 1; row < room.Y + room.H - 1; row++)
        {
            for (int col = room.X + 1; col < room.X + room.W - 1; col++)
            {
                if (IsValidStairPos(grid, col, row, s))
                    candidates.Add((col, row));
            }
        }

        if (candidates.Count == 0) { sx = sy = -1; return false; }

        var chosen = candidates[rng.Next(candidates.Count)];
        sx = chosen.x;
        sy = chosen.y;
        return true;
    }

    /// <summary>
    /// (x, y)가 계단을 놓기에 유효한 위치인지 검사합니다.
    ///
    /// 조건:
    ///   1. 현재 ROOM 타일이어야 함 (이미 계단/통로 등이면 제외)
    ///   2. 상하좌우 4방향 이웃 중 CORRIDOR가 하나도 없어야 함
    /// </summary>
    private static bool IsValidStairPos(int[,] grid, int x, int y, DungeonSettings s)
    {
        if (grid[y, x] != ROOM) return false;

        int[] dx = {  0,  0,  1, -1 };
        int[] dy = {  1, -1,  0,  0 };

        for (int i = 0; i < 4; i++)
        {
            int nx = x + dx[i];
            int ny = y + dy[i];
            if (nx < 0 || nx >= s.MapWidth || ny < 0 || ny >= s.MapHeight) continue;
            if (grid[ny, nx] == CORRIDOR) return false;
        }
        return true;
    }

    /// <summary>
    /// 12자리 랜덤 시드를 생성합니다.
    ///
    /// 규칙:
    ///   - 항상 12자리 정수 반환 (100000000000 ~ 999999999999)
    ///   - 첫 번째 자리는 1~9 (0으로 시작하지 않음)
    /// </summary>
    /// <returns>12자리 랜덤 시드</returns>
    public static long GenerateSeed()
    {
        var rng   = new Random();
        long first = rng.Next(1, 10);                  // 첫 자리: 1~9
        long rest  = (long)(rng.NextDouble() * 100000000000L);  // 나머지 11자리
        return first * 100000000000L + rest;
    }

    private static double EuclideanDist(Room a, Room b)
    {
        double dx = b.Cx - a.Cx;
        double dy = b.Cy - a.Cy;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // ══════════════════════════════════════════════════════════
    //  Step 3 — L자형 통로 (출입구 기반)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 두 방을 L자형 통로로 연결합니다.
    ///
    /// 흐름:
    ///   1) Primary axis(|dx|≥|dy|에 따른 H-first / V-first)로 path 후보를 cell list에 미리 emit.
    ///   2) src/dst가 아닌 다른 방의 interior / perim(0) / perim+1(벽 옆 1칸) 과 겹치는지 검사.
    ///   3) 겹치면 alternate axis로 1회 재시도.
    ///   4) 둘 다 겹치면: mandatory(MST)는 connectivity 보장 위해 primary로 carve, optional(EXTRA)은 skip.
    ///   5) src/dst side의 축 범위가 겹치면 같은 door 축을 재사용한다.
    ///      겹치지 않을 때만 기존 MinStraight 보정을 적용한다.
    /// </summary>
    private static bool DrawLCorridor(
        int[,] grid, List<Room> rooms, int srcIdx, int dstIdx,
        HashSet<(int, int)> corridorTiles,
        DungeonSettings s,
        bool isMandatoryEdge,
        List<(int x, int y)> pathBuf)
    {
        var src = rooms[srcIdx];
        var dst = rooms[dstIdx];
        int dx = dst.Cx - src.Cx;
        int dy = dst.Cy - src.Cy;
        bool primaryHorizFirst = Math.Abs(dx) >= Math.Abs(dy);

        if (DebugCorridorCarving)
            DebugEmit("  axis-primary=" + (primaryHorizFirst ? "H-first" : "V-first") +
                      " dx=" + dx + " dy=" + dy + " MIN=" + s.MinStraight +
                      " src.Cx=" + src.Cx + " src.Cy=" + src.Cy +
                      " dst.Cx=" + dst.Cx + " dst.Cy=" + dst.Cy);

        // 1) Primary axis 경로 emit + 검사
        EmitLCorridorPath(grid, src, dst, s, primaryHorizFirst, pathBuf);
        bool primaryClean = IsCorridorCandidateClean(
            grid, pathBuf, rooms, srcIdx, dstIdx, s, isMandatoryEdge);
        if (primaryClean)
        {
            if (DebugCorridorCarving) DebugEmit("  carve=primary(clean) cells=" + pathBuf.Count);
            CarvePath(grid, corridorTiles, pathBuf, s);
            return true;
        }

        // 2) Alternate axis 재시도
        EmitLCorridorPath(grid, src, dst, s, !primaryHorizFirst, pathBuf);
        bool alternateClean = IsCorridorCandidateClean(
            grid, pathBuf, rooms, srcIdx, dstIdx, s, isMandatoryEdge);
        if (alternateClean)
        {
            if (DebugCorridorCarving) DebugEmit("  carve=alternate(clean) cells=" + pathBuf.Count);
            CarvePath(grid, corridorTiles, pathBuf, s);
            return true;
        }

        // 3) 둘 다 충돌 — mandatory면 connectivity 보장 위해 primary 강제 carve, optional은 skip.
        if (isMandatoryEdge)
        {
            EmitLCorridorPath(grid, src, dst, s, primaryHorizFirst, pathBuf);
            if (DebugCorridorCarving) DebugEmit("  carve=primary FALLBACK(both-dirty,mandatory) cells=" + pathBuf.Count);
            CarvePath(grid, corridorTiles, pathBuf, s);
            return true;
        }
        else
        {
            if (DebugCorridorCarving) DebugEmit("  carve=SKIP(both-dirty,extra)");
            return false;
        }
    }

    // ── L corridor path 생성 (carve 없이 cell list만 채움) ─────────────────
    private static void EmitLCorridorPath(
        int[,] grid, Room src, Room dst, DungeonSettings s,
        bool horizFirst, List<(int x, int y)> path)
    {
        path.Clear();
        int MIN = s.MinStraight;
        int dx = dst.Cx - src.Cx;
        int dy = dst.Cy - src.Cy;

        if (horizFirst)
        {
            // ── 수평 연결 ────────────────────────────────────────────
            int doorSX, farSX, doorEX, farEX, stepS, stepE;
            if (dx >= 0)
            {
                doorSX = src.X + src.W - 1; farSX = src.X + src.W + MIN - 1;
                doorEX = dst.X;             farEX = dst.X - MIN;
                stepS = 1; stepE = -1;
            }
            else
            {
                doorSX = src.X;             farSX = src.X - MIN;
                doorEX = dst.X + dst.W - 1; farEX = dst.X + dst.W + MIN - 1;
                stepS = -1; stepE = 1;
            }

            int sy = src.Cy, ey = dst.Cy;
            if (Math.Abs(sy - ey) < MIN &&
                !TryUseSharedDoorAxis(src.Y, src.Y + src.H, dst.Y, dst.Y + dst.H, sy, out sy, out ey))
            {
                int eyDir = (ey >= sy) ? 1 : -1;
                if (eyDir == 0) eyDir = 1;
                ey = Math.Max(dst.Y, Math.Min(dst.Y + dst.H - 1, sy + eyDir * MIN));
                if (Math.Abs(sy - ey) < MIN)
                    sy = Math.Max(src.Y, Math.Min(src.Y + src.H - 1, ey - eyDir * MIN));
            }

            path.Add((doorSX, sy));
            path.Add((doorEX, ey));
            EmitH(doorSX + stepS, farSX, sy, path);
            EmitH(doorEX + stepE, farEX, ey, path);
            EmitH(farSX, farEX, sy, path);
            EmitV(sy, ey, farEX, path);
        }
        else
        {
            // ── 수직 연결 ────────────────────────────────────────────
            int doorSY, farSY, doorEY, farEY, stepS, stepE;
            if (dy >= 0)
            {
                doorSY = src.Y + src.H - 1; farSY = src.Y + src.H + MIN - 1;
                doorEY = dst.Y;             farEY = dst.Y - MIN;
                stepS = 1; stepE = -1;
            }
            else
            {
                doorSY = src.Y;             farSY = src.Y - MIN;
                doorEY = dst.Y + dst.H - 1; farEY = dst.Y + dst.H + MIN - 1;
                stepS = -1; stepE = 1;
            }

            int sx = src.Cx, ex = dst.Cx;
            if (Math.Abs(sx - ex) < MIN &&
                !TryUseSharedDoorAxis(src.X, src.X + src.W, dst.X, dst.X + dst.W, sx, out sx, out ex))
            {
                int exDir = (ex >= sx) ? 1 : -1;
                if (exDir == 0) exDir = 1;
                ex = Math.Max(dst.X, Math.Min(dst.X + dst.W - 1, sx + exDir * MIN));
                if (Math.Abs(sx - ex) < MIN)
                    sx = Math.Max(src.X, Math.Min(src.X + src.W - 1, ex - exDir * MIN));
            }

            path.Add((sx, doorSY));
            path.Add((ex, doorEY));
            EmitV(doorSY + stepS, farSY, sx, path);
            EmitV(doorEY + stepE, farEY, ex, path);
            EmitV(farSY, farEY, sx, path);
            EmitH(sx, ex, farEY, path);
        }
    }

    private static void EmitH(int x0, int x1, int y, List<(int x, int y)> path)
    {
        int step = (x1 >= x0) ? 1 : -1;
        for (int x = x0; (step > 0) ? (x <= x1) : (x >= x1); x += step)
            path.Add((x, y));
    }

    private static void EmitV(int y0, int y1, int x, List<(int x, int y)> path)
    {
        int step = (y1 >= y0) ? 1 : -1;
        for (int y = y0; (step > 0) ? (y <= y1) : (y >= y1); y += step)
            path.Add((x, y));
    }

    private static void CarvePath(int[,] grid, HashSet<(int, int)> tiles,
                                   List<(int x, int y)> path, DungeonSettings s)
    {
        for (int i = 0; i < path.Count; i++)
        {
            var p = path[i];
            SetTile(grid, tiles, p.x, p.y, s);
        }
    }

    private static bool IsCorridorCandidateClean(
        int[,] grid, List<(int x, int y)> path, List<Room> rooms,
        int srcIdx, int dstIdx, DungeonSettings s, bool isMandatoryEdge)
    {
        if (PathHitsThirdRoom(path, rooms, srcIdx, dstIdx)) return false;
        if (PathCreatesBadDoorRun(grid, path, rooms, s)) return false;
        if (PathCreatesOrphanDoorStub(grid, path, rooms, s)) return false;

        if (!isMandatoryEdge &&
            PathCreatesOutwardRoomStub(path, rooms[srcIdx], rooms[dstIdx], s))
        {
            if (DebugCorridorCarving) DebugEmit("  reject=outward-room-stub");
            return false;
        }

        // Extra corridors are optional, so reject long side-parallel runs near
        // third rooms while still allowing short perpendicular door stubs.
        if (!isMandatoryEdge &&
            PathCreatesThirdRoomParallelRun(path, rooms, srcIdx, dstIdx, s))
            return false;

        return true;
    }

    /// <summary>
    /// path가 src/dst가 아닌 다른 방의 interior / perim(0) / perim+1(벽 옆 1칸) 위로 지나가는지 판정.
    /// perim+1 까지 잡는 이유: bend axis가 이웃 방 벽에서 1칸 떨어진 라인 위로 길게 흐르면
    /// "벽에 평행하게 붙은 통로" 시각 패턴이 발생하기 때문.
    /// </summary>
    private static bool PathHitsThirdRoom(List<(int x, int y)> path,
                                          List<Room> rooms, int srcIdx, int dstIdx)
    {
        for (int i = 0; i < path.Count; i++)
        {
            int x = path[i].x;
            int y = path[i].y;
            for (int k = 0; k < rooms.Count; k++)
            {
                if (k == srcIdx || k == dstIdx) continue;
                var r = rooms[k];
                int xL  = r.X,     xR  = r.X + r.W,     yT  = r.Y,     yB  = r.Y + r.H;
                int xL2 = r.X - 2, xR2 = r.X + r.W + 1, yT2 = r.Y - 2, yB2 = r.Y + r.H + 1;

                // INTERIOR
                if (x >= xL && x < xR && y >= yT && y < yB) return true;
                // PERIM (perim+0)
                bool topBot0 = (y == yT - 1 || y == yB) && x >= xL && x < xR;
                bool leftRt0 = (x == xL - 1 || x == xR) && y >= yT && y < yB;
                if (topBot0 || leftRt0) return true;
                // PERIM+1 (alongside)
                bool topBot1 = (y == yT2 || y == yB2) && x >= xL && x < xR;
                bool leftRt1 = (x == xL2 || x == xR2) && y >= yT && y < yB;
                if (topBot1 || leftRt1) return true;
            }
        }
        return false;
    }

    private static bool PathCreatesOutwardRoomStub(
        List<(int x, int y)> path, Room src, Room dst, DungeonSettings s)
    {
        int overlapX = OverlapLength(src.X, src.X + src.W, dst.X, dst.X + dst.W);
        int overlapY = OverlapLength(src.Y, src.Y + src.H, dst.Y, dst.Y + dst.H);

        bool separatedX = src.X + src.W <= dst.X || dst.X + dst.W <= src.X;
        bool separatedY = src.Y + src.H <= dst.Y || dst.Y + dst.H <= src.Y;
        int minUsefulOverlap = Math.Max(1, s.MinStraight);

        if (separatedX && overlapY >= minUsefulOverlap)
        {
            bool srcUsesVerticalDetourSide = PathTouchesTopOrBottomSide(path, src);
            bool dstUsesVerticalDetourSide = PathTouchesTopOrBottomSide(path, dst);
            if (srcUsesVerticalDetourSide && dstUsesVerticalDetourSide)
                return true;
        }

        if (separatedY && overlapX >= minUsefulOverlap)
        {
            bool srcUsesHorizontalDetourSide = PathTouchesLeftOrRightSide(path, src);
            bool dstUsesHorizontalDetourSide = PathTouchesLeftOrRightSide(path, dst);
            if (srcUsesHorizontalDetourSide && dstUsesHorizontalDetourSide)
                return true;
        }

        return false;
    }

    private static int OverlapLength(int aStart, int aEnd, int bStart, int bEnd)
        => Math.Max(0, Math.Min(aEnd, bEnd) - Math.Max(aStart, bStart));

    private static bool PathTouchesTopOrBottomSide(List<(int x, int y)> path, Room room)
    {
        for (int i = 0; i < path.Count; i++)
        {
            int x = path[i].x;
            int y = path[i].y;
            bool onTopOrBottom = y == room.Y - 1 || y == room.Y + room.H;
            if (onTopOrBottom && x >= room.X && x < room.X + room.W)
                return true;
        }

        return false;
    }

    private static bool PathTouchesLeftOrRightSide(List<(int x, int y)> path, Room room)
    {
        for (int i = 0; i < path.Count; i++)
        {
            int x = path[i].x;
            int y = path[i].y;
            bool onLeftOrRight = x == room.X - 1 || x == room.X + room.W;
            if (onLeftOrRight && y >= room.Y && y < room.Y + room.H)
                return true;
        }

        return false;
    }

    private static bool PathCreatesBadDoorRun(
        int[,] grid, List<(int x, int y)> path, List<Room> rooms, DungeonSettings s)
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            if (SideHasBadDoorRun(grid, path, s, r.Y - 1, true, r.X, r.X + r.W)) return true;
            if (SideHasBadDoorRun(grid, path, s, r.Y + r.H, true, r.X, r.X + r.W)) return true;
            if (SideHasBadDoorRun(grid, path, s, r.X - 1, false, r.Y, r.Y + r.H)) return true;
            if (SideHasBadDoorRun(grid, path, s, r.X + r.W, false, r.Y, r.Y + r.H)) return true;
        }

        return false;
    }

    private static bool PathCreatesOrphanDoorStub(
        int[,] grid, List<(int x, int y)> path, List<Room> rooms, DungeonSettings s)
    {
        for (int i = 0; i < path.Count; i++)
        {
            int x = path[i].x;
            int y = path[i].y;
            if (!InBounds(s, x, y) || grid[y, x] == ROOM) continue;

            for (int rIdx = 0; rIdx < rooms.Count; rIdx++)
            {
                var r = rooms[rIdx];
                if (IsOrphanDoorOnSide(grid, path, s, x, y, r.Y - 1, true, r.X, r.X + r.W, 0, -1))
                    return true;
                if (IsOrphanDoorOnSide(grid, path, s, x, y, r.Y + r.H, true, r.X, r.X + r.W, 0, 1))
                    return true;
                if (IsOrphanDoorOnSide(grid, path, s, x, y, r.X - 1, false, r.Y, r.Y + r.H, -1, 0))
                    return true;
                if (IsOrphanDoorOnSide(grid, path, s, x, y, r.X + r.W, false, r.Y, r.Y + r.H, 1, 0))
                    return true;
            }
        }

        return false;
    }

    private static bool IsOrphanDoorOnSide(
        int[,] grid, List<(int x, int y)> path, DungeonSettings s,
        int x, int y, int fixedCoord, bool horizontal, int rangeStart, int rangeEnd,
        int outwardDx, int outwardDy)
    {
        int sideK = horizontal ? x : y;
        int sideFixed = horizontal ? y : x;
        if (sideFixed != fixedCoord || sideK < rangeStart || sideK >= rangeEnd)
            return false;

        int ox = x + outwardDx;
        int oy = y + outwardDy;
        if (!InBounds(s, ox, oy)) return true;
        if (grid[oy, ox] == CORRIDOR) return false;
        return !PathWouldCarveCorridor(grid, path, ox, oy);
    }

    private static bool PathCreatesThirdRoomParallelRun(
        List<(int x, int y)> path, List<Room> rooms, int srcIdx, int dstIdx,
        DungeonSettings s)
    {
        const int MaxDistanceFromPerimeter = 2;
        const int MinParallelRunLength = 3;

        int sidePadding = Math.Max(1, s.MinStraight);
        for (int i = 0; i < rooms.Count; i++)
        {
            if (i == srcIdx || i == dstIdx) continue;

            var r = rooms[i];
            int xStart = r.X - sidePadding;
            int xEnd = r.X + r.W + sidePadding;
            int yStart = r.Y - sidePadding;
            int yEnd = r.Y + r.H + sidePadding;

            for (int distance = 1; distance <= MaxDistanceFromPerimeter; distance++)
            {
                if (PathHasHorizontalRun(path, r.Y - 1 - distance, xStart, xEnd, MinParallelRunLength))
                    return true;
                if (PathHasHorizontalRun(path, r.Y + r.H + distance, xStart, xEnd, MinParallelRunLength))
                    return true;
                if (PathHasVerticalRun(path, r.X - 1 - distance, yStart, yEnd, MinParallelRunLength))
                    return true;
                if (PathHasVerticalRun(path, r.X + r.W + distance, yStart, yEnd, MinParallelRunLength))
                    return true;
            }
        }

        return false;
    }

    private static bool PathHasHorizontalRun(
        List<(int x, int y)> path, int y, int xStart, int xEnd, int minRunLength)
    {
        int runLen = 0;
        for (int x = xStart; x < xEnd; x++)
        {
            if (PathContains(path, x, y))
            {
                runLen++;
                if (runLen >= minRunLength) return true;
            }
            else
            {
                runLen = 0;
            }
        }

        return false;
    }

    private static bool PathHasVerticalRun(
        List<(int x, int y)> path, int x, int yStart, int yEnd, int minRunLength)
    {
        int runLen = 0;
        for (int y = yStart; y < yEnd; y++)
        {
            if (PathContains(path, x, y))
            {
                runLen++;
                if (runLen >= minRunLength) return true;
            }
            else
            {
                runLen = 0;
            }
        }

        return false;
    }

    private static bool PathContains(List<(int x, int y)> path, int x, int y)
    {
        for (int i = 0; i < path.Count; i++)
            if (path[i].x == x && path[i].y == y)
                return true;

        return false;
    }

    private static bool SideHasBadDoorRun(
        int[,] grid, List<(int x, int y)> path, DungeonSettings s,
        int fixedCoord, bool horizontal, int rangeStart, int rangeEnd)
    {
        int runLen = 0;
        for (int k = rangeStart; k < rangeEnd; k++)
        {
            int x = horizontal ? k : fixedCoord;
            int y = horizontal ? fixedCoord : k;
            bool isDoor = InBounds(s, x, y) &&
                          (grid[y, x] == CORRIDOR || PathWouldCarveCorridor(grid, path, x, y));
            if (isDoor)
            {
                runLen++;
                if (runLen > 1) return true;
            }
            else
            {
                runLen = 0;
            }
        }

        return false;
    }

    private static bool PathWouldCarveCorridor(int[,] grid, List<(int x, int y)> path, int x, int y)
    {
        if (grid[y, x] == ROOM) return false;

        for (int i = 0; i < path.Count; i++)
            if (path[i].x == x && path[i].y == y)
                return true;

        return false;
    }

    private static bool InBounds(DungeonSettings s, int x, int y)
        => x >= 0 && x < s.MapWidth && y >= 0 && y < s.MapHeight;

    private static bool TryUseSharedDoorAxis(
        int srcStart, int srcEnd, int dstStart, int dstEnd, int preferred,
        out int srcAxis, out int dstAxis)
    {
        int overlapStart = Math.Max(srcStart, dstStart);
        int overlapEnd = Math.Min(srcEnd, dstEnd);
        if (overlapStart >= overlapEnd)
        {
            srcAxis = preferred;
            dstAxis = preferred;
            return false;
        }

        int shared = Math.Max(overlapStart, Math.Min(overlapEnd - 1, preferred));
        srcAxis = shared;
        dstAxis = shared;
        return true;
    }

    private static void SetTile(
        int[,] grid, HashSet<(int, int)> tiles,
        int x, int y, DungeonSettings s)
    {
        if (x < 0 || x >= s.MapWidth || y < 0 || y >= s.MapHeight) return;

        if (DebugCorridorCarving && _debugRooms != null)
            DebugCheckThirdRoomHit(x, y, grid);

        // 이미 방(ROOM) 타일이면 덮어쓰지 않음 — 방 바닥 값을 유지
        if (grid[y, x] != ROOM)
            grid[y, x] = CORRIDOR;
        tiles.Add((x, y));
    }

    private static void DebugCheckThirdRoomHit(int x, int y, int[,] grid)
    {
        for (int k = 0; k < _debugRooms.Count; k++)
        {
            if (k == _debugSrcIdx || k == _debugDstIdx) continue;
            var r = _debugRooms[k];
            bool interior = x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H;
            bool perimTB  = (y == r.Y - 1 || y == r.Y + r.H) && x >= r.X && x < r.X + r.W;
            bool perimLR  = (x == r.X - 1 || x == r.X + r.W) && y >= r.Y && y < r.Y + r.H;
            if (interior || perimTB || perimLR)
            {
                string kind = interior ? "INTERIOR" : (perimTB ? "PERIM-TB" : "PERIM-LR");
                DebugEmit("  [3rd-room " + kind + "] cell=(" + x + "," + y + ") on R" + k +
                          " (existing grid=" + grid[y, x] + ")");
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  유효성 검사
    // ══════════════════════════════════════════════════════════

    private static void ValidateSettings(ref DungeonSettings s)
    {
        if (s.MapWidth   < 10)  throw new ArgumentException("MapWidth must be >= 10");
        if (s.MapHeight  < 10)  throw new ArgumentException("MapHeight must be >= 10");
        if (s.MinRoomSize < 3)  throw new ArgumentException("MinRoomSize must be >= 3");
        if (s.MaxRoomSize < s.MinRoomSize)
            throw new ArgumentException("MaxRoomSize must be >= MinRoomSize");
        if (s.BspDepth < 1)     throw new ArgumentException("BspDepth must be >= 1");
        if (s.Padding < 1)      throw new ArgumentException("Padding must be >= 1");
        if (s.MinStraight < 1)  throw new ArgumentException("MinStraight must be >= 1");
        if (s.MaxFloor < 1)     s.MaxFloor = 100;
        s.Floor         = Math.Max(1, Math.Min(s.MaxFloor, s.Floor));
        s.ExtraConnProb = Math.Max(0f, Math.Min(1f, s.ExtraConnProb));
    }
}
