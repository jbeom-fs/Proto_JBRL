using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// LocationRoot 단위로 미니맵에 그릴 Tilemap 집합을 등록하는 컴포넌트.
///
/// 두 가지 모드를 지원합니다:
///   1. 명시적 모드 — groundTilemap / wallTilemap / doorTilemap을 Inspector에서 직접 연결.
///      (기존 Town 구성과 동일한 backward-compat 경로)
///   2. 자동 발견 모드 — autoDiscoverChildren=true일 때 OnEnable 시점에 자식 Tilemap을
///      한 번만 스캔하여 gameObject.layer로 walkable / wall / door로 분류합니다.
///      Elite Arena, Boss Arena 등 추후 추가될 LocationRoot에 동일 구조로 사용 가능합니다.
/// </summary>
public class TilemapMinimapSource : MonoBehaviour
{
    private const string DefaultWalkableLayerName = "Walkable";
    private const string DefaultWallLayerName = "Wall";
    private const string DefaultDoorLayerName = "Door";

    [SerializeField] private string locationId;

    [Header("Explicit Tilemaps (optional — backward compat)")]
    [SerializeField] private Tilemap groundTilemap;
    [SerializeField] private Tilemap wallTilemap;
    [SerializeField] private Tilemap doorTilemap;

    [Header("Auto Discovery")]
    [Tooltip("OnEnable 시 자식 Tilemap을 GameObject Layer 기준으로 한 번 스캔해 분류합니다.")]
    [SerializeField] private bool autoDiscoverChildren = false;

    [SerializeField] private string walkableLayerName = DefaultWalkableLayerName;
    [SerializeField] private string wallLayerName = DefaultWallLayerName;
    [SerializeField] private string doorLayerName = DefaultDoorLayerName;

    [Header("Colors")]
    [SerializeField] private Color groundColor = new Color(0.75f, 0.82f, 0.95f, 0.92f);
    [SerializeField] private Color wallColor = new Color(0.35f, 0.38f, 0.45f, 0.95f);
    [SerializeField] private Color doorColor = new Color(0.95f, 0.78f, 0.36f, 0.95f);

    private readonly List<Tilemap> _walkableTilemaps = new List<Tilemap>(4);
    private readonly List<Tilemap> _wallTilemaps = new List<Tilemap>(4);
    private readonly List<Tilemap> _doorTilemaps = new List<Tilemap>(4);
    private bool _classified;

    public string LocationId => locationId;

    public IReadOnlyList<Tilemap> WalkableTilemaps => _walkableTilemaps;
    public IReadOnlyList<Tilemap> WallTilemaps => _wallTilemaps;
    public IReadOnlyList<Tilemap> DoorTilemaps => _doorTilemaps;

    public Color GroundColor => groundColor;
    public Color WallColor => wallColor;
    public Color DoorColor => doorColor;

    /// <summary>backward-compat: 첫 번째 walkable tilemap (없으면 null).</summary>
    public Tilemap GroundTilemap => _walkableTilemaps.Count > 0 ? _walkableTilemaps[0] : null;
    /// <summary>backward-compat: 첫 번째 wall tilemap (없으면 null).</summary>
    public Tilemap WallTilemap => _wallTilemaps.Count > 0 ? _wallTilemaps[0] : null;

    /// <summary>Player marker 좌표 변환의 기준 Tilemap. walk → wall → door 순으로 fallback.</summary>
    public Tilemap ReferenceTilemap
    {
        get
        {
            if (_walkableTilemaps.Count > 0) return _walkableTilemaps[0];
            if (_wallTilemaps.Count > 0) return _wallTilemaps[0];
            if (_doorTilemaps.Count > 0) return _doorTilemaps[0];
            return null;
        }
    }

    public bool IsReady =>
        _walkableTilemaps.Count > 0 || _wallTilemaps.Count > 0 || _doorTilemaps.Count > 0;

    private void OnEnable()
    {
        EnsureClassified();
        LocationMinimapRegistry.Register(this);
    }

    private void OnDisable()
    {
        LocationMinimapRegistry.Unregister(this);
    }

    private void EnsureClassified()
    {
        if (_classified)
            return;

        _walkableTilemaps.Clear();
        _wallTilemaps.Clear();
        _doorTilemaps.Clear();

        if (groundTilemap != null)
            _walkableTilemaps.Add(groundTilemap);
        if (wallTilemap != null)
            _wallTilemaps.Add(wallTilemap);
        if (doorTilemap != null)
            _doorTilemaps.Add(doorTilemap);

        if (autoDiscoverChildren)
            DiscoverChildTilemapsByLayer();

        _classified = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_walkableTilemaps.Count == 0 && _wallTilemaps.Count == 0 && _doorTilemaps.Count == 0)
        {
            Debug.LogWarning(
                "[TilemapMinimapSource] '" + locationId + "' classified 0 Tilemaps. " +
                "Wire groundTilemap/wallTilemap/doorTilemap explicitly, or enable autoDiscoverChildren and set " +
                "children's GameObject Layer to '" + walkableLayerName + "' / '" + wallLayerName + "' / '" + doorLayerName + "'.",
                this);
        }
#endif
    }

    private void DiscoverChildTilemapsByLayer()
    {
        int walkableLayer = LayerMask.NameToLayer(string.IsNullOrEmpty(walkableLayerName) ? DefaultWalkableLayerName : walkableLayerName);
        int wallLayer = LayerMask.NameToLayer(string.IsNullOrEmpty(wallLayerName) ? DefaultWallLayerName : wallLayerName);
        int doorLayer = LayerMask.NameToLayer(string.IsNullOrEmpty(doorLayerName) ? DefaultDoorLayerName : doorLayerName);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (walkableLayer < 0 && wallLayer < 0 && doorLayer < 0)
            Debug.LogWarning(
                "[TilemapMinimapSource] '" + locationId + "' autoDiscoverChildren is enabled but no matching layer was resolved. " +
                "Check that '" + walkableLayerName + "', '" + wallLayerName + "', '" + doorLayerName + "' layers exist.",
                this);
#endif

        Tilemap[] candidates = GetComponentsInChildren<Tilemap>(true);
        for (int i = 0; i < candidates.Length; i++)
        {
            Tilemap candidate = candidates[i];
            if (candidate == null)
                continue;

            int layer = candidate.gameObject.layer;
            if (walkableLayer >= 0 && layer == walkableLayer)
            {
                AddUnique(_walkableTilemaps, candidate);
                continue;
            }
            if (wallLayer >= 0 && layer == wallLayer)
            {
                AddUnique(_wallTilemaps, candidate);
                continue;
            }
            if (doorLayer >= 0 && layer == doorLayer)
            {
                AddUnique(_doorTilemaps, candidate);
                continue;
            }
        }
    }

    private static void AddUnique(List<Tilemap> list, Tilemap tilemap)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], tilemap))
                return;
        }
        list.Add(tilemap);
    }
}
