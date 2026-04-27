// ═══════════════════════════════════════════════════════════════════
//  DoorController.cs
//  Application Layer — 문 개폐 전담
// ═══════════════════════════════════════════════════════════════════

using UnityEngine;

public class DoorController : MonoBehaviour
{
    [Header("Dependencies")]
    public DungeonEventChannel    eventChannel;
    public DungeonTilemapRenderer dungeonRenderer;

    private void OnEnable()
    {
        if (eventChannel != null)
            eventChannel.OnNormalRoomEntered += OnNormalRoomEntered;
    }

    private void OnDisable()
    {
        if (eventChannel != null)
            eventChannel.OnNormalRoomEntered -= OnNormalRoomEntered;
    }

    public void OpenAllDoors()
    {
        dungeonRenderer?.OpenAllDoors();
#if UNITY_EDITOR
        Debug.Log("[DoorController] 모든 문 열림");
#endif
    }

    // Action<RoomEnteredEventArgs> 시그니처 (struct 기반)
    private void OnNormalRoomEntered(RoomEnteredEventArgs args)
    {
        if (!args.IsFirstVisit) return;
        dungeonRenderer?.CloseDoorsForRoom(args.Room);
    }
}