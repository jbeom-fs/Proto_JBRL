using UnityEngine;

public class TeleportDestinationPoint : MonoBehaviour
{
    [SerializeField] private string destinationId;

    public string DestinationId => destinationId;

    private void OnEnable()
    {
        TeleportDestinationRegistry.Register(this);
    }

    private void OnDisable()
    {
        TeleportDestinationRegistry.Unregister(this);
    }
}
