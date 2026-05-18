using UnityEngine;
using UnityEngine.UI;

public class MinimapController : MonoBehaviour
{
    private const int InitialInitializeMaxFrames = 60;

    [Header("Dependencies")]
    [SerializeField] private RawImage minimapImage;
    [SerializeField] private RectTransform playerMarker;
    [SerializeField] private Graphic playerMarkerGraphic;
    [SerializeField] private Transform player;
    [SerializeField] private DungeonManager dungeonManager;
    [SerializeField] private FogOfWarController fogOfWar;
    [SerializeField] private DungeonEventChannel eventChannel;

    [Header("Rendering")]
    [SerializeField, Min(1)] private int pixelsPerCell = 3;
    [SerializeField] private Color visibleRoomColor = new Color(0.75f, 0.82f, 0.95f, 0.92f);
    [SerializeField] private Color exploredRoomColor = new Color(0.26f, 0.31f, 0.42f, 0.78f);
    [SerializeField] private Color visibleCorridorColor = new Color(0.70f, 0.74f, 0.82f, 0.88f);
    [SerializeField] private Color exploredCorridorColor = new Color(0.18f, 0.21f, 0.28f, 0.72f);
    [SerializeField] private Color visibleDoorColor = new Color(0.95f, 0.78f, 0.36f, 0.95f);
    [SerializeField] private Color exploredDoorColor = new Color(0.46f, 0.36f, 0.18f, 0.78f);
    [SerializeField] private Color playerColor = new Color(0.2f, 1f, 0.65f, 1f);
    [SerializeField] private Color transparentColor = new Color(0f, 0f, 0f, 0f);

    private Texture2D _texture;
    private Color32[] _pixels;
    private DungeonData _data;
    private RectTransform _minimapRect;
    private Vector2Int _lastPlayerGrid;
    private bool _hasLastPlayerGrid;
    private bool _warnedMissingReferences;
    private bool _warnedInitialInitializeFailed;
    private Coroutine _initializationRoutine;

    private void Awake()
    {
        _minimapRect = minimapImage != null
            ? minimapImage.rectTransform
            : transform as RectTransform;
        if (playerMarkerGraphic != null)
            playerMarkerGraphic.color = playerColor;

        WarnIfMissingReferences();
    }

    private void OnEnable()
    {
        SubscribeEvents();
        StartInitialInitializeRoutine();
    }

    private void Start()
    {
        InitializeFromCurrentDungeon();
    }

    private void OnDisable()
    {
        StopInitialInitializeRoutine();
        UnsubscribeEvents();
    }

    private void OnDestroy()
    {
        if (_texture != null)
            Destroy(_texture);
    }

    private void LateUpdate()
    {
        if (!CanUpdateMarker())
            return;

        Vector2Int playerGrid = dungeonManager.WorldToGrid(player.position);
        if (_hasLastPlayerGrid && playerGrid == _lastPlayerGrid)
            return;

        _lastPlayerGrid = playerGrid;
        _hasLastPlayerGrid = true;
        UpdatePlayerMarker(playerGrid);
    }

    private void SubscribeEvents()
    {
        if (eventChannel != null)
            eventChannel.OnFloorChanged += OnFloorChanged;
        if (fogOfWar != null)
            fogOfWar.VisibilityChanged += OnFogVisibilityChanged;
    }

    private void UnsubscribeEvents()
    {
        if (eventChannel != null)
            eventChannel.OnFloorChanged -= OnFloorChanged;
        if (fogOfWar != null)
            fogOfWar.VisibilityChanged -= OnFogVisibilityChanged;
    }

    private void OnFloorChanged(int previousFloor, int newFloor)
    {
        StartInitialInitializeRoutine();
        InitializeFromCurrentDungeon();
    }

    private void OnFogVisibilityChanged()
    {
        InitializeFromCurrentDungeon();
    }

    private void InitializeFromCurrentDungeon()
    {
        if (!CanRender() || !IsFogReady())
            return;

        DungeonData latestData = dungeonManager.Data;
        if (latestData == null)
            return;

        bool mustRecreateTexture =
            _texture == null ||
            _data != latestData ||
            _texture.width != latestData.MapWidth * pixelsPerCell ||
            _texture.height != latestData.MapHeight * pixelsPerCell;

        _data = latestData;

        if (mustRecreateTexture)
            RecreateTexture(latestData);

        RefreshTexture();
        ForceMarkerRefresh();
    }

    private void StartInitialInitializeRoutine()
    {
        if (_initializationRoutine != null)
            return;

        _initializationRoutine = StartCoroutine(WaitForInitialStateAndInitialize());
    }

    private void StopInitialInitializeRoutine()
    {
        if (_initializationRoutine == null)
            return;

        StopCoroutine(_initializationRoutine);
        _initializationRoutine = null;
    }

    private System.Collections.IEnumerator WaitForInitialStateAndInitialize()
    {
        for (int frame = 0; frame < InitialInitializeMaxFrames; frame++)
        {
            if (CanInitializeFromCurrentState())
            {
                InitializeFromCurrentDungeon();
                _initializationRoutine = null;
                yield break;
            }

            yield return null;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_warnedInitialInitializeFailed)
        {
            Debug.LogWarning("[MinimapController] Initial minimap state was not ready within the bootstrap window.", this);
            _warnedInitialInitializeFailed = true;
        }
#endif
        _initializationRoutine = null;
    }

    private bool CanInitializeFromCurrentState()
    {
        return CanRender() &&
               dungeonManager.Data != null &&
               IsFogReady();
    }

    private bool IsFogReady()
    {
        return fogOfWar != null && fogOfWar.HasInitialized;
    }

    private bool CanRender()
    {
        WarnIfMissingReferences();
        return minimapImage != null &&
               playerMarker != null &&
               player != null &&
               dungeonManager != null &&
               fogOfWar != null;
    }

    private bool CanUpdateMarker()
    {
        return playerMarker != null &&
               player != null &&
               dungeonManager != null &&
               dungeonManager.Data != null;
    }

    private void RecreateTexture(DungeonData data)
    {
        if (_texture != null)
            Destroy(_texture);

        int width = data.MapWidth * pixelsPerCell;
        int height = data.MapHeight * pixelsPerCell;
        _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        _pixels = new Color32[width * height];
        minimapImage.texture = _texture;
    }

    private void RefreshTexture()
    {
        if (_data == null || _texture == null || _pixels == null || fogOfWar == null)
            return;

        Color32 clear = transparentColor;
        for (int i = 0; i < _pixels.Length; i++)
            _pixels[i] = clear;

        for (int row = 0; row < _data.MapHeight; row++)
        {
            for (int col = 0; col < _data.MapWidth; col++)
            {
                Vector2Int cell = new Vector2Int(col, row);
                if (!fogOfWar.IsExploredCell(cell))
                    continue;

                int tileType = _data.GetTileTypeUnchecked(col, row);
                if (!TryGetCellColor(tileType, fogOfWar.IsVisibleCell(cell), out Color32 color))
                    continue;

                FillCellPixels(col, row, color);
            }
        }

        _texture.SetPixels32(_pixels);
        _texture.Apply(false, false);
    }

    private bool TryGetCellColor(int tileType, bool isVisible, out Color32 color)
    {
        switch (tileType)
        {
            case DungeonGenerator.ROOM:
            case DungeonGenerator.STAIR_UP:
                color = isVisible ? visibleRoomColor : exploredRoomColor;
                return true;
            case DungeonGenerator.CORRIDOR:
                color = isVisible ? visibleCorridorColor : exploredCorridorColor;
                return true;
            case DungeonGenerator.DOOR_CLOSED:
                color = isVisible ? visibleDoorColor : exploredDoorColor;
                return true;
            default:
                color = transparentColor;
                return false;
        }
    }

    private void FillCellPixels(int gridX, int gridY, Color32 color)
    {
        int textureWidth = _texture.width;
        int startX = gridX * pixelsPerCell;
        int startY = (_data.MapHeight - 1 - gridY) * pixelsPerCell;

        for (int y = 0; y < pixelsPerCell; y++)
        {
            int pixelRow = (startY + y) * textureWidth;
            for (int x = 0; x < pixelsPerCell; x++)
                _pixels[pixelRow + startX + x] = color;
        }
    }

    private void ForceMarkerRefresh()
    {
        _hasLastPlayerGrid = false;
        if (!CanUpdateMarker())
            return;

        Vector2Int playerGrid = dungeonManager.WorldToGrid(player.position);
        _lastPlayerGrid = playerGrid;
        _hasLastPlayerGrid = true;
        UpdatePlayerMarker(playerGrid);
    }

    private void UpdatePlayerMarker(Vector2Int grid)
    {
        if (_data == null || !_data.InBounds(grid.x, grid.y))
        {
            playerMarker.gameObject.SetActive(false);
            return;
        }

        playerMarker.gameObject.SetActive(fogOfWar == null || fogOfWar.IsExploredCell(grid));
        playerMarker.anchoredPosition = GridToMinimapAnchoredPosition(grid);
    }

    private Vector2 GridToMinimapAnchoredPosition(Vector2Int grid)
    {
        Rect rect = _minimapRect.rect;
        float normalizedX = (grid.x + 0.5f) / _data.MapWidth;
        float normalizedY = (grid.y + 0.5f) / _data.MapHeight;
        float x = normalizedX * rect.width - _minimapRect.pivot.x * rect.width;
        float y = (1f - normalizedY) * rect.height - _minimapRect.pivot.y * rect.height;
        return new Vector2(x, y);
    }

    private void WarnIfMissingReferences()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedMissingReferences)
            return;

        if (minimapImage != null &&
            playerMarker != null &&
            player != null &&
            dungeonManager != null &&
            fogOfWar != null)
            return;

        Debug.LogWarning("[MinimapController] Inspector references are incomplete. Minimap update skipped until references are assigned.", this);
        _warnedMissingReferences = true;
#endif
    }
}
