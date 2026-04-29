using UnityEngine;

public class DoorController : MonoBehaviour
{
    [Header("Dependencies")]
    public DungeonEventChannel eventChannel;
    public DungeonTilemapRenderer dungeonRenderer;

    private void OnEnable()
    {
    }

    private void OnDisable()
    {
    }

    public void OpenAllDoors()
    {
        DungeonManager.Instance?.OpenCurrentRoomDoors();
    }
}
