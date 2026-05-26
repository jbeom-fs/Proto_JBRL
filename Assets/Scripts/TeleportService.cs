using UnityEngine;

public class TeleportService : MonoBehaviour
{
    [SerializeField, TeleportDestinationId] private string targetDestinationId;
    [SerializeField] private LocationTransitionManager transitionManager;
    [SerializeField] private bool teleportOnPlayerTrigger = true;
    [SerializeField, Min(0f)] private float triggerCooldownSeconds = 0.5f;

    private float _lastTeleportTime = -999f;

    public void RequestTeleport(PlayerController player)
    {
        if (player == null || transitionManager == null || string.IsNullOrWhiteSpace(targetDestinationId))
            return;

        if (Time.time - _lastTeleportTime < triggerCooldownSeconds)
            return;

        _lastTeleportTime = Time.time;
        transitionManager.TeleportPlayer(player, targetDestinationId);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!teleportOnPlayerTrigger)
            return;

        if (other.TryGetComponent(out PlayerController player))
            RequestTeleport(player);
    }
}
