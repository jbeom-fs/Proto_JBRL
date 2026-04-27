// ═══════════════════════════════════════════════════════════════════
//  DungeonTilemapRenderer.cs
//  Presentation Layer — Tilemap 시각화 전담
//
//  문 레이어 구조:
//    [Layer 0] tilemap (메인)   — 바닥/통로/계단/벽 항상 표시
//    [Layer 1] doorTilemap (상위) — 문 타일만, SetColor로 on/off
//
//  문 닫기: doorTilemap에서 SetColor(OPAQUE)  → 문이 보임
//  문 열기: doorTilemap에서 SetColor(TRANSPARENT) → 문이 안 보임
//           메인 tilemap의 통로 타일은 항상 유지 → 비어보이지 않음
// ═══════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class DungeonTilemapRenderer : MonoBehaviour
{
    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("Tilemap — 메인 레이어 (바닥/통로/계단)")]
    public Tilemap tilemap;

    [Header("Tilemap — 문 레이어 (메인 위에 배치)")]
    [Tooltip("Grid 하위에 Tilemap을 하나 더 만들어 연결하세요.\n" +
             "Order in Layer를 메인보다 높게 설정하세요.")]
    public Tilemap doorTilemap;

    [Header("Tiles")]
    [Tooltip("방 바닥 (ROOM = 1)")]
    public TileBase floorTile;

    [Tooltip("통로 (CORRIDOR = 2). null 이면 floorTile 사용.")]
    public TileBase corridorTile;

    [Tooltip("올라가는 계단 (STAIR_UP = 3). null 이면 floorTile 사용.")]
    public TileBase stairUpTile;

    [Tooltip("닫힌 문 타일. 문 레이어에 투명으로 미리 배치됩니다.")]
    public TileBase doorTile;

    [Tooltip("벽 / 빈 공간 (EMPTY = 0). null 이면 빈 칸.")]
    public TileBase wallTile;

    // ── 상수 ────────────────────────────────────────────────────────
    private static readonly Color TRANSPARENT = new Color(1f, 1f, 1f, 0f);
    private static readonly Color OPAQUE      = Color.white;

    // ── 캐시 ────────────────────────────────────────────────────────
    private DungeonData _data;

    /// <summary>
    /// 문 후보 위치 — key: Tilemap 좌표, value: 그리드 좌표
    /// 생성 시 계산, 런타임엔 SetColor만 호출
    /// </summary>
    private readonly Dictionary<Vector3Int, Vector2Int> _doorPositions
        = new Dictionary<Vector3Int, Vector2Int>();

    /// <summary>현재 닫혀 있는 문의 Tilemap 좌표만 추적합니다.</summary>
    private readonly HashSet<Vector3Int> _closedDoorPositions
        = new HashSet<Vector3Int>();

    // ── 공개 API ─────────────────────────────────────────────────────

    /// <summary>
    /// DungeonData 전체를 메인 Tilemap에 배치합니다.
    /// 이후 문 후보 위치를 doorTilemap에 투명으로 미리 배치합니다.
    /// 로딩 화면 중 호출되어야 합니다.
    /// </summary>
    public void PlaceTiles(DungeonData data)
    {
        _data = data;
        _doorPositions.Clear();
        _closedDoorPositions.Clear();

        tilemap.ClearAllTiles();
        if (doorTilemap != null) doorTilemap.ClearAllTiles();

        // ── 1. 메인 타일 일괄 배치 (문 포함 모든 walkable 타일) ────
        int total   = data.MapWidth * data.MapHeight;
        var changes = new TileChangeData[total];
        int idx     = 0;

        data.ForEachTile((col, row, tileType) =>
        {
            changes[idx++] = new TileChangeData
            {
                position  = new Vector3Int(col, -row, 0),
                tile      = ResolveTile(tileType),
                color     = OPAQUE,
                transform = Matrix4x4.identity,
            };
        });

        tilemap.SetTiles(changes, false);

        // ── 2. 문 레이어에 doorTile 투명으로 미리 배치 ─────────────
        if (doorTilemap != null && doorTile != null)
            PreplaceDoorTiles(data);
    }

    /// <summary>
    /// 방의 문을 닫습니다.
    /// doorTilemap에서 SetColor(OPAQUE) → 메시 리빌드 없음.
    /// 메인 Tilemap의 바닥 타일은 그대로 유지됩니다.
    /// </summary>
    public void CloseDoorsForRoom(RoomInfo room)
    {
        if (_data == null || doorTilemap == null) return;

        IterateRoomExterior(room, (col, row) =>
        {
            if (_data.GetTileType(col, row) != DungeonGenerator.CORRIDOR) return;

            var tilemapPos = new Vector3Int(col, -row, 0);
            if (!_doorPositions.ContainsKey(tilemapPos)) return;

            _data.SetTileValue(col, row, DungeonGenerator.DOOR_CLOSED);
            _closedDoorPositions.Add(tilemapPos);   // 닫힌 문 추적
            doorTilemap.SetColor(tilemapPos, OPAQUE);
        });
    }

    /// <summary>
    /// 모든 닫힌 문을 엽니다.
    /// _closedDoorPositions만 순회 — 전체 문 후보 탐색 없음.
    /// </summary>
    public void OpenAllDoors()
    {
        if (_data == null || doorTilemap == null || _closedDoorPositions.Count == 0)
            return;

        foreach (var tilemapPos in _closedDoorPositions)
        {
            var gridPos = _doorPositions[tilemapPos];
            _data.SetTileValue(gridPos.x, gridPos.y, DungeonGenerator.CORRIDOR);
            doorTilemap.SetColor(tilemapPos, TRANSPARENT);
        }

        _closedDoorPositions.Clear();
    }

    // ── 좌표 변환 ────────────────────────────────────────────────────

    public Vector3 GridToWorld(Vector2Int gridPos)
        => tilemap.GetCellCenterWorld(new Vector3Int(gridPos.x, -gridPos.y, 0));

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        var cell = tilemap.WorldToCell(worldPos);
        return new Vector2Int(cell.x, -cell.y);
    }

    // ── 내부 메서드 ──────────────────────────────────────────────────

    /// <summary>
    /// 모든 방의 문 후보 위치에 doorTile을 투명으로 사전 배치합니다.
    /// SetTiles는 로딩 화면 중 1회만 호출됩니다.
    /// </summary>
    private void PreplaceDoorTiles(DungeonData data)
    {
        var doorChanges = new List<TileChangeData>();

        data.ForEachRoom(room =>
        {
            IterateRoomExterior(room, (col, row) =>
            {
                if (!data.InBounds(col, row)) return;
                if (data.GetTileType(col, row) != DungeonGenerator.CORRIDOR) return;

                var tilemapPos = new Vector3Int(col, -row, 0);
                if (_doorPositions.ContainsKey(tilemapPos)) return;

                _doorPositions[tilemapPos] = new Vector2Int(col, row);
                doorChanges.Add(new TileChangeData
                {
                    position  = tilemapPos,
                    tile      = doorTile,
                    color     = TRANSPARENT,   // 처음엔 투명 (문 열림 상태)
                    transform = Matrix4x4.identity,
                });
            });
        });

        if (doorChanges.Count == 0) return;

        // doorTilemap에 일괄 배치 (메인 Tilemap 불변)
        doorTilemap.SetTiles(doorChanges.ToArray(), false);

        // 색상 오버라이드 허용 후 투명 재적용
        foreach (var kv in _doorPositions)
        {
            doorTilemap.SetTileFlags(kv.Key, TileFlags.None);
            doorTilemap.SetColor(kv.Key, TRANSPARENT);
        }
    }

    /// <summary>방 테두리 바깥 1칸(4방향)을 순회합니다.</summary>
    private static void IterateRoomExterior(RoomInfo room, System.Action<int, int> action)
    {
        for (int col = room.X; col < room.Right; col++)
        {
            action(col, room.Y - 1);
            action(col, room.Bottom);
        }
        for (int row = room.Y; row < room.Bottom; row++)
        {
            action(room.X - 1, row);
            action(room.Right,  row);
        }
    }

    private TileBase ResolveTile(int tileType)
    {
        switch (tileType)
        {
            case DungeonGenerator.ROOM:     return floorTile;
            case DungeonGenerator.CORRIDOR: return corridorTile != null ? corridorTile : floorTile;
            case DungeonGenerator.STAIR_UP: return stairUpTile  != null ? stairUpTile  : floorTile;
            default:                        return wallTile;
        }
    }
}