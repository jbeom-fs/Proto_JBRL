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

    /// <summary>각 방 pair마다 생성/점수화할 EXTRA path 후보 수</summary>
    public int ExtraCandidateCount;

    /// <summary>각 방마다 EXTRA 통로 엣지 후보로 고려할 최근접 이웃 방 수 (k)</summary>
    public int ExtraNeighborCount;

    /// <summary>EXTRA score bonus per cell overlapping an existing corridor.</summary>
    public int ExtraOverlapScoreWeight;

    /// <summary>EXTRA score penalty per path cell.</summary>
    public int ExtraPathLengthPenaltyWeight;

    /// <summary>EXTRA score divisor for squared room-center distance penalty.</summary>
    public int ExtraCenterDistancePenaltyDivisor;

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
        ExtraCandidateCount = 12,
        ExtraNeighborCount = 3,
        ExtraOverlapScoreWeight = 20,
        ExtraPathLengthPenaltyWeight = 8,
        ExtraCenterDistancePenaltyDivisor = 20,
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
        public bool IsElite;
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
        public bool IsElite;
    }

    public struct DungeonLayoutInfo
    {
        public bool ShouldHaveEliteRoom;
        public int EliteRoomIndex;
        public string EliteRoomWarning;
    }

    private struct ExtraRoomPair
    {
        public int A;
        public int B;
        public int CenterDistanceSq;
    }

    private struct ExtraCorridorCandidate
    {
        public int RoomAIndex;
        public int RoomBIndex;
        public int ExtraAttemptIndex;
        public int CandidateIndex;
        public bool PrimaryPath;
        public List<(int x, int y)> Path;
        public int PathLength;
        public int CenterDistanceSq;
        public int CorridorOverlapCount;
        public int Score;
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
        => GenerateDungeon(settings, out outRooms, out _);

    public static int[,] GenerateDungeon(
        DungeonSettings settings,
        out RoomRect[] outRooms,
        out DungeonLayoutInfo layoutInfo)
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

        layoutInfo = ConnectAll(grid, rooms, corridorTiles, settings, rng);
        PlaceStairs(grid, rooms, settings, rng);

        // Room → RoomRect 변환 후 반환
        outRooms = new RoomRect[rooms.Count];
        for (int i = 0; i < rooms.Count; i++)
            outRooms[i] = new RoomRect { X=rooms[i].X, Y=rooms[i].Y,
                                         W=rooms[i].W, H=rooms[i].H,
                                         IsElite=rooms[i].IsElite };
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
    private static DungeonLayoutInfo ConnectAll(
        int[,] grid, List<Room> rooms,
        HashSet<(int, int)> corridorTiles,
        DungeonSettings s, Random rng)
    {
        var layoutInfo = new DungeonLayoutInfo
        {
            ShouldHaveEliteRoom = IsEliteFloor(s.Floor),
            EliteRoomIndex = -1,
            EliteRoomWarning = null,
        };

        int n = rooms.Count;
        if (n < 2)
        {
            if (layoutInfo.ShouldHaveEliteRoom)
                layoutInfo.EliteRoomWarning = "Elite floor has fewer than two rooms; elite room was not assigned.";
            return layoutInfo;
        }

        var connected      = new HashSet<int> { 0 };
        var remaining      = new HashSet<int>();
        for (int k = 1; k < n; k++) remaining.Add(k);
        var mstEdges = new List<(int a, int b)>(n - 1);

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
            mstEdges.Add((srcIdx, dstIdx));
            connected.Add(dstIdx);
            remaining.Remove(dstIdx);
            if (DebugCorridorCarving)
            {
                DebugEmit("  state-update type=MST pairAdded=" + (!mstPairAlreadyConnected) +
                          " connectedAdd=R" + dstIdx + " remainingRemove=R" + dstIdx);
                DebugConnectState("  after  step=" + debugStep + " type=MST",
                    grid, rooms, connected, remaining, connectedPairs, s);
            }

        }

        if (layoutInfo.ShouldHaveEliteRoom)
            AssignEliteRoom(rooms, mstEdges, ref layoutInfo);

        ConnectExtraCorridors(grid, rooms, corridorTiles, connectedPairs, s, rng, pathBuf, layoutInfo.EliteRoomIndex);
        return layoutInfo;
    }

    private static void ConnectExtraCorridors(
        int[,] grid, List<Room> rooms,
        HashSet<(int, int)> corridorTiles,
        HashSet<(int, int)> connectedPairs,
        DungeonSettings s, Random rng,
        List<(int x, int y)> pathBuf,
        int eliteRoomIndex)
    {
        if (s.ExtraConnProb <= 0f || s.ExtraCandidateCount <= 0 || s.ExtraNeighborCount <= 0) return;

        var edgeCandidates = BuildExtraEdgeCandidates(rooms, connectedPairs, s, eliteRoomIndex);
        if (DebugCorridorCarving)
        {
            DebugEmit("--- EXTRA edge-candidates: count=" + edgeCandidates.Count +
                      " k=" + s.ExtraNeighborCount + " ---");
        }

        for (int i = edgeCandidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            var tmp = edgeCandidates[i];
            edgeCandidates[i] = edgeCandidates[j];
            edgeCandidates[j] = tmp;
        }

        var pairCandidates = new List<ExtraCorridorCandidate>(s.ExtraCandidateCount);

        for (int edgeIndex = 0; edgeIndex < edgeCandidates.Count; edgeIndex++)
        {
            var pair = edgeCandidates[edgeIndex];
            if (rng.NextDouble() >= s.ExtraConnProb)
            {
                if (DebugCorridorCarving)
                    DebugEmit("--- EXTRA edge=" + edgeIndex +
                              " src=R" + pair.A + " dst=R" + pair.B +
                              " roll=skip ---");
                continue;
            }

            pairCandidates.Clear();
            BuildExtraPathCandidatesForPair(grid, rooms, pair, s, edgeIndex, pathBuf, pairCandidates);
            if (pairCandidates.Count == 0)
            {
                if (DebugCorridorCarving)
                    DebugEmit("  extra-pair edge=" + edgeIndex +
                              " src=R" + pair.A + " dst=R" + pair.B +
                              " requested=" + s.ExtraCandidateCount +
                              " clean=0 best=none");
                continue;
            }

            int bestIndex = SelectBestExtraCorridorCandidate(pairCandidates);
            var selected = pairCandidates[bestIndex];
            if (DebugCorridorCarving)
            {
                DebugEmit("  extra-pair edge=" + edgeIndex +
                          " src=R" + pair.A + " dst=R" + pair.B +
                          " requested=" + s.ExtraCandidateCount +
                          " clean=" + pairCandidates.Count +
                          " bestCandidate=" + selected.CandidateIndex +
                          " score=" + selected.Score +
                          " cells=" + selected.PathLength +
                          " overlap=" + selected.CorridorOverlapCount);
                DebugEmit("--- EXTRA corridor selected: edge=" + edgeIndex +
                          " src=R" + selected.RoomAIndex +
                          " dst=R" + selected.RoomBIndex +
                          " candidate=" + selected.CandidateIndex +
                          " path=" + (selected.PrimaryPath ? "primary" : "alternate") +
                          " score=" + selected.Score +
                          " cells=" + selected.PathLength +
                          " overlap=" + selected.CorridorOverlapCount + " ---");
            }

            _debugRooms = rooms;
            _debugSrcIdx = selected.RoomAIndex;
            _debugDstIdx = selected.RoomBIndex;
            CarvePath(grid, corridorTiles, selected.Path, s);
            connectedPairs.Add((Math.Min(selected.RoomAIndex, selected.RoomBIndex),
                                Math.Max(selected.RoomAIndex, selected.RoomBIndex)));
        }
    }

    private static List<ExtraRoomPair> BuildExtraEdgeCandidates(
        List<Room> rooms,
        HashSet<(int, int)> connectedPairs,
        DungeonSettings s,
        int eliteRoomIndex)
    {
        var edges = new List<ExtraRoomPair>();
        int neighborCount = Math.Max(0, s.ExtraNeighborCount);
        if (neighborCount == 0 || rooms.Count < 2) return edges;

        var edgeKeys = new HashSet<(int, int)>();
        var neighborIndices = new int[Math.Max(0, rooms.Count - 1)];
        var neighborDistances = new int[Math.Max(0, rooms.Count - 1)];

        for (int i = 0; i < rooms.Count; i++)
        {
            if (IsExtraRoomExcluded(rooms, i, eliteRoomIndex)) continue;

            int candidateCount = 0;
            for (int j = 0; j < rooms.Count; j++)
            {
                if (j == i) continue;
                if (IsExtraRoomExcluded(rooms, j, eliteRoomIndex)) continue;

                neighborIndices[candidateCount] = j;
                neighborDistances[candidateCount] = CenterDistanceSq(rooms[i], rooms[j]);
                candidateCount++;
            }

            int limit = Math.Min(neighborCount, candidateCount);
            for (int rank = 0; rank < limit; rank++)
            {
                int best = rank;
                for (int cursor = rank + 1; cursor < candidateCount; cursor++)
                {
                    if (IsBetterExtraNeighbor(
                        neighborDistances[cursor], neighborIndices[cursor],
                        neighborDistances[best], neighborIndices[best]))
                    {
                        best = cursor;
                    }
                }

                if (best != rank)
                {
                    int tmpIndex = neighborIndices[rank];
                    neighborIndices[rank] = neighborIndices[best];
                    neighborIndices[best] = tmpIndex;

                    int tmpDistance = neighborDistances[rank];
                    neighborDistances[rank] = neighborDistances[best];
                    neighborDistances[best] = tmpDistance;
                }

                int a = Math.Min(i, neighborIndices[rank]);
                int b = Math.Max(i, neighborIndices[rank]);
                var key = (a, b);
                if (connectedPairs.Contains(key) || edgeKeys.Contains(key)) continue;

                edgeKeys.Add(key);
                edges.Add(new ExtraRoomPair
                {
                    A = a,
                    B = b,
                    CenterDistanceSq = neighborDistances[rank],
                });
            }
        }

        return edges;
    }

    private static bool IsExtraRoomExcluded(List<Room> rooms, int roomIndex, int eliteRoomIndex)
        => roomIndex == eliteRoomIndex || rooms[roomIndex].IsElite;

    private static bool IsBetterExtraNeighbor(
        int candidateDistance, int candidateIndex,
        int bestDistance, int bestIndex)
    {
        if (candidateDistance != bestDistance) return candidateDistance < bestDistance;
        return candidateIndex < bestIndex;
    }

    private static bool IsEliteFloor(int floor)
        => floor > 0 && floor % 10 == 5;

    private static void AssignEliteRoom(
        List<Room> rooms,
        List<(int a, int b)> mstEdges,
        ref DungeonLayoutInfo layoutInfo)
    {
        int n = rooms.Count;
        var degree = new int[n];
        var depth = new int[n];
        for (int i = 0; i < depth.Length; i++)
            depth[i] = -1;

        for (int i = 0; i < mstEdges.Count; i++)
        {
            degree[mstEdges[i].a]++;
            degree[mstEdges[i].b]++;
        }

        BuildMstDepths(n, mstEdges, depth);

        int best = -1;
        for (int i = 1; i < n; i++)
        {
            if (degree[i] != 1) continue;
            if (IsBetterEliteRoomCandidate(rooms, depth, i, best))
                best = i;
        }

        if (best < 0)
        {
            layoutInfo.EliteRoomWarning = "No MST leaf room was found; using farthest non-start room as elite fallback.";
            for (int i = 1; i < n; i++)
                if (IsBetterEliteRoomCandidate(rooms, depth, i, best))
                    best = i;
        }

        if (best < 0)
        {
            layoutInfo.EliteRoomWarning = "Elite room assignment failed; no non-start room exists.";
            return;
        }

        var room = rooms[best];
        room.IsElite = true;
        rooms[best] = room;
        layoutInfo.EliteRoomIndex = best;
    }

    private static void BuildMstDepths(int roomCount, List<(int a, int b)> mstEdges, int[] depth)
    {
        var queue = new Queue<int>();
        depth[0] = 0;
        queue.Enqueue(0);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            for (int i = 0; i < mstEdges.Count; i++)
            {
                int next = -1;
                if (mstEdges[i].a == current)
                    next = mstEdges[i].b;
                else if (mstEdges[i].b == current)
                    next = mstEdges[i].a;

                if (next < 0 || next >= roomCount || depth[next] >= 0) continue;
                depth[next] = depth[current] + 1;
                queue.Enqueue(next);
            }
        }
    }

    private static bool IsBetterEliteRoomCandidate(List<Room> rooms, int[] depth, int candidate, int best)
    {
        if (best < 0) return true;

        int candidateDepth = depth[candidate];
        int bestDepth = depth[best];
        if (candidateDepth != bestDepth)
            return candidateDepth > bestDepth;

        int candidateDistance = CenterDistanceSq(rooms[0], rooms[candidate]);
        int bestDistance = CenterDistanceSq(rooms[0], rooms[best]);
        if (candidateDistance != bestDistance)
            return candidateDistance > bestDistance;

        return candidate < best;
    }

    private static void BuildExtraPathCandidatesForPair(
        int[,] grid, List<Room> rooms, ExtraRoomPair pair,
        DungeonSettings s, int attemptIndex, List<(int x, int y)> pathBuf,
        List<ExtraCorridorCandidate> candidates)
    {
        var a = rooms[pair.A];
        var b = rooms[pair.B];
        bool primaryHorizFirst = Math.Abs(b.Cx - a.Cx) >= Math.Abs(b.Cy - a.Cy);

        for (int candidateIndex = 0; candidateIndex < s.ExtraCandidateCount; candidateIndex++)
        {
            bool primaryPath = (candidateIndex % 2) == 0;
            bool horizFirst = primaryPath ? primaryHorizFirst : !primaryHorizFirst;
            EmitExtraPathCandidate(rooms[pair.A], rooms[pair.B], s, horizFirst, candidateIndex, pathBuf);
            TryAddExtraCandidate(grid, rooms, pair, s, primaryPath, attemptIndex, candidateIndex, pathBuf, candidates);
        }
    }

    private static void TryAddExtraCandidate(
        int[,] grid, List<Room> rooms, ExtraRoomPair pair,
        DungeonSettings s, bool primaryPath, int attemptIndex, int candidateIndex,
        List<(int x, int y)> pathBuf,
        List<ExtraCorridorCandidate> candidates)
    {
        if (!IsCorridorCandidateClean(grid, pathBuf, rooms, pair.A, pair.B, s, false))
            return;

        if (ExtraCandidatePathAlreadyAdded(candidates, pathBuf))
            return;

        int pathLength = pathBuf.Count;
        int overlap = CountCorridorOverlap(grid, pathBuf, s);
        int score = ScoreExtraCorridorCandidate(pathLength, pair.CenterDistanceSq, overlap, s);
        var pathCopy = new List<(int x, int y)>(pathLength);
        for (int i = 0; i < pathBuf.Count; i++)
            pathCopy.Add(pathBuf[i]);

        candidates.Add(new ExtraCorridorCandidate
        {
            RoomAIndex = pair.A,
            RoomBIndex = pair.B,
            ExtraAttemptIndex = attemptIndex,
            CandidateIndex = candidateIndex,
            PrimaryPath = primaryPath,
            Path = pathCopy,
            PathLength = pathLength,
            CenterDistanceSq = pair.CenterDistanceSq,
            CorridorOverlapCount = overlap,
            Score = score,
        });
    }

    private static int SelectBestExtraCorridorCandidate(List<ExtraCorridorCandidate> candidates)
    {
        int best = 0;
        for (int i = 1; i < candidates.Count; i++)
        {
            if (IsBetterExtraCorridorCandidate(candidates[i], candidates[best]))
                best = i;
        }

        return best;
    }

    private static bool IsBetterExtraCorridorCandidate(
        ExtraCorridorCandidate a, ExtraCorridorCandidate b)
    {
        if (a.Score != b.Score) return a.Score > b.Score;
        if (a.PathLength != b.PathLength) return a.PathLength < b.PathLength;
        if (a.CorridorOverlapCount != b.CorridorOverlapCount)
            return a.CorridorOverlapCount > b.CorridorOverlapCount;
        if (a.CenterDistanceSq != b.CenterDistanceSq) return a.CenterDistanceSq < b.CenterDistanceSq;
        if (a.RoomAIndex != b.RoomAIndex) return a.RoomAIndex < b.RoomAIndex;
        if (a.RoomBIndex != b.RoomBIndex) return a.RoomBIndex < b.RoomBIndex;
        if (a.CandidateIndex != b.CandidateIndex) return a.CandidateIndex < b.CandidateIndex;
        if (a.ExtraAttemptIndex != b.ExtraAttemptIndex) return a.ExtraAttemptIndex < b.ExtraAttemptIndex;
        if (a.PrimaryPath != b.PrimaryPath) return a.PrimaryPath;

        return false;
    }

    private static void EmitExtraPathCandidate(
        Room src, Room dst, DungeonSettings s, bool horizFirst,
        int candidateIndex, List<(int x, int y)> path)
    {
        int variant = candidateIndex / 2;
        if (variant == 0)
        {
            EmitLCorridorPath(null, src, dst, s, horizFirst, path);
            return;
        }

        if (horizFirst)
        {
            int sy = PickExtraAxis(src.Y, src.Y + src.H, src.Cy, dst.Cy,
                dst.Y, dst.Y + dst.H, variant);
            int ey = PickExtraAxis(dst.Y, dst.Y + dst.H, dst.Cy, src.Cy,
                src.Y, src.Y + src.H, variant);
            EmitLCorridorPathWithAxes(src, dst, s, true, sy, ey, path);
        }
        else
        {
            int sx = PickExtraAxis(src.X, src.X + src.W, src.Cx, dst.Cx,
                dst.X, dst.X + dst.W, variant);
            int ex = PickExtraAxis(dst.X, dst.X + dst.W, dst.Cx, src.Cx,
                src.X, src.X + src.W, variant);
            EmitLCorridorPathWithAxes(src, dst, s, false, sx, ex, path);
        }
    }

    private static int PickExtraAxis(
        int start, int end, int center, int targetCenter,
        int otherStart, int otherEnd, int variant)
    {
        int overlapStart = Math.Max(start, otherStart);
        int overlapEnd = Math.Min(end, otherEnd);
        if (variant == 1 && overlapStart < overlapEnd)
            return ClampInt((overlapStart + overlapEnd - 1) / 2, start, end - 1);

        switch (variant % 6)
        {
            case 1:
                return ClampInt(targetCenter, start, end - 1);
            case 2:
                return ClampInt(center - Math.Max(1, (end - start) / 4), start, end - 1);
            case 3:
                return ClampInt(center + Math.Max(1, (end - start) / 4), start, end - 1);
            case 4:
                return start;
            case 5:
                return end - 1;
            default:
                return center;
        }
    }

    private static void EmitLCorridorPathWithAxes(
        Room src, Room dst, DungeonSettings s, bool horizFirst,
        int srcAxis, int dstAxis, List<(int x, int y)> path)
    {
        path.Clear();
        int min = s.MinStraight;
        int dx = dst.Cx - src.Cx;
        int dy = dst.Cy - src.Cy;

        if (horizFirst)
        {
            int doorSX, farSX, doorEX, farEX, stepS, stepE;
            if (dx >= 0)
            {
                doorSX = src.X + src.W - 1; farSX = src.X + src.W + min - 1;
                doorEX = dst.X;             farEX = dst.X - min;
                stepS = 1; stepE = -1;
            }
            else
            {
                doorSX = src.X;             farSX = src.X - min;
                doorEX = dst.X + dst.W - 1; farEX = dst.X + dst.W + min - 1;
                stepS = -1; stepE = 1;
            }

            int sy = ClampDoorAxis(srcAxis, src.Y, src.Y + src.H);
            int ey = ClampDoorAxis(dstAxis, dst.Y, dst.Y + dst.H);
            EmitH(doorSX + stepS, farSX, sy, path);
            EmitH(doorEX + stepE, farEX, ey, path);
            EmitH(farSX, farEX, sy, path);
            EmitV(sy, ey, farEX, path);
        }
        else
        {
            int doorSY, farSY, doorEY, farEY, stepS, stepE;
            if (dy >= 0)
            {
                doorSY = src.Y + src.H - 1; farSY = src.Y + src.H + min - 1;
                doorEY = dst.Y;             farEY = dst.Y - min;
                stepS = 1; stepE = -1;
            }
            else
            {
                doorSY = src.Y;             farSY = src.Y - min;
                doorEY = dst.Y + dst.H - 1; farEY = dst.Y + dst.H + min - 1;
                stepS = -1; stepE = 1;
            }

            int sx = ClampDoorAxis(srcAxis, src.X, src.X + src.W);
            int ex = ClampDoorAxis(dstAxis, dst.X, dst.X + dst.W);
            EmitV(doorSY + stepS, farSY, sx, path);
            EmitV(doorEY + stepE, farEY, ex, path);
            EmitV(farSY, farEY, sx, path);
            EmitH(sx, ex, farEY, path);
        }
    }

    private static bool ExtraCandidatePathAlreadyAdded(
        List<ExtraCorridorCandidate> candidates, List<(int x, int y)> path)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            var existing = candidates[i].Path;
            if (existing.Count != path.Count) continue;

            bool same = true;
            for (int j = 0; j < path.Count; j++)
            {
                if (existing[j] != path[j])
                {
                    same = false;
                    break;
                }
            }

            if (same) return true;
        }

        return false;
    }

    private static int ClampInt(int value, int min, int max)
        => Math.Max(min, Math.Min(max, value));

    private static int ClampDoorAxis(int value, int start, int end)
    {
        if (end - start <= 2)
            return ClampInt(value, start, end - 1);

        return ClampInt(value, start + 1, end - 2);
    }

    private static int ScoreExtraCorridorCandidate(
        int pathLength, int centerDistanceSq, int corridorOverlapCount,
        DungeonSettings s)
    {
        int distanceDivisor = Math.Max(1, s.ExtraCenterDistancePenaltyDivisor);
        return corridorOverlapCount * s.ExtraOverlapScoreWeight
             - pathLength * s.ExtraPathLengthPenaltyWeight
             - centerDistanceSq / distanceDivisor;
    }

    private static int CountCorridorOverlap(
        int[,] grid, List<(int x, int y)> path, DungeonSettings s)
    {
        int count = 0;
        for (int i = 0; i < path.Count; i++)
        {
            int x = path[i].x;
            int y = path[i].y;
            if (InBounds(s, x, y) && grid[y, x] == CORRIDOR)
                count++;
        }

        return count;
    }

    private static int CenterDistanceSq(Room a, Room b)
    {
        int dx = b.Cx - a.Cx;
        int dy = b.Cy - a.Cy;
        return dx * dx + dy * dy;
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
            sy = ClampDoorAxis(sy, src.Y, src.Y + src.H);
            ey = ClampDoorAxis(ey, dst.Y, dst.Y + dst.H);

            // 턴 라인(farEX)이 src 방의 가로 범위 안이면 가로 출구가 방 밖으로 오버슈트해
            // 고아 stub을 남긴다(폭 넓은 방의 대칭 케이스). 가로 출구를 생략하고 세로가
            // 위/아래 벽으로 바로 빠지게 한다.
            if (farEX >= src.X && farEX <= src.X + src.W - 1)
            {
                EmitH(doorEX + stepE, farEX, ey, path);
                EmitV(sy, ey, farEX, path);
            }
            else
            {
                EmitH(doorSX + stepS, farSX, sy, path);
                EmitH(doorEX + stepE, farEX, ey, path);
                EmitH(farSX, farEX, sy, path);
                EmitV(sy, ey, farEX, path);
            }
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
            sx = ClampDoorAxis(sx, src.X, src.X + src.W);
            ex = ClampDoorAxis(ex, dst.X, dst.X + dst.W);

            // 턴 라인(farEY)이 src 방의 세로 범위 안이면, 세로 출구를 방 밖으로 내보내면
            // 방 반대편에 고아 dead-end stub이 남는다(키 큰 방이 위쪽 변에서 꺾이는 방과
            // V-first로 이어질 때). 이 경우 세로 출구를 생략하고 수평이 측벽으로 바로
            // 빠지게 한다 — 연결성은 그대로 유지된다.
            if (farEY >= src.Y && farEY <= src.Y + src.H - 1)
            {
                EmitV(doorEY + stepE, farEY, ex, path);
                EmitH(sx, ex, farEY, path);
            }
            else
            {
                EmitV(doorSY + stepS, farSY, sx, path);
                EmitV(doorEY + stepE, farEY, ex, path);
                EmitV(farSY, farEY, sx, path);
                EmitH(sx, ex, farEY, path);
            }
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
        if (!isMandatoryEdge &&
            PathCarvesRoomPerimeter(path, rooms, srcIdx, dstIdx))
            return false;

        if (!isMandatoryEdge &&
            PathUsesRoomCornerDoorway(path, rooms, srcIdx, dstIdx))
            return false;

        if (PathHitsThirdRoom(path, rooms, srcIdx, dstIdx)) return false;
        if (PathCreatesBadDoorRun(grid, path, rooms, s)) return false;
        if (PathCreatesOrphanDoorStub(grid, path, rooms, s)) return false;

        if (!isMandatoryEdge &&
            PathCreatesOutwardRoomStub(path, rooms[srcIdx], rooms[dstIdx], s))
        {
            if (DebugCorridorCarving) DebugEmit("  reject=outward-room-stub");
            return false;
        }

        if (!isMandatoryEdge &&
            PathCreatesDiagonalRoomStub(path, rooms[srcIdx], rooms[dstIdx]))
        {
            if (DebugCorridorCarving) DebugEmit("  reject=diagonal-room-stub");
            return false;
        }

        // Extra corridors are optional, so reject long side-parallel runs near
        // third rooms while still allowing short perpendicular door stubs.
        if (!isMandatoryEdge &&
            PathCreatesThirdRoomParallelRun(path, rooms, srcIdx, dstIdx, s))
            return false;

        return true;
    }

    private static bool PathCarvesRoomPerimeter(
        List<(int x, int y)> path, List<Room> rooms, int srcIdx, int dstIdx)
    {
        for (int i = 0; i < path.Count; i++)
        {
            int x = path[i].x;
            int y = path[i].y;
            for (int rIdx = 0; rIdx < rooms.Count; rIdx++)
            {
                var r = rooms[rIdx];
                if (!IsRoomPerimeterCell(x, y, r)) continue;

                if (DebugCorridorCarving)
                {
                    string role = rIdx == srcIdx ? "src" : (rIdx == dstIdx ? "dst" : "third");
                    DebugEmit("  reject=room-perimeter-corridor room=R" + rIdx +
                              " role=" + role +
                              " cell=(" + x + "," + y + ")" +
                              " src=R" + srcIdx +
                              " dst=R" + dstIdx);
                }
                return true;
            }
        }

        return false;
    }

    private static bool IsRoomPerimeterCell(int x, int y, Room room)
    {
        if (x < room.X || x >= room.X + room.W ||
            y < room.Y || y >= room.Y + room.H)
            return false;

        return x == room.X || x == room.X + room.W - 1 ||
               y == room.Y || y == room.Y + room.H - 1;
    }

    private static bool PathUsesRoomCornerDoorway(
        List<(int x, int y)> path, List<Room> rooms, int srcIdx, int dstIdx)
    {
        return PathUsesRoomCornerDoorway(path, rooms[srcIdx], srcIdx, "src") ||
               PathUsesRoomCornerDoorway(path, rooms[dstIdx], dstIdx, "dst");
    }

    private static bool PathUsesRoomCornerDoorway(
        List<(int x, int y)> path, Room room, int roomIdx, string role)
    {
        for (int i = 0; i < path.Count; i++)
        {
            int x = path[i].x;
            int y = path[i].y;
            string side;
            int anchorX;
            int anchorY;
            if (!TryGetDoorAnchorFromAdjacentCell(x, y, room, out side, out anchorX, out anchorY))
                continue;

            if (!IsCornerDoorAnchor(anchorX, anchorY, room, side))
                continue;

            if (DebugCorridorCarving)
                DebugEmit("  reject=corner-doorway room=R" + roomIdx +
                          " role=" + role +
                          " side=" + side +
                          " anchor=(" + anchorX + "," + anchorY + ")");
            return true;
        }

        return false;
    }

    private static bool TryGetDoorAnchorFromAdjacentCell(
        int x, int y, Room room, out string side, out int anchorX, out int anchorY)
    {
        if (x == room.X - 1 && y >= room.Y && y < room.Y + room.H)
        {
            side = "left";
            anchorX = room.X;
            anchorY = y;
            return true;
        }

        if (x == room.X + room.W && y >= room.Y && y < room.Y + room.H)
        {
            side = "right";
            anchorX = room.X + room.W - 1;
            anchorY = y;
            return true;
        }

        if (y == room.Y - 1 && x >= room.X && x < room.X + room.W)
        {
            side = "top";
            anchorX = x;
            anchorY = room.Y;
            return true;
        }

        if (y == room.Y + room.H && x >= room.X && x < room.X + room.W)
        {
            side = "bottom";
            anchorX = x;
            anchorY = room.Y + room.H - 1;
            return true;
        }

        side = "";
        anchorX = 0;
        anchorY = 0;
        return false;
    }

    private static bool IsCornerDoorAnchor(int anchorX, int anchorY, Room room, string side)
    {
        if (side == "left" || side == "right")
            return anchorY == room.Y || anchorY == room.Y + room.H - 1;

        return anchorX == room.X || anchorX == room.X + room.W - 1;
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

    private static bool PathCreatesDiagonalRoomStub(
        List<(int x, int y)> path, Room src, Room dst)
    {
        bool separatedX = src.X + src.W <= dst.X || dst.X + dst.W <= src.X;
        bool separatedY = src.Y + src.H <= dst.Y || dst.Y + dst.H <= src.Y;
        if (!separatedX || !separatedY) return false;

        if (PathTouchesTopOrBottomSide(path, src) &&
            PathTouchesLeftOrRightSide(path, src))
            return true;

        if (PathTouchesTopOrBottomSide(path, dst) &&
            PathTouchesLeftOrRightSide(path, dst))
            return true;

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
        s.ExtraCandidateCount = Math.Max(0, s.ExtraCandidateCount);
        s.ExtraNeighborCount = Math.Max(0, s.ExtraNeighborCount);
        s.ExtraOverlapScoreWeight = Math.Max(0, s.ExtraOverlapScoreWeight);
        s.ExtraPathLengthPenaltyWeight = Math.Max(0, s.ExtraPathLengthPenaltyWeight);
        s.ExtraCenterDistancePenaltyDivisor = Math.Max(1, s.ExtraCenterDistancePenaltyDivisor);
    }
}
