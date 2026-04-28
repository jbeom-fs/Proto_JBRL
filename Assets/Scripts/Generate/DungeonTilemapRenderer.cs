// ═══════════════════════════════════════════════════════════════════
//  DungeonTilemapRenderer.cs
//  Presentation Layer — Tilemap 시각화 전담
//
//  문 레이어 구조:
//    [Layer 0] tilemap (메인)   — 바닥/통로/계단/벽 항상 표시
//    [Layer 1] doorTilemap (상위) — 닫힐 때만 문 타일 배치, 열리면 제거
//
//  성능 설계:
//    SetTiles(TileChangeData[], ignoreLockFlags: true) 를 배치 호출로 사용
//    → N번 SetColor 개별 호출 대신 1번 배치 호출 → interop N→1
//    → CloseDoorsForRoom 람다 제거 → delegate heap 할당 없음 → GC 간헐적 드랍 제거
// ═══════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Tilemaps;

public class DungeonTilemapRenderer : MonoBehaviour
{
    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("Tilemap — 메인 레이어 (바닥/통로/계단)")]
    public Tilemap tilemap;

    [Header("Tilemap — 문 레이어 (메인 위에 배치)")]
    [Tooltip("Grid 하위에 Tilemap을 하나 더 만들어 연결하세요.")]
    public Tilemap doorTilemap;

    [Header("Tiles")]
    [Tooltip("방 바닥 (ROOM = 1)")]
    public TileBase floorTile;

    [Tooltip("통로 (CORRIDOR = 2). null 이면 floorTile 사용.")]
    public TileBase corridorTile;

    [Tooltip("올라가는 계단 (STAIR_UP = 3). null 이면 floorTile 사용.")]
    public TileBase stairUpTile;

    [Tooltip("닫힌 문 타일. doorTilemap에 사전 배치 후 색상으로 on/off됩니다.")]
    public TileBase doorTile;

    [Tooltip("벽 / 빈 공간 (EMPTY = 0). null 이면 빈 칸.")]
    public TileBase wallTile;

    // ── 상수 ────────────────────────────────────────────────────────
    private static readonly Color OPAQUE      = Color.white;

    // ── 캐시 ────────────────────────────────────────────────────────
    private DungeonData     _data;
    private TilemapRenderer _doorTilemapRenderer;

    private readonly Dictionary<Vector3Int, Vector2Int> _doorPositions
        = new Dictionary<Vector3Int, Vector2Int>();

    private readonly HashSet<Vector3Int> _closedDoorPositions
        = new HashSet<Vector3Int>();

    private readonly List<Vector3Int> _renderedDoorPositions
        = new List<Vector3Int>(16);

    // SetTiles 배치 호출에 재사용할 버퍼 — 매 호출마다 List/Array 할당 방지
    private readonly List<TileChangeData> _doorChangeBuffer
        = new List<TileChangeData>(32);

    private readonly Dictionary<int, TileChangeData[]> _doorChangeArraysBySize
        = new Dictionary<int, TileChangeData[]>();

    private TileBase[] _mainTileBuffer;
    private TileBase[] _chunkTileBuffer;

    // ── 공개 API ─────────────────────────────────────────────────────

    public void PlaceTiles(DungeonData data)
    {
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("place_tiles_begin",
                "size=" + data.MapWidth + "x" + data.MapHeight + " rooms=" + data.RoomCount);

        double stageStart = Time.realtimeSinceStartupAsDouble;
        _data = data;
        _doorPositions.Clear();
        _closedDoorPositions.Clear();
        _renderedDoorPositions.Clear();

        if (doorTilemap != null)
        {
            if (_doorTilemapRenderer == null)
                _doorTilemapRenderer = doorTilemap.GetComponent<TilemapRenderer>();
            EnsureDoorTilemapActive();
            SyncDoorTilemapPresentation();
            _doorTilemapRenderer.enabled = false;
        }

        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("place_tiles_stage_prepare",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " hasDoorTilemap=" + (doorTilemap != null));

        stageStart = Time.realtimeSinceStartupAsDouble;
        tilemap.ClearAllTiles();
        if (doorTilemap != null) doorTilemap.ClearAllTiles();
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("place_tiles_stage_clear",
                "elapsedMs=" + ElapsedMs(stageStart));

        // ── 1. 메인 타일 일괄 배치 ────────────────────────────────
        stageStart = Time.realtimeSinceStartupAsDouble;
        int total = data.MapWidth * data.MapHeight;
        var tiles = GetMainTileBuffer(total);
        int visibleTileCount = 0;

        for (int row = 0; row < data.MapHeight; row++)
        {
            int yOffset = data.MapHeight - 1 - row;
            int dstRow = yOffset * data.MapWidth;

            for (int col = 0; col < data.MapWidth; col++)
            {
                TileBase tile = ResolveTile(data.GetTileTypeUnchecked(col, row));
                if (tile != null) visibleTileCount++;
                tiles[col + dstRow] = tile;
            }
        }

        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("place_tiles_stage_build_changes",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " total=" + total +
                " visible=" + visibleTileCount);

        stageStart = Time.realtimeSinceStartupAsDouble;
        var bounds = new BoundsInt(0, 1 - data.MapHeight, 0, data.MapWidth, data.MapHeight, 1);
        tilemap.SetTilesBlock(bounds, tiles);
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("place_tiles_stage_set_tiles",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " tileCount=" + tiles.Length +
                " method=SetTilesBlock");

        stageStart = Time.realtimeSinceStartupAsDouble;
        CacheDoorPositions(data);
        if (RuntimePerfLogger.IsActive)
        {
            RuntimePerfLogger.MarkEvent("place_tiles_stage_cache_doors",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " doorCandidates=" + _doorPositions.Count);
            RuntimePerfLogger.MarkEvent("place_tiles_end", "doorCandidates=" + _doorPositions.Count);
        }
    }

    public IEnumerator PlaceTilesChunked(DungeonData data, int chunkRows)
    {
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("place_tiles_begin",
                "size=" + data.MapWidth + "x" + data.MapHeight +
                " rooms=" + data.RoomCount +
                " chunkRows=" + chunkRows);

        double stageStart = Time.realtimeSinceStartupAsDouble;
        _data = data;
        _doorPositions.Clear();
        _closedDoorPositions.Clear();
        _renderedDoorPositions.Clear();

        if (doorTilemap != null)
        {
            if (_doorTilemapRenderer == null)
                _doorTilemapRenderer = doorTilemap.GetComponent<TilemapRenderer>();
            EnsureDoorTilemapActive();
            SyncDoorTilemapPresentation();
            _doorTilemapRenderer.enabled = false;
        }

        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("place_tiles_stage_prepare",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " hasDoorTilemap=" + (doorTilemap != null));

        stageStart = Time.realtimeSinceStartupAsDouble;
        tilemap.ClearAllTiles();
        if (doorTilemap != null) doorTilemap.ClearAllTiles();
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("place_tiles_stage_clear",
                "elapsedMs=" + ElapsedMs(stageStart));

        chunkRows = Mathf.Clamp(chunkRows, 1, data.MapHeight);

        int total = data.MapWidth * data.MapHeight;
        int visibleTileCount = 0;
        double totalSetTilesMs = 0.0;
        int chunkIndex = 0;

        for (int rowStart = 0; rowStart < data.MapHeight; rowStart += chunkRows)
        {
            int rowEnd = Mathf.Min(rowStart + chunkRows, data.MapHeight);
            int currentChunkRows = rowEnd - rowStart;
            var tiles = GetChunkTileBuffer(data.MapWidth * currentChunkRows);

            stageStart = Time.realtimeSinceStartupAsDouble;
            int chunkVisible = 0;
            for (int row = rowStart; row < rowEnd; row++)
            {
                int localY = rowEnd - 1 - row;
                int dstRow = localY * data.MapWidth;

                for (int col = 0; col < data.MapWidth; col++)
                {
                    TileBase tile = ResolveTile(data.GetTileTypeUnchecked(col, row));
                    if (tile != null) chunkVisible++;
                    tiles[col + dstRow] = tile;
                }
            }
            visibleTileCount += chunkVisible;

            if (RuntimePerfLogger.IsActive)
                RuntimePerfLogger.MarkEvent("place_tiles_stage_build_chunk",
                    "index=" + chunkIndex +
                    " rows=" + rowStart + ":" + rowEnd +
                    " elapsedMs=" + ElapsedMs(stageStart) +
                    " visible=" + chunkVisible);

            stageStart = Time.realtimeSinceStartupAsDouble;
            var bounds = new BoundsInt(0, 1 - rowEnd, 0, data.MapWidth, currentChunkRows, 1);
            tilemap.SetTilesBlock(bounds, tiles);
            double setTilesMs = (Time.realtimeSinceStartupAsDouble - stageStart) * 1000.0;
            totalSetTilesMs += setTilesMs;

            if (RuntimePerfLogger.IsActive)
                RuntimePerfLogger.MarkEvent("place_tiles_stage_set_tiles_chunk",
                    "index=" + chunkIndex +
                    " rows=" + rowStart + ":" + rowEnd +
                    " elapsedMs=" + setTilesMs.ToString("F3", CultureInfo.InvariantCulture) +
                    " tileCount=" + tiles.Length);

            chunkIndex++;
            yield return null;
        }

        if (RuntimePerfLogger.IsActive)
        {
            RuntimePerfLogger.MarkEvent("place_tiles_stage_build_changes",
                "elapsedMs=chunked total=" + total +
                " visible=" + visibleTileCount +
                " chunks=" + chunkIndex);
            RuntimePerfLogger.MarkEvent("place_tiles_stage_set_tiles",
                "elapsedMs=" + totalSetTilesMs.ToString("F3", CultureInfo.InvariantCulture) +
                " tileCount=" + total +
                " method=SetTilesBlockChunked" +
                " chunks=" + chunkIndex);
        }

        stageStart = Time.realtimeSinceStartupAsDouble;
        CacheDoorPositions(data);
        if (RuntimePerfLogger.IsActive)
        {
            RuntimePerfLogger.MarkEvent("place_tiles_stage_cache_doors",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " doorCandidates=" + _doorPositions.Count);
            RuntimePerfLogger.MarkEvent("place_tiles_end", "doorCandidates=" + _doorPositions.Count);
        }
    }

    /// <summary>
    /// 방의 문을 닫습니다.
    /// 람다 없이 루프를 직접 순회 — delegate 할당 없음.
    /// 변경 사항을 배치로 모아 SetTiles 1회 호출 — interop N→1.
    /// </summary>
    public void CloseDoorsForRoom(RoomInfo room)
    {
        if (_data == null || doorTilemap == null || doorTile == null) return;

        double start = Time.realtimeSinceStartupAsDouble;

        _doorChangeBuffer.Clear();

        for (int i = 0; i < _renderedDoorPositions.Count; i++)
        {
            _doorChangeBuffer.Add(new TileChangeData
            {
                position  = _renderedDoorPositions[i],
                tile      = null,
                color     = OPAQUE,
                transform = Matrix4x4.identity,
            });
        }

        _renderedDoorPositions.Clear();
        _closedDoorPositions.Clear();

        // ── 4방향 테두리 직접 순회 (람다/delegate 할당 없음) ────
        for (int col = room.X; col < room.Right; col++)
        {
            TryAddDoorClose(col, room.Y - 1);
            TryAddDoorClose(col, room.Bottom);
        }
        for (int row = room.Y; row < room.Bottom; row++)
        {
            TryAddDoorClose(room.X - 1, row);
            TryAddDoorClose(room.Right,  row);
        }

        FlushDoorChanges();
        SetDoorVisible(_renderedDoorPositions.Count > 0);

        double elapsedMs = (Time.realtimeSinceStartupAsDouble - start) * 1000.0;
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("close_doors",
                "room=" + room.X + ":" + room.Y +
                " closed=" + _closedDoorPositions.Count +
                " rendered=" + _renderedDoorPositions.Count +
                " elapsedMs=" + elapsedMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 모든 닫힌 문을 엽니다.
    /// N번 SetColor 개별 호출 대신 SetTiles 1회 배치 호출.
    /// </summary>
    public bool OpenAllDoors()
    {
        if (_data == null || doorTilemap == null || _closedDoorPositions.Count == 0)
            return false;

        double start = Time.realtimeSinceStartupAsDouble;
        int openedCount = _closedDoorPositions.Count;

        foreach (var tilemapPos in _closedDoorPositions)
        {
            var gridPos = _doorPositions[tilemapPos];
            _data.SetTileValue(gridPos.x, gridPos.y, DungeonGenerator.CORRIDOR);
        }

        _closedDoorPositions.Clear();
        SetDoorVisible(false);

        double elapsedMs = (Time.realtimeSinceStartupAsDouble - start) * 1000.0;
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("open_all_doors",
                "opened=" + openedCount +
                " rendered=" + _renderedDoorPositions.Count +
                " visible=false" +
                " elapsedMs=" + elapsedMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));

        return true;
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

    private void CacheDoorPositions(DungeonData data)
    {
        _doorPositions.Clear();

        for (int i = 0; i < data.RoomCount; i++)
        {
            var room = data.GetRoom(i);

            for (int col = room.X; col < room.Right; col++)
            {
                TryCacheDoorPosition(data, col, room.Y - 1);
                TryCacheDoorPosition(data, col, room.Bottom);
            }
            for (int row = room.Y; row < room.Bottom; row++)
            {
                TryCacheDoorPosition(data, room.X - 1, row);
                TryCacheDoorPosition(data, room.Right,  row);
            }
        }
    }

    private void TryCacheDoorPosition(DungeonData data, int col, int row)
    {
        if (!data.InBounds(col, row)) return;
        if (data.GetTileType(col, row) != DungeonGenerator.CORRIDOR) return;

        var tilemapPos = new Vector3Int(col, -row, 0);
        if (_doorPositions.ContainsKey(tilemapPos)) return;

        _doorPositions[tilemapPos] = new Vector2Int(col, row);
    }

    private void TryAddDoorClose(int col, int row)
    {
        if (_data.GetTileType(col, row) != DungeonGenerator.CORRIDOR) return;

        var tilemapPos = new Vector3Int(col, -row, 0);
        if (!_doorPositions.ContainsKey(tilemapPos)) return;
        if (_closedDoorPositions.Contains(tilemapPos)) return;

        _data.SetTileValue(col, row, DungeonGenerator.DOOR_CLOSED);
        _closedDoorPositions.Add(tilemapPos);
        _renderedDoorPositions.Add(tilemapPos);
        _doorChangeBuffer.Add(new TileChangeData
        {
            position  = tilemapPos,
            tile      = doorTile,
            color     = OPAQUE,
            transform = Matrix4x4.identity,
        });
    }

    private void EnsureDoorTilemapActive()
    {
        if (!doorTilemap.gameObject.activeInHierarchy)
            doorTilemap.gameObject.SetActive(true);

        if (_doorTilemapRenderer == null)
            _doorTilemapRenderer = doorTilemap.GetComponent<TilemapRenderer>();

        if (_doorTilemapRenderer != null && !_doorTilemapRenderer.enabled)
            _doorTilemapRenderer.enabled = true;

        if (_doorTilemapRenderer != null && tilemap != null)
        {
            var mainRenderer = tilemap.GetComponent<TilemapRenderer>();
            if (mainRenderer != null)
            {
                if (_doorTilemapRenderer.sortingLayerID != mainRenderer.sortingLayerID)
                    _doorTilemapRenderer.sortingLayerID = mainRenderer.sortingLayerID;
                if (_doorTilemapRenderer.sortingOrder <= mainRenderer.sortingOrder)
                    _doorTilemapRenderer.sortingOrder = mainRenderer.sortingOrder + 1;

                if (_doorTilemapRenderer.sharedMaterial != mainRenderer.sharedMaterial)
                    _doorTilemapRenderer.sharedMaterial = mainRenderer.sharedMaterial;

                if (_doorTilemapRenderer.maskInteraction != mainRenderer.maskInteraction)
                    _doorTilemapRenderer.maskInteraction = mainRenderer.maskInteraction;
            }
        }

        if (doorTilemap.color != OPAQUE)
            doorTilemap.color = OPAQUE;
    }

    private void SetDoorVisible(bool visible)
    {
        if (doorTilemap == null) return;

        EnsureDoorTilemapActive();

        if (_doorTilemapRenderer == null)
            _doorTilemapRenderer = doorTilemap.GetComponent<TilemapRenderer>();

        if (_doorTilemapRenderer != null)
            _doorTilemapRenderer.enabled = visible;
    }

    private void SyncDoorTilemapPresentation()
    {
        if (doorTilemap == null || tilemap == null) return;

        if (doorTilemap.tileAnchor != tilemap.tileAnchor)
            doorTilemap.tileAnchor = tilemap.tileAnchor;

        if (doorTilemap.orientation != tilemap.orientation)
            doorTilemap.orientation = tilemap.orientation;

        if (doorTilemap.orientationMatrix != tilemap.orientationMatrix)
            doorTilemap.orientationMatrix = tilemap.orientationMatrix;
    }

    private void FlushDoorChanges()
    {
        int count = _doorChangeBuffer.Count;
        if (count == 0 || doorTilemap == null) return;

        EnsureDoorTilemapActive();
        SyncDoorTilemapPresentation();
        var changes = GetDoorChangeArray(count);

        for (int i = 0; i < count; i++)
            changes[i] = _doorChangeBuffer[i];

        doorTilemap.SetTiles(changes, true);
    }

    private TileChangeData[] GetDoorChangeArray(int count)
    {
        if (!_doorChangeArraysBySize.TryGetValue(count, out var changes))
        {
            changes = new TileChangeData[count];
            _doorChangeArraysBySize.Add(count, changes);
        }

        return changes;
    }

    private TileBase[] GetMainTileBuffer(int count)
    {
        if (_mainTileBuffer == null || _mainTileBuffer.Length != count)
            _mainTileBuffer = new TileBase[count];

        return _mainTileBuffer;
    }

    private TileBase[] GetChunkTileBuffer(int count)
    {
        if (_chunkTileBuffer == null || _chunkTileBuffer.Length != count)
            _chunkTileBuffer = new TileBase[count];

        return _chunkTileBuffer;
    }

    private static string ElapsedMs(double startTime)
    {
        return ((Time.realtimeSinceStartupAsDouble - startTime) * 1000.0)
            .ToString("F3", CultureInfo.InvariantCulture);
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
