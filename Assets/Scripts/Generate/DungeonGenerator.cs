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

    /// <summary>
    /// 2번째로 가까운 방에 추가 통로를 연결할 확률 (0.0 ~ 1.0)
    /// 0.0 = 추가 연결 없음 / 1.0 = 항상 추가 연결
    /// </summary>
    public float ExtraConnProb;

    /// <summary>
    /// 랜덤 시드 — null 이면 실행마다 다른 결과
    /// 재현이 필요할 때 임의의 정수를 지정하세요.
    /// </summary>
    public int? Seed;

    // ─── 기본 설정값 ───────────────────────────────────────────
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
    };
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
    // ── 내부 구조체 ────────────────────────────────────────────

    private struct Room
    {
        public int Cx, Cy;      // 방 중심 좌표
        public int X, Y, W, H; // 좌상단 좌표 및 크기
    }

    private class BSPNode
    {
        public int X, Y, W, H;
        public BSPNode Left, Right;

        public bool IsLeaf => Left == null && Right == null;

        public BSPNode(int x, int y, int w, int h)
        {
            X = x; Y = y; W = w; H = h;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  공개 메서드
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 던전을 절차적으로 생성합니다.
    /// </summary>
    /// <param name="settings">생성 설정값 (DungeonSettings.Default 참고)</param>
    /// <returns>
    /// int[height, width] 배열.
    /// grid[y, x] == 0 → 이동 불가 / grid[y, x] == 1 → 이동 가능
    /// </returns>
    public static int[,] GenerateDungeon(DungeonSettings settings)
    {
        // 설정 유효성 검사
        ValidateSettings(ref settings);

        var rng  = settings.Seed.HasValue ? new Random(settings.Seed.Value) : new Random();
        var grid = new int[settings.MapHeight, settings.MapWidth];
        var corridorTiles = new HashSet<(int x, int y)>();
        var rooms = new List<Room>();

        // ── Step 1: BSP 분할 → 방 수집 ──────────────────────────
        var root = new BSPNode(1, 1, settings.MapWidth - 2, settings.MapHeight - 2);
        BspSplit(root, 0, settings, rng);
        CollectRooms(root, settings, rng, rooms);

        // ── Step 2: 방을 그리드에 그리기 ────────────────────────
        foreach (var room in rooms)
            FillRoom(grid, room);

        // ── Step 3: 출입구 기반 L자형 통로 연결 (MST + 추가 연결) ─
        ConnectAll(grid, rooms, corridorTiles, settings, rng);

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
                grid[y, x] = 1;
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

        var connected = new HashSet<int> { 0 };
        var remaining = new HashSet<int>();
        for (int k = 1; k < n; k++) remaining.Add(k);

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
            connected.Add(dstIdx);
            remaining.Remove(dstIdx);

            // ── 2nd: 추가 연결 — 같은 소스(srcIdx) 기준 2번째로 가까운 방 ──
            // srcIdx → dstIdx (MST 연결) 이후,
            // srcIdx → bestK  (추가 연결, ExtraConnProb 확률)
            if (remaining.Count > 0 && rng.NextDouble() < s.ExtraConnProb)
            {
                double bestDist2 = double.MaxValue;
                int bestK = -1;

                foreach (int k in remaining)
                {
                    double d = EuclideanDist(rooms[srcIdx], rooms[k]);
                    if (d < bestDist2) { bestDist2 = d; bestK = k; }
                }

                if (bestK >= 0)
                {
                    DrawLCorridor(grid, rooms[srcIdx], rooms[bestK], corridorTiles, s);
                    connected.Add(bestK);
                    remaining.Remove(bestK);
                }
            }
        }
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
    /// 방에서 목적지 방향 면의 출입구를 계산합니다.
    ///
    ///  outside: 방 테두리 바깥 바로 옆 타일 → 통로 시작/끝 좌표
    ///  door   : 방 테두리 위의 문 타일    → corridorTiles에 등록해 보존
    ///
    /// [기존 center→center 방식]  : 통로 일부가 방 내부를 통과
    ///                              → L의 짧은 팔이 방 바깥 1칸에서 꺾임 (붙는 문제)
    /// [출입구 기반 방식]         : 통로 전체가 방 밖 빈 공간에서 그려짐
    ///                              → 방과 통로가 구조적으로 분리됨
    /// </summary>
    private static void GetExit(
        Room room, Room toward,
        out int outsideX, out int outsideY,
        out int doorX,    out int doorY)
    {
        int dx = toward.Cx - room.Cx;
        int dy = toward.Cy - room.Cy;

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            if (dx >= 0)  // 오른쪽 면
            {
                outsideX = room.X + room.W;     outsideY = room.Cy;
                doorX    = room.X + room.W - 1; doorY    = room.Cy;
            }
            else          // 왼쪽 면
            {
                outsideX = room.X - 1; outsideY = room.Cy;
                doorX    = room.X;     doorY    = room.Cy;
            }
        }
        else
        {
            if (dy >= 0)  // 아래쪽 면
            {
                outsideX = room.Cx; outsideY = room.Y + room.H;
                doorX    = room.Cx; doorY    = room.Y + room.H - 1;
            }
            else          // 위쪽 면
            {
                outsideX = room.Cx; outsideY = room.Y - 1;
                doorX    = room.Cx; doorY    = room.Y;
            }
        }
    }

    /// <summary>
    /// 두 방을 방 바깥 출입구 기준 L자형 통로로 연결합니다.
    ///
    /// [출입구 기반]  outside_A → outside_B 사이만 통로를 그림
    ///               → 방 내부를 통과하지 않음
    ///
    /// [최소 팔 보장] 두 outside 좌표 사이의 팔이 MIN(=2)칸 미만이면
    ///               Z자형 우회로로 MIN칸을 강제 확보
    ///               → 통로가 방에 바로 붙는 현상 방지
    ///
    ///  Z자형 예시 (수직 팔이 짧을 때):
    ///    outside_A ─────── tx          ← 수평 절반
    ///                      │ MIN칸     ← 수직 MIN 확보
    ///    outside_B ─────── tx          ← 수평 복귀
    /// </summary>
    private static void DrawLCorridor(
        int[,] grid, Room src, Room dst,
        HashSet<(int, int)> corridorTiles,
        DungeonSettings s)
    {
        const int MIN = 2;

        GetExit(src, dst, out int sx, out int sy, out int sdx, out int sdy);
        GetExit(dst, src, out int ex, out int ey, out int edx, out int edy);

        // 문 타일 등록 (방 테두리)
        SetTile(grid, corridorTiles, sdx, sdy, s);
        SetTile(grid, corridorTiles, edx, edy, s);

        int adx    = Math.Abs(ex - sx);
        int ady    = Math.Abs(ey - sy);
        int syDir  = (ey >= sy) ? 1 : -1;
        int sxDir  = (ex >= sx) ? 1 : -1;

        if (adx >= ady)
        {
            // 수평 우선 — 짧은 팔은 수직(ady)
            if (ady >= MIN)
            {
                // 정상 L자
                DrawHLine(grid, corridorTiles, sx, ex, sy, s);
                DrawVLine(grid, corridorTiles, sy, ey, ex, s);
            }
            else
            {
                // 수직 팔 부족 → Z자형으로 MIN칸 수직 확보
                int tx   = sx + (ex - sx) / 2;
                int midY = sy + syDir * MIN;
                DrawHLine(grid, corridorTiles, sx, tx,  sy,   s);
                DrawVLine(grid, corridorTiles, sy, midY, tx,   s);
                DrawHLine(grid, corridorTiles, tx, ex,  midY,  s);
                DrawVLine(grid, corridorTiles, midY, ey, ex,   s);
            }
        }
        else
        {
            // 수직 우선 — 짧은 팔은 수평(adx)
            if (adx >= MIN)
            {
                // 정상 L자
                DrawVLine(grid, corridorTiles, sy, ey, sx, s);
                DrawHLine(grid, corridorTiles, sx, ex, ey, s);
            }
            else
            {
                // 수평 팔 부족 → Z자형으로 MIN칸 수평 확보
                int ty   = sy + (ey - sy) / 2;
                int midX = sx + sxDir * MIN;
                DrawVLine(grid, corridorTiles, sy, ty,  sx,   s);
                DrawHLine(grid, corridorTiles, sx, midX, ty,   s);
                DrawVLine(grid, corridorTiles, ty, ey,  midX,  s);
                DrawHLine(grid, corridorTiles, midX, ex, ey,   s);
            }
        }
    }

    private static void SetTile(
        int[,] grid, HashSet<(int, int)> tiles,
        int x, int y, DungeonSettings s)
    {
        if (x < 0 || x >= s.MapWidth || y < 0 || y >= s.MapHeight) return;
        grid[y, x] = 1;
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
        s.ExtraConnProb = Math.Max(0f, Math.Min(1f, s.ExtraConnProb));
    }
}
