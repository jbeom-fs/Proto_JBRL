// ═══════════════════════════════════════════════════════════════════
//  DungeonTypes.cs
//  던전 시스템 공유 타입 정의
//  여러 클래스에서 사용하는 enum/struct/class를 한 곳에서 관리합니다.
// ═══════════════════════════════════════════════════════════════════

using System;

// ── 방 타입 ──────────────────────────────────────────────────────────
/// <summary>던전 방의 종류입니다.</summary>
public enum RoomType
{
    /// <summary>일반 방. 첫 진입 시 문이 닫힙니다.</summary>
    Normal,

    /// <summary>
    /// 스폰 방. 층 진입 시 플레이어가 배치되는 방입니다.
    /// 문이 닫히지 않습니다.
    /// </summary>
    Spawn,

    /// <summary>
    /// 계단 방. STAIR_UP 타일을 포함한 방입니다.
    /// 문이 닫히지 않습니다.
    /// </summary>
    Stair,

    /// <summary>몬스터 밀도가 높은 전투 방입니다.</summary>
    MonsterDen,
}

// ── 방 정보 ──────────────────────────────────────────────────────────
/// <summary>방의 위치·크기와 타입을 담는 구조체입니다.</summary>
public struct RoomInfo
{
    public DungeonGenerator.RoomRect Rect;
    public RoomType                  Type;

    /// <summary>문 닫힘에서 면제되는지 여부 (Spawn/Stair는 면제).</summary>
    public bool IsExempt => Type == RoomType.Spawn || Type == RoomType.Stair;

    public int X      => Rect.X;
    public int Y      => Rect.Y;
    public int Right  => Rect.Right;
    public int Bottom => Rect.Bottom;
    public bool Contains(int col, int row) => Rect.Contains(col, row);
}

// ── 이벤트 인수 ───────────────────────────────────────────────────────
/// <summary>
/// 방 진입 이벤트에 전달되는 데이터입니다.
/// struct 사용 → Heap 할당 없음, GC 스파이크 방지
/// </summary>
public struct RoomEnteredEventArgs
{
    public RoomInfo Room       { get; }
    public bool IsFirstVisit   { get; }

    public RoomEnteredEventArgs(RoomInfo room, bool isFirstVisit)
    {
        Room         = room;
        IsFirstVisit = isFirstVisit;
    }
}
