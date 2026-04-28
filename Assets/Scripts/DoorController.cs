using UnityEngine;

public class DoorController : MonoBehaviour
{
    [Header("Dependencies")]
    public DungeonEventChannel eventChannel;
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
    }

    private void OnNormalRoomEntered(RoomEnteredEventArgs args)
    {
        if (!args.IsFirstVisit) return;
        dungeonRenderer?.CloseDoorsForRoom(args.Room);
    }
}
