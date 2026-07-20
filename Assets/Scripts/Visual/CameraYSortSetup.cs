using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class CameraYSortSetup : MonoBehaviour
{
    private void Awake()
    {
        Camera targetCamera = GetComponent<Camera>();
        targetCamera.transparencySortMode = TransparencySortMode.CustomAxis;
        targetCamera.transparencySortAxis = Vector3.up;
    }
}
