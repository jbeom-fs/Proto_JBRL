// ═══════════════════════════════════════════════════════════════════
//  DungeonEventChannel.cs
//  Infrastructure Layer — ScriptableObject 이벤트 버스
//
//  책임:
//    • 이벤트 선언 및 발행
//    • 발행자(DungeonManager)와 구독자(PlayerController, DoorController 등)를
//      서로 모르게 연결
//
//  사용법:
//    1. Project 뷰 우클릭 → Create → Dungeon → Event Channel
//    2. 생성된 Asset을 Inspector에서 각 컴포넌트 슬롯에 드래그
//    3. 구독: channel.OnRoomEntered += Handler;
//    4. 발행: channel.Raise(roomInfo, isFirstVisit);
//
//  확장:
//    새 이벤트가 필요하면 이 파일에만 추가하면 됩니다.
//    발행자·구독자 코드는 수정 불필요.
// ═══════════════════════════════════════════════════════════════════

using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Dungeon/Event Channel", fileName = "DungeonEventChannel")]
public class DungeonEventChannel : ScriptableObject
{
    // ── 방 진입 이벤트 ───────────────────────────────────────────────

    /// <summary>모든 방 타입 진입 시 발행됩니다.</summary>
    public event Action<RoomEnteredEventArgs> OnRoomEntered;

    /// <summary>Normal 방에 진입할 때 발행됩니다.</summary>
    public event Action<RoomEnteredEventArgs> OnNormalRoomEntered;

    /// <summary>Spawn 방에 진입할 때 발행됩니다.</summary>
    public event Action<RoomEnteredEventArgs> OnSpawnRoomEntered;

    /// <summary>Stair 방에 진입할 때 발행됩니다.</summary>
    public event Action<RoomEnteredEventArgs> OnStairRoomEntered;

    // ── 층 변경 이벤트 ───────────────────────────────────────────────

    /// <summary>층이 변경될 때 발행됩니다. (이전 층, 새 층)</summary>
    public event Action<int, int> OnFloorChanged;

    // ── 발행 메서드 ──────────────────────────────────────────────────

    /// <summary>방 진입 이벤트를 발행합니다.</summary>
    public void RaiseRoomEntered(RoomInfo room, bool isFirstVisit)
    {
        // struct이므로 Heap 할당 없음
        var args = new RoomEnteredEventArgs(room, isFirstVisit);

        if (OnRoomEntered != null)
        {
#if UNITY_EDITOR
            Debug.Log($"[Event] OnRoomEntered — Type: {room.Type}  FirstVisit: {isFirstVisit}");
#endif
            OnRoomEntered.Invoke(args);
        }

        switch (room.Type)
        {
            case RoomType.Normal:
                if (OnNormalRoomEntered != null)
                {
                    OnNormalRoomEntered.Invoke(args);
                }
                break;

            case RoomType.Spawn:
                if (OnSpawnRoomEntered != null)
                {
#if UNITY_EDITOR
                    Debug.Log($"[Event] OnSpawnRoomEntered — FirstVisit: {isFirstVisit}");
#endif
                    OnSpawnRoomEntered.Invoke(args);
                }
                break;

            case RoomType.Stair:
                if (OnStairRoomEntered != null)
                {
#if UNITY_EDITOR
                    Debug.Log($"[Event] OnStairRoomEntered — FirstVisit: {isFirstVisit}");
#endif
                    OnStairRoomEntered.Invoke(args);
                }
                break;
        }
    }

    /// <summary>층 변경 이벤트를 발행합니다.</summary>
    public void RaiseFloorChanged(int prevFloor, int newFloor)
    {
#if UNITY_EDITOR
        Debug.Log($"[Event] OnFloorChanged — {prevFloor}F → {newFloor}F");
#endif
        OnFloorChanged?.Invoke(prevFloor, newFloor);
    }

    // ── ScriptableObject 생명주기 ────────────────────────────────────

    private void OnDisable()
    {
        OnRoomEntered        = null;
        OnNormalRoomEntered  = null;
        OnSpawnRoomEntered   = null;
        OnStairRoomEntered   = null;
        OnFloorChanged       = null;
    }
}