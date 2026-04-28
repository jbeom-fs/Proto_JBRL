// ═══════════════════════════════════════════════════════════════════
//  RoomRegistry.cs
//  Domain Layer — 방 상태 관리
//
//  책임:
//    • 각 방의 타입(Normal/Spawn/Stair) 보관 및 변경
//    • 문이 닫힌 방 추적 (중복 닫기 방지)
//    • 스폰 방 지정 및 조회
//    • Unity 의존 없음
// ═══════════════════════════════════════════════════════════════════

using System.Collections.Generic;

public class RoomRegistry
{
    // ── 방 타입 저장소 — key: (X, Y) 좌상단 좌표 ───────────────────
    private readonly Dictionary<(int x, int y), RoomType> _types
        = new Dictionary<(int x, int y), RoomType>();

    // ── 문이 닫힌 방 집합 ──────────────────────────────────────────
    private readonly HashSet<(int x, int y)> _closedRooms
        = new HashSet<(int x, int y)>();

    // ── 스폰 방 ─────────────────────────────────────────────────────
    private (int x, int y)? _spawnRoomKey = null;

    // ── 초기화 ─────────────────────────────────────────────────────

    /// <summary>
    /// DungeonData의 방 목록을 기반으로 레지스트리를 초기화합니다.
    /// Stair 방은 자동 감지, 나머지는 Normal로 설정합니다.
    /// </summary>
    public void Initialize(DungeonData data)
    {
        _types.Clear();
        _closedRooms.Clear();
        _spawnRoomKey = null;

        for (int i = 0; i < data.RoomCount; i++)
        {
            var room = data.GetRoom(i);
            var key  = Key(room);
            var type = RoomType.Normal;

            // STAIR_UP 포함 여부로 Stair 방 감지
            for (int row = room.Y; row < room.Bottom && type == RoomType.Normal; row++)
                for (int col = room.X; col < room.Right && type == RoomType.Normal; col++)
                    if (data.GetTileType(col, row) == DungeonGenerator.STAIR_UP)
                        type = RoomType.Stair;

            _types[key] = type;
        }
    }

    // ── 타입 조회·변경 ──────────────────────────────────────────────

    /// <summary>방의 현재 타입을 반환합니다.</summary>
    public RoomType GetRoomType(RoomInfo room)
    {
        _types.TryGetValue(Key(room), out var type);
        return type;
    }

    /// <summary>방의 타입을 변경합니다.</summary>
    public void SetRoomType(RoomInfo room, RoomType type)
    {
        _types[Key(room)] = type;
        if (type == RoomType.Spawn)
            _spawnRoomKey = Key(room);
    }

    /// <summary>방에 타입 정보를 적용한 RoomInfo를 반환합니다.</summary>
    public RoomInfo Resolve(RoomInfo room)
    {
        room.Type = GetRoomType(room);
        return room;
    }

    // ── 문 상태 ────────────────────────────────────────────────────

    /// <summary>해당 방의 문이 이미 닫혔는지 반환합니다.</summary>
    public bool IsRoomClosed(RoomInfo room) => _closedRooms.Contains(Key(room));

    /// <summary>방을 닫힌 상태로 표시합니다.</summary>
    public void MarkRoomClosed(RoomInfo room) => _closedRooms.Add(Key(room));

    /// <summary>모든 닫힌 방 기록을 초기화합니다.</summary>
    public void ClearClosedRooms() => _closedRooms.Clear();

    // ── 면제 여부 ───────────────────────────────────────────────────

    /// <summary>문 닫힘에서 면제되는 방인지 반환합니다 (Spawn / Stair).</summary>
    public bool IsExempt(RoomInfo room)
    {
        var type = GetRoomType(room);
        return type == RoomType.Spawn || type == RoomType.Stair;
    }

    // ── 내부 유틸 ──────────────────────────────────────────────────
    private static (int x, int y) Key(RoomInfo room) => (room.X, room.Y);
}
