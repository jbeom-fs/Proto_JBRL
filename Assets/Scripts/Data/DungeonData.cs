// ═══════════════════════════════════════════════════════════════════
//  DungeonData.cs
//  Domain Layer — 던전의 순수 데이터와 쿼리
//
//  책임:
//    • 그리드 배열 보유 및 타일 값 조회·변경
//    • 방 목록 보유 및 위치 쿼리
//    • Unity 의존 없음 (Tilemap, MonoBehaviour 모름)
// ═══════════════════════════════════════════════════════════════════

public class DungeonData
{
    // ── 그리드 ─────────────────────────────────────────────────────
    private readonly int[,] _grid;
    private readonly int[,] _originalGrid;  // 문 복원용 원본

    public int MapWidth  { get; }
    public int MapHeight { get; }

    // ── 방 목록 ────────────────────────────────────────────────────
    private readonly RoomInfo[] _rooms;
    public int RoomCount => _rooms.Length;

    /// <summary>
    /// 각 그리드 셀이 속한 방의 인덱스를 저장합니다.
    /// -1 이면 방이 아닌 타일(통로·벽)입니다.
    /// GetRoomAt() 을 O(n) → O(1) 로 만드는 핵심 자료구조입니다.
    /// </summary>
    private readonly int[,] _roomIndex;

    // ── 생성자 ─────────────────────────────────────────────────────
    public DungeonData(int[,] grid, RoomInfo[] rooms)
    {
        _grid         = grid;
        _originalGrid = (int[,])grid.Clone();
        _rooms        = rooms;
        MapWidth      = grid.GetLength(1);
        MapHeight     = grid.GetLength(0);

        // 생성 시 1회 순회로 인덱스 맵 구축
        _roomIndex    = BuildRoomIndex(rooms, MapWidth, MapHeight);
    }

    // ── 타일 쿼리 ──────────────────────────────────────────────────

    /// <summary>그리드 좌표(col, row)의 타일 타입을 반환합니다.</summary>
    public int GetTileType(int col, int row)
    {
        if (!InBounds(col, row)) return DungeonGenerator.EMPTY;
        return _grid[row, col];
    }

    /// <summary>플레이어가 이동 가능한 타일인지 반환합니다.</summary>
    public bool IsWalkable(int col, int row)
    {
        if (!InBounds(col, row)) return false;
        int v = _grid[row, col];
        return v == DungeonGenerator.ROOM     ||
               v == DungeonGenerator.CORRIDOR ||
               v == DungeonGenerator.STAIR_UP;
    }

    /// <summary>타일 값을 직접 변경합니다 (문 개폐에 사용).</summary>
    public void SetTileValue(int col, int row, int value)
    {
        if (InBounds(col, row))
            _grid[row, col] = value;
    }

    /// <summary>원본 그리드에서 해당 위치의 값을 반환합니다 (문 복원용).</summary>
    public int GetOriginalTileValue(int col, int row)
    {
        if (!InBounds(col, row)) return DungeonGenerator.EMPTY;
        return _originalGrid[row, col];
    }

    /// <summary>전체 그리드를 순회합니다 (Tilemap 배치용).</summary>
    public void ForEachTile(System.Action<int, int, int> action)
    {
        for (int row = 0; row < MapHeight; row++)
            for (int col = 0; col < MapWidth; col++)
                action(col, row, _grid[row, col]);
    }

    // ── 방 쿼리 ────────────────────────────────────────────────────

    /// <summary>그리드 좌표가 속한 방을 O(1)로 반환합니다.</summary>
    public RoomInfo? GetRoomAt(int col, int row)
    {
        if (!InBounds(col, row)) return null;
        int idx = _roomIndex[row, col];
        if (idx < 0) return null;
        return _rooms[idx];
    }

    /// <summary>특정 타일 타입을 포함한 방을 반환합니다. 없으면 null.</summary>
    public RoomInfo? GetRoomContaining(int tileType)
    {
        for (int row = 0; row < MapHeight; row++)
            for (int col = 0; col < MapWidth; col++)
            {
                if (_grid[row, col] != tileType) continue;
                var room = GetRoomAt(col, row);
                if (room.HasValue) return room;
            }
        return null;
    }

    /// <summary>모든 방을 순회합니다.</summary>
    public void ForEachRoom(System.Action<RoomInfo> action)
    {
        foreach (var r in _rooms) action(r);
    }

    /// <summary>인덱스로 방을 반환합니다.</summary>
    public RoomInfo GetRoom(int index) => _rooms[index];

    // ── 내부 빌더 ──────────────────────────────────────────────────

    /// <summary>
    /// 방 배열을 순회해 각 셀에 방 인덱스를 기록한 2D 맵을 생성합니다.
    /// 생성자에서 1회만 호출됩니다.
    /// </summary>
    private static int[,] BuildRoomIndex(RoomInfo[] rooms, int width, int height)
    {
        var index = new int[height, width];

        // 전체를 -1(방 없음)으로 초기화
        for (int r = 0; r < height; r++)
            for (int c = 0; c < width; c++)
                index[r, c] = -1;

        // 각 방의 영역에 해당 방의 인덱스 기록
        for (int i = 0; i < rooms.Length; i++)
        {
            var room = rooms[i];
            for (int r = room.Y; r < room.Bottom; r++)
                for (int c = room.X; c < room.Right; c++)
                    index[r, c] = i;
        }

        return index;
    }

    // ── 유틸 ───────────────────────────────────────────────────────
    public bool InBounds(int col, int row)
        => col >= 0 && col < MapWidth && row >= 0 && row < MapHeight;
}