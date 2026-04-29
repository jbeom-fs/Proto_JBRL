// ═══════════════════════════════════════════════════════════════════
//  DungeonQueryService.cs
//  Domain Layer — 던전 데이터 조회 전담
//
//  책임:
//    • DungeonData / RoomRegistry / DungeonTilemapRenderer를 참조해
//      읽기 전용 쿼리를 수행합니다.
//    • 데이터를 생성하거나 변경하지 않습니다.
//    • DungeonManager.Generate() / GenerateChunked 완료 직후
//      UpdateData()를 호출해 최신 데이터를 주입받습니다.
//
//  사용법:
//    DungeonManager가 생성 시 소유하며, public 쿼리 메서드를 위임받습니다.
//    외부 코드는 DungeonManager.Instance 를 통해 간접 사용합니다.
// ═══════════════════════════════════════════════════════════════════

using UnityEngine;

public class DungeonQueryService
{
    private DungeonData           _data;
    private RoomRegistry          _registry;
    private int[,]                _originGrid;
    private DungeonTilemapRenderer _renderer;

    // ── 생성 ─────────────────────────────────────────────────────────

    public DungeonQueryService(DungeonTilemapRenderer renderer)
    {
        _renderer = renderer;
    }

    // ── 데이터 갱신 (Generate 완료 직후 DungeonManager가 호출) ────────

    /// <summary>
    /// 새 던전 생성 완료 후 호출합니다.
    /// Registry.Initialize()가 끝난 뒤에 호출해야 타입 정보가 반영됩니다.
    /// </summary>
    public void UpdateData(DungeonData data, RoomRegistry registry, int[,] originGrid)
    {
        _data       = data;
        _registry   = registry;
        _originGrid = originGrid;
    }

    // ── 타일 / 보행 가능 여부 ─────────────────────────────────────────

    /// <summary>해당 그리드 좌표가 보행 가능한지 반환합니다.</summary>
    public bool IsWalkable(int col, int row)
        => _data?.IsWalkable(col, row) ?? false;

    /// <summary>해당 그리드 좌표의 타일 타입을 반환합니다.</summary>
    public int GetTileType(int col, int row)
        => _data?.GetTileType(col, row) ?? DungeonGenerator.EMPTY;

    /// <summary>그리드 좌표가 복도인지 반환합니다.</summary>
    public bool IsCorr(int x, int y)
    {
        if (_originGrid == null) return false;
        if (x < 0 || y < 0 || y >= _originGrid.GetLength(0) || x >= _originGrid.GetLength(1))
            return false;
        return _originGrid[y, x] == DungeonGenerator.CORRIDOR;
    }

    // ── 방 쿼리 ──────────────────────────────────────────────────────

    /// <summary>그리드 좌표가 속한 방을 타입 정보 포함해 반환합니다.</summary>
    public RoomInfo? GetRoomAt(int col, int row)
    {
        if (_data == null || _registry == null) return null;
        var room = _data.GetRoomAt(col, row);
        if (!room.HasValue) return null;
        return _registry.Resolve(room.Value);
    }

    /// <summary>해당 타입의 계단 위치를 그리드 좌표로 반환합니다. 없으면 (-1,-1).</summary>
    public Vector2Int FindStairPos(int stairType)
    {
        if (_data == null) return new Vector2Int(-1, -1);
        for (int row = 0; row < _data.MapHeight; row++)
            for (int col = 0; col < _data.MapWidth; col++)
                if (_data.GetTileType(col, row) == stairType)
                    return new Vector2Int(col, row);
        return new Vector2Int(-1, -1);
    }

    // ── 좌표 변환 (Renderer 위임) ─────────────────────────────────────

    /// <summary>그리드 좌표를 월드 좌표로 변환합니다.</summary>
    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        if (_renderer == null)
        {
            Debug.LogError("[DungeonQueryService] DungeonTilemapRenderer가 없습니다 — GridToWorld 실패");
            return Vector3.zero;
        }
        return _renderer.GridToWorld(gridPos);
    }

    /// <summary>월드 좌표를 그리드 좌표로 변환합니다.</summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        if (_renderer == null)
        {
            Debug.LogError("[DungeonQueryService] DungeonTilemapRenderer가 없습니다 — WorldToGrid 실패");
            return Vector2Int.zero;
        }
        return _renderer.WorldToGrid(worldPos);
    }
}
