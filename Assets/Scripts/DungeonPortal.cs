using UnityEngine;

public class DungeonPortal : MonoBehaviour
{
    [SerializeField] private TeleportService teleportService;

    public void EnterDungeon()
    {
        PlayerController activePlayer = PlayerController.Active;
        if (activePlayer != null && teleportService != null)
            teleportService.RequestTeleport(activePlayer);
    }
}
