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

        while (remaining.Count > 0)
        {
            // ── 1st: MST — 가장 가까운 미연결 방 ──────────────────
            double bestDist = double.MaxValue;
            int srcIdx = -1, dstIdx = -1;

            foreach (int i in connected)
                foreach (int j in remaining)
                {
                    double d = EuclideanDist(rooms[i], rooms[j]);
                    if (d < bestDist) { bestDist = d; srcIdx = i; dstIdx = j; }
                }

            DrawLCorridor(grid, rooms[srcIdx], rooms[dstIdx], corridorTiles, s);
            connectedPairs.Add((Math.Min(srcIdx, dstIdx), Math.Max(srcIdx, dstIdx)));
            connected.Add(dstIdx);
            remaining.Remove(dstIdx);

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
                    DrawLCorridor(grid, rooms[srcIdx], rooms[bestK], corridorTiles, s);
                    connectedPairs.Add((Math.Min(srcIdx, bestK), Math.Max(srcIdx, bestK)));
                    if (remaining.Contains(bestK))
                    {
                        connected.Add(bestK);
                        remaining.Remove(bestK);
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
    /// [스텁 보장 — 코너-벽 거리]
    ///   코너는 각 방 벽에서 MinStraight 이상 떨어진 far_outside 에 위치
    ///   → 수직/수평 팔이 방 끝에 붙는 현상 원천 차단
    ///
    /// [팔 길이 보장 — 두 방 공동 계산]
    ///   두 far_outside 의 수직/수평 거리가 MinStraight 미만이면
    ///   도어 y/x 위치를 방 벽 범위 안에서 조정
    ///   → Z자형 우회 없이 깔끔한 L 2세그먼트로 완성
    /// </summary>
    private static void DrawLCorridor(
        int[,] grid, Room src, Room dst,
        HashSet<(int, int)> corridorTiles,
        DungeonSettings s)
    {
        int MIN = s.MinStraight;
        int dx = dst.Cx - src.Cx;
        int dy = dst.Cy - src.Cy;

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            // ── 수평 연결 ────────────────────────────────────────────
            int doorSX, farSX, doorEX, farEX, stepS, stepE;
            if (dx >= 0)   // src 우측 출구 → dst 좌측 출구
            {
                doorSX = src.X + src.W - 1; farSX = src.X + src.W + MIN - 1;
                doorEX = dst.X;             farEX = dst.X - MIN;
                stepS = 1; stepE = -1;
            }
            else           // src 좌측 출구 → dst 우측 출구
            {
                doorSX = src.X;             farSX = src.X - MIN;
                doorEX = dst.X + dst.W - 1; farEX = dst.X + dst.W + MIN - 1;
                stepS = -1; stepE = 1;
            }

            // 수직 팔 길이 보장: |sy - ey| >= MIN
            int sy = src.Cy, ey = dst.Cy;
            if (Math.Abs(sy - ey) < MIN)
            {
                int eyDir = (ey >= sy) ? 1 : -1;
                if (eyDir == 0) eyDir = 1;
                ey = Math.Max(dst.Y, Math.Min(dst.Y + dst.H - 1, sy + eyDir * MIN));
                if (Math.Abs(sy - ey) < MIN)
                    sy = Math.Max(src.Y, Math.Min(src.Y + src.H - 1, ey - eyDir * MIN));
            }

            // 도어 타일 등록
            SetTile(grid, corridorTiles, doorSX, sy, s);
            SetTile(grid, corridorTiles, doorEX, ey, s);

            // 스텁: door 다음 칸부터 far_outside 까지
            DrawHLine(grid, corridorTiles, doorSX + stepS, farSX, sy, s);
            DrawHLine(grid, corridorTiles, doorEX + stepE, farEX, ey, s);

            // L자: H(farSX → farEX, y=sy) + V(sy → ey, x=farEX)
            DrawHLine(grid, corridorTiles, farSX, farEX, sy, s);
            DrawVLine(grid, corridorTiles, sy, ey, farEX, s);
        }
        else
        {
            // ── 수직 연결 ────────────────────────────────────────────
            int doorSY, farSY, doorEY, farEY, stepS, stepE;
            if (dy >= 0)   // src 하단 출구 → dst 상단 출구
            {
                doorSY = src.Y + src.H - 1; farSY = src.Y + src.H + MIN - 1;
                doorEY = dst.Y;             farEY = dst.Y - MIN;
                stepS = 1; stepE = -1;
            }
            else           // src 상단 출구 → dst 하단 출구
            {
                doorSY = src.Y;             farSY = src.Y - MIN;
                doorEY = dst.Y + dst.H - 1; farEY = dst.Y + dst.H + MIN - 1;
                stepS = -1; stepE = 1;
            }

            // 수평 팔 길이 보장: |sx - ex| >= MIN
            int sx = src.Cx, ex = dst.Cx;
            if (Math.Abs(sx - ex) < MIN)
            {
                int exDir = (ex >= sx) ? 1 : -1;
                if (exDir == 0) exDir = 1;
                ex = Math.Max(dst.X, Math.Min(dst.X + dst.W - 1, sx + exDir * MIN));
                if (Math.Abs(sx - ex) < MIN)
                    sx = Math.Max(src.X, Math.Min(src.X + src.W - 1, ex - exDir * MIN));
            }

            // 도어 타일 등록
            SetTile(grid, corridorTiles, sx, doorSY, s);
            SetTile(grid, corridorTiles, ex, doorEY, s);

            // 스텁: door 다음 칸부터 far_outside 까지
            DrawVLine(grid, corridorTiles, doorSY + stepS, farSY, sx, s);
            DrawVLine(grid, corridorTiles, doorEY + stepE, farEY, ex, s);

            // L자: V(farSY → farEY, x=sx) + H(sx → ex, y=farEY)
            DrawVLine(grid, corridorTiles, farSY, farEY, sx, s);
            DrawHLine(grid, corridorTiles, sx, ex, farEY, s);
        }
    }

    private static void SetTile(
        int[,] grid, HashSet<(int, int)> tiles,
        int x, int y, DungeonSettings s)
    {
        if (x < 0 || x >= s.MapWidth || y < 0 || y >= s.MapHeight) return;
        // 이미 방(ROOM) 타일이면 덮어쓰지 않음 — 방 바닥 값을 유지
        if (grid[y, x] != ROOM)
            grid[y, x] = CORRIDOR;
        tiles.Add((x, y));
    }

    private static void DrawHLine(
        int[,] grid, HashSet<(int, int)> tiles,
        int x0, int x1, int y, DungeonSettings s)
    {
        int step = (x1 >= x0) ? 1 : -1;
        for (int x = x0; (step > 0) ? (x <= x1) : (x >= x1); x += step)
            SetTile(grid, tiles, x, y, s);
    }

    private static void DrawVLine(
        int[,] grid, HashSet<(int, int)> tiles,
        int y0, int y1, int x, DungeonSettings s)
    {
        int step = (y1 >= y0) ? 1 : -1;
        for (int y = y0; (step > 0) ? (y <= y1) : (y >= y1); y += step)
            SetTile(grid, tiles, x, y, s);
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

// ═══════════════════════════════════════════════════════════════
//  사용 예시 (콘솔 출력용 — Unity 등 게임 엔진에서는 이 부분 제거)
// ═══════════════════════════════════════════════════════════════
class Program
{
    static void Main()
    {
        // ── 예시 1: 기본 설정으로 생성 (매 실행마다 다름) ──────────
        int[,] map1 = DungeonGenerator.GenerateDungeon(DungeonSettings.Default);
        Console.WriteLine($"[Ex1] Random  — {map1.GetLength(1)}x{map1.GetLength(0)}");
        PrintStats(map1);

        // ── 예시 2: Seed+Floor 결정론적 생성 ────────────────────────
        // 같은 Seed + 같은 Floor = 항상 동일한 지형
        var s = DungeonSettings.Default;
        s.Seed  = 42;
        s.Floor = 1;
        int[,] map2a = DungeonGenerator.GenerateDungeon(s);
        int[,] map2b = DungeonGenerator.GenerateDungeon(s);   // 동일해야 함
        bool same = GridEqual(map2a, map2b);
        Console.WriteLine($"[Ex2] Seed=42 Floor=1 — 재현 동일: {same}");
        PrintStats(map2a);

        // ── 예시 3: 같은 Seed, 층마다 다른 지형 ────────────────────
        Console.WriteLine("[Ex3] Seed=42, 층별 walkable 수:");
        for (int floor = 1; floor <= 5; floor++)
        {
            s.Floor = floor;
            var map = DungeonGenerator.GenerateDungeon(s);
            int w = CountWalkable(map);
            Console.WriteLine($"  Floor {floor}: walkable={w}");
        }

        // ── 예시 4: 100층 전체 생성 ─────────────────────────────────
        s.Seed = 1234;
        Console.WriteLine($"[Ex4] Seed={s.Seed}, 1~100층 생성...");
        for (int floor = 1; floor <= 100; floor++)
        {
            s.Floor = floor;
            DungeonGenerator.GenerateDungeon(s);  // 실제 사용 시 결과를 저장
        }
        Console.WriteLine("  완료.");
    }

    static void PrintStats(int[,] grid)
    {
        int rooms = 0, corr = 0, total = grid.GetLength(0) * grid.GetLength(1);
        for (int y = 0; y < grid.GetLength(0); y++)
            for (int x = 0; x < grid.GetLength(1); x++)
            {
                if (grid[y, x] == DungeonGenerator.ROOM)     rooms++;
                if (grid[y, x] == DungeonGenerator.CORRIDOR) corr++;
            }
        Console.WriteLine($"  room={rooms}, corridor={corr}, walkable={rooms+corr}, total={total}");
    }

    static int CountWalkable(int[,] grid)
    {
        int count = 0;
        for (int y = 0; y < grid.GetLength(0); y++)
            for (int x = 0; x < grid.GetLength(1); x++)
                if (grid[y, x] == DungeonGenerator.ROOM ||
                    grid[y, x] == DungeonGenerator.CORRIDOR) count++;
        return count;
    }

    static bool GridEqual(int[,] a, int[,] b)
    {
        if (a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1)) return false;
        for (int y = 0; y < a.GetLength(0); y++)
            for (int x = 0; x < a.GetLength(1); x++)
                if (a[y, x] != b[y, x]) return false;
        return true;
    }
}
