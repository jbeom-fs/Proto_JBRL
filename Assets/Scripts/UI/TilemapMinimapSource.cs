using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapMinimapSource : MonoBehaviour
{
    [SerializeField] private string locationId;
    [SerializeField] private Tilemap groundTilemap;
    [SerializeField] private Tilemap wallTilemap;

    [Header("Colors")]
    [SerializeField] private Color groundColor = new Color(0.75f, 0.82f, 0.95f, 0.92f);
    [SerializeField] private Color wallColor   = new Color(0.35f, 0.38f, 0.45f, 0.95f);

    public string  LocationId    => locationId;
    public Tilemap GroundTilemap => groundTilemap;
    public Tilemap WallTilemap   => wallTilemap;
    public Color   GroundColor   => groundColor;
    public Color   WallColor     => wallColor;

    public bool IsReady => groundTilemap != null || wallTilemap != null;

    private void OnEnable()  => LocationMinimapRegistry.Register(this);
    private void OnDisable() => LocationMinimapRegistry.Unregister(this);
}
