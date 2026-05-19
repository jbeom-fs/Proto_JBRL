using UnityEngine;

public sealed class LocationRoot : MonoBehaviour
{
    [SerializeField] private string locationRootId;

    public string LocationRootId => locationRootId;

    private void OnEnable()
    {
        LocationRootRegistry.Register(this);
    }

    private void OnDisable()
    {
        LocationRootRegistry.Unregister(this);
    }
}
