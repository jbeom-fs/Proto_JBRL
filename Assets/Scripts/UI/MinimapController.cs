using UnityEngine;
using UnityEngine.Tilemaps;
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
    [SerializeField, Min(0)] private int stairMarkerPixelPadding = 4;
    [SerializeField] private Color visibleRoomColor    = new Color(0.75f, 0.82f, 0.95f, 0.92f);
    [SerializeField] private Color exploredRoomColor   = new Color(0.26f, 0.31f, 0.42f, 0.78f);
    [SerializeField] private Color visibleCorridorColor  = new Color(0.70f, 0.74f, 0.82f, 0.88f);
    [SerializeField] private Color exploredCorridorColor = new Color(0.18f, 0.21f, 0.28f, 0.72f);
    [SerializeField] private Color visibleDoorColor    = new Color(0.95f, 0.78f, 0.36f, 0.95f);
    [SerializeField] private Color exploredDoorColor   = new Color(0.46f, 0.36f, 0.18f, 0.78f);
    [SerializeField] private Color stairColor          = new Color(0.05f, 0.16f, 0.55f, 1f);
    [SerializeField] private Color playerColor         = new Color(0f,    0.32f, 0.12f, 1f);
    [SerializeField] private Color transparentColor    = new Color(0f,    0f,    0f,    0f);

    // ── Dungeon mode state ────────────────────────────────────────────
    private Texture2D  _texture;
    private Color32[]  _pixels;
    private DungeonData _data;

    // ── Tilemap mode state ────────────────────────────────────────────
    private Texture2D          _tilemapTexture;
    private TilemapMinimapSource _tilemapSource;
    private TilemapMinimapSource _lastBuiltTilemapSource;
    private BoundsInt          _tilemapBounds;

    // ── Shared state ──────────────────────────────────────────────────
    private enum MinimapMode { Dungeon, Tilemap }
    private MinimapMode _mode = MinimapMode.Dungeon;
    private string      _pendingTilemapLocationId;
    private RectTransform _minimapRect;
    private Canvas        _rootCanvas;
    private Vector2       _playerMarkerBaseSize;
    private Vector2Int  _lastPlayerGrid;
    private bool        _hasLastPlayerGrid;
    private bool        _warnedMissingReferences;
    private bool        _warnedInitialInitializeFailed;
    private Coroutine   _initializationRoutine;

    // ═════════════════════════════════════════════════════════════════
    // Unity lifecycle
    // ═════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _minimapRect = minimapImage != null
            ? minimapImage.rectTransform
            : transform as RectTransform;
        _rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (playerMarker != null)
        {
            _playerMarkerBaseSize = playerMarker.sizeDelta;
            SnapPlayerMarkerSize();
        }

        if (playerMarkerGraphic != null)
            playerMarkerGraphic.color = playerColor;

        WarnIfMissingReferences();
    }

    private void OnEnable()
    {
        SnapPlayerMarkerSize();
        SubscribeEvents();
        StartInitialInitializeRoutine();
    }

    private void Start()
    {
        if (_mode == MinimapMode.Dungeon)
            InitializeFromCurrentDungeon();
    }

    private void OnDisable()
    {
        StopInitialInitializeRoutine();
        UnsubscribeEvents();
    }

    private void OnDestroy()
    {
        if (_texture != null)        Destroy(_texture);
        if (_tilemapTexture != null) Destroy(_tilemapTexture);
    }

    private void LateUpdate()
    {
        if (_mode == MinimapMode.Tilemap)
        {
            UpdateTilemapMarkerIfMoved();
            return;
        }

        if (!CanUpdateMarker())
            return;

        Vector2Int playerGrid = dungeonManager.WorldToGrid(player.position);
        if (_hasLastPlayerGrid && playerGrid == _lastPlayerGrid)
            return;

        _lastPlayerGrid    = playerGrid;
        _hasLastPlayerGrid = true;
        UpdateDungeonPlayerMarker(playerGrid);
    }

    // ═════════════════════════════════════════════════════════════════
    // Public API — called by TownDungeonTransitionManager on teleport
    // ═════════════════════════════════════════════════════════════════

    public void SetDungeonSource()
    {
        StopInitialInitializeRoutine();

        _mode                    = MinimapMode.Dungeon;
        _tilemapSource           = null;
        _pendingTilemapLocationId = null;
        _hasLastPlayerGrid       = false;

        // Dungeon texture will be built when fog/floor events fire after dungeon generation.
        // Start polling as a safety net in case events arrive before this frame completes.
        StartInitialInitializeRoutine();
    }

    public void SetTilemapSource(string locationId)
    {
        if (string.IsNullOrWhiteSpace(locationId))
        {
            Warn("SetTilemapSource called with empty locationId.");
            return;
        }

        StopInitialInitializeRoutine();

        _mode              = MinimapMode.Tilemap;
        _hasLastPlayerGrid = false;

        // Clear stale texture immediately so a failed lookup never leaves dungeon map visible.
        if (minimapImage != null)
            minimapImage.texture = null;

        if (LocationMinimapRegistry.TryGet(locationId, out TilemapMinimapSource source))
        {
            _tilemapSource           = source;
            _pendingTilemapLocationId = null;

            if (!source.IsReady)
            {
                Warn("TilemapMinimapSource '" + locationId + "' is registered but has no Walkable/Wall/Door Tilemap. " +
                     "Wire groundTilemap/wallTilemap/doorTilemap explicitly, or enable autoDiscoverChildren with proper child Layers.");
                return;
            }

            InitializeFromTilemapSource();
        }
        else
        {
            // Source not registered yet (root not enabled). Poll via coroutine.
            _tilemapSource           = null;
            _pendingTilemapLocationId = locationId;
            StartInitialInitializeRoutine();
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // Events
    // ═════════════════════════════════════════════════════════════════

    private void SubscribeEvents()
    {
        if (eventChannel != null) eventChannel.OnFloorChanged  += OnFloorChanged;
        if (fogOfWar     != null) fogOfWar.VisibilityChanged   += OnFogVisibilityChanged;
    }

    private void UnsubscribeEvents()
    {
        if (eventChannel != null) eventChannel.OnFloorChanged  -= OnFloorChanged;
        if (fogOfWar     != null) fogOfWar.VisibilityChanged   -= OnFogVisibilityChanged;
    }

    private void OnFloorChanged(int previousFloor, int newFloor)
    {
        if (_mode != MinimapMode.Dungeon)
            return;

        StopInitialInitializeRoutine();
        StartInitialInitializeRoutine();
        InitializeFromCurrentDungeon();
    }

    private void OnFogVisibilityChanged()
    {
        if (_mode != MinimapMode.Dungeon)
            return;

        InitializeFromCurrentDungeon();
    }

    // ═════════════════════════════════════════════════════════════════
    // Dungeon mode
    // ═════════════════════════════════════════════════════════════════

    private void InitializeFromCurrentDungeon()
    {
        if (_mode != MinimapMode.Dungeon)
            return;

        if (!CanRenderDungeon() || !IsFogReady())
            return;

        DungeonData latestData = dungeonManager.Data;
        if (latestData == null)
            return;

        bool mustRecreateTexture =
            _texture == null ||
            _data != latestData ||
            _texture.width  != latestData.MapWidth  * pixelsPerCell ||
            _texture.height != latestData.MapHeight * pixelsPerCell;

        _data = latestData;

        if (mustRecreateTexture)
            RecreateDungeonTexture(latestData);

        RefreshDungeonTexture();
        ForceMarkerRefresh();
    }

    private bool CanInitializeDungeonFromCurrentState()
    {
        return CanRenderDungeon() && dungeonManager.Data != null && IsFogReady();
    }

    private bool IsFogReady() => fogOfWar != null && fogOfWar.HasInitialized;

    private bool CanRenderDungeon()
    {
        WarnIfMissingReferences();
        return minimapImage  != null &&
               playerMarker  != null &&
               player        != null &&
               dungeonManager != null &&
               fogOfWar      != null;
    }

    private bool CanUpdateMarker()
    {
        return playerMarker   != null &&
               player         != null &&
               dungeonManager != null &&
               dungeonManager.Data != null;
    }

    private void RecreateDungeonTexture(DungeonData data)
    {
        if (_texture != null)
            Destroy(_texture);

        int width  = data.MapWidth  * pixelsPerCell;
        int height = data.MapHeight * pixelsPerCell;

        _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp,
        };
        _pixels = new Color32[width * height];
        minimapImage.texture = _texture;
    }

    private void RefreshDungeonTexture()
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
                if (!TryGetDungeonCellColor(tileType, fogOfWar.IsVisibleCell(cell), out Color32 color))
                    continue;

                FillDungeonCellPixels(col, row, color);
            }
        }

        DrawDungeonStairs();
        _texture.SetPixels32(_pixels);
        _texture.Apply(false, false);
    }

    private bool TryGetDungeonCellColor(int tileType, bool isVisible, out Color32 color)
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

    private void DrawDungeonStairs()
    {
        if (_data == null || fogOfWar == null)
            return;

        Color32 color = stairColor;
        for (int row = 0; row < _data.MapHeight; row++)
        {
            for (int col = 0; col < _data.MapWidth; col++)
            {
                if (_data.GetTileTypeUnchecked(col, row) != DungeonGenerator.STAIR_UP)
                    continue;

                var cell = new Vector2Int(col, row);
                if (!fogOfWar.IsExploredCell(cell))
                    continue;

                FillDungeonStairMarkerPixels(col, row, color);
            }
        }
    }

    private void FillDungeonStairMarkerPixels(int gridX, int gridY, Color32 color)
    {
        int textureWidth = _texture.width;
        int textureHeight = _texture.height;
        int startX = gridX * pixelsPerCell - stairMarkerPixelPadding;
        int startY = (_data.MapHeight - 1 - gridY) * pixelsPerCell - stairMarkerPixelPadding;
        int size = pixelsPerCell + stairMarkerPixelPadding * 2;

        int minX = Mathf.Max(0, startX);
        int minY = Mathf.Max(0, startY);
        int maxX = Mathf.Min(textureWidth, startX + size);
        int maxY = Mathf.Min(textureHeight, startY + size);

        for (int y = minY; y < maxY; y++)
        {
            int pixelRow = y * textureWidth;
            for (int x = minX; x < maxX; x++)
                _pixels[pixelRow + x] = color;
        }
    }

    private void FillDungeonCellPixels(int gridX, int gridY, Color32 color)
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
        _lastPlayerGrid    = playerGrid;
        _hasLastPlayerGrid = true;
        UpdateDungeonPlayerMarker(playerGrid);
    }

    private void UpdateDungeonPlayerMarker(Vector2Int grid)
    {
        if (_data == null || !_data.InBounds(grid.x, grid.y))
        {
            playerMarker.gameObject.SetActive(false);
            return;
        }

        playerMarker.gameObject.SetActive(fogOfWar == null || fogOfWar.IsExploredCell(grid));

        Rect  rect        = _minimapRect.rect;
        float normalizedX = (grid.x + 0.5f) / _data.MapWidth;
        float normalizedY = (grid.y + 0.5f) / _data.MapHeight;
        float x = normalizedX * rect.width  - _minimapRect.pivot.x * rect.width;
        float y = (1f - normalizedY) * rect.height - _minimapRect.pivot.y * rect.height;
        playerMarker.anchoredPosition = SnapMarkerPosition(x, y);
    }

    // ═════════════════════════════════════════════════════════════════
    // Tilemap mode
    // ═════════════════════════════════════════════════════════════════

    private void InitializeFromTilemapSource()
    {
        if (!CanRenderTilemap())
            return;

        // Cache: skip rebuild if same source and texture already built
        if (_tilemapTexture != null && _lastBuiltTilemapSource == _tilemapSource)
        {
            minimapImage.texture = _tilemapTexture;
            _hasLastPlayerGrid = false;
            return;
        }

        BuildTilemapTexture(_tilemapSource);
        _lastBuiltTilemapSource = _tilemapSource;
        _hasLastPlayerGrid = false;
    }

    private bool CanRenderTilemap()
    {
        return minimapImage  != null &&
               playerMarker  != null &&
               player        != null &&
               _tilemapSource != null &&
               _tilemapSource.IsReady;
    }

    private bool TryResolvePendingTilemapSource()
    {
        if (_pendingTilemapLocationId == null)
            return _tilemapSource != null;

        if (!LocationMinimapRegistry.TryGet(_pendingTilemapLocationId, out TilemapMinimapSource source))
            return false;

        _tilemapSource           = source;
        _pendingTilemapLocationId = null;
        return true;
    }

    private void BuildTilemapTexture(TilemapMinimapSource source)
    {
        System.Collections.Generic.IReadOnlyList<Tilemap> walkable = source.WalkableTilemaps;
        System.Collections.Generic.IReadOnlyList<Tilemap> walls = source.WallTilemaps;
        System.Collections.Generic.IReadOnlyList<Tilemap> doors = source.DoorTilemaps;

        CompressBoundsAll(walkable);
        CompressBoundsAll(walls);
        CompressBoundsAll(doors);

        if (!TryComputeUnionBounds(walkable, walls, doors, out BoundsInt bounds))
        {
            Warn("TilemapMinimapSource '" + source.LocationId + "' has empty Tilemap bounds. No minimap texture built.");
            return;
        }

        int w = bounds.size.x;
        int h = bounds.size.y;

        _tilemapBounds = bounds;

        if (_tilemapTexture != null)
        {
            Destroy(_tilemapTexture);
            _tilemapTexture = null;
        }

        int texW = w * pixelsPerCell;
        int texH = h * pixelsPerCell;

        _tilemapTexture = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp,
        };

        Color32[] pixels = new Color32[texW * texH];
        Color32 clear = transparentColor;
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        Color32 groundColor = source.GroundColor;
        Color32 wallColor   = source.WallColor;
        Color32 doorColor   = source.DoorColor;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector3Int cell = new Vector3Int(bounds.xMin + x, bounds.yMin + y, 0);

                bool hasDoor = AnyHasTile(doors, cell);
                bool hasWall = AnyHasTile(walls, cell);
                bool hasGround = AnyHasTile(walkable, cell);

                if (!hasDoor && !hasWall && !hasGround)
                    continue;

                Color32 color;
                if (hasDoor)
                    color = doorColor;
                else if (hasWall)
                    color = wallColor;
                else
                    color = groundColor;

                int startX = x * pixelsPerCell;
                int startY = y * pixelsPerCell; // Tilemap Y↑ matches Texture2D Y↑, no flip needed

                for (int py = 0; py < pixelsPerCell; py++)
                {
                    int pixelRow = (startY + py) * texW;
                    for (int px = 0; px < pixelsPerCell; px++)
                        pixels[pixelRow + startX + px] = color;
                }
            }
        }

        _tilemapTexture.SetPixels32(pixels);
        _tilemapTexture.Apply(false, false);
        minimapImage.texture = _tilemapTexture;
    }

    private static void CompressBoundsAll(System.Collections.Generic.IReadOnlyList<Tilemap> tilemaps)
    {
        if (tilemaps == null)
            return;
        for (int i = 0; i < tilemaps.Count; i++)
            tilemaps[i]?.CompressBounds();
    }

    private static bool TryComputeUnionBounds(
        System.Collections.Generic.IReadOnlyList<Tilemap> walkable,
        System.Collections.Generic.IReadOnlyList<Tilemap> walls,
        System.Collections.Generic.IReadOnlyList<Tilemap> doors,
        out BoundsInt bounds)
    {
        bool initialized = false;
        bounds = default;

        AccumulateBounds(walkable, ref bounds, ref initialized);
        AccumulateBounds(walls, ref bounds, ref initialized);
        AccumulateBounds(doors, ref bounds, ref initialized);

        return initialized && bounds.size.x > 0 && bounds.size.y > 0;
    }

    private static void AccumulateBounds(
        System.Collections.Generic.IReadOnlyList<Tilemap> tilemaps,
        ref BoundsInt bounds,
        ref bool initialized)
    {
        if (tilemaps == null)
            return;

        for (int i = 0; i < tilemaps.Count; i++)
        {
            Tilemap tilemap = tilemaps[i];
            if (tilemap == null)
                continue;

            BoundsInt b = tilemap.cellBounds;
            if (b.size.x <= 0 || b.size.y <= 0)
                continue;

            if (!initialized)
            {
                bounds = b;
                initialized = true;
            }
            else
            {
                bounds = UnionBounds(bounds, b);
            }
        }
    }

    private static bool AnyHasTile(System.Collections.Generic.IReadOnlyList<Tilemap> tilemaps, Vector3Int cell)
    {
        if (tilemaps == null)
            return false;

        for (int i = 0; i < tilemaps.Count; i++)
        {
            Tilemap tilemap = tilemaps[i];
            if (tilemap != null && tilemap.GetTile(cell) != null)
                return true;
        }
        return false;
    }

    private void UpdateTilemapMarkerIfMoved()
    {
        if (!CanRenderTilemap())
            return;

        Tilemap refTilemap = _tilemapSource.ReferenceTilemap;
        if (refTilemap == null)
            return;
        Vector3Int cell    = refTilemap.WorldToCell(player.position);
        Vector2Int cell2d  = new Vector2Int(cell.x, cell.y);

        if (_hasLastPlayerGrid && cell2d == _lastPlayerGrid)
            return;

        _lastPlayerGrid    = cell2d;
        _hasLastPlayerGrid = true;
        UpdateTilemapPlayerMarker(cell);
    }

    private void UpdateTilemapPlayerMarker(Vector3Int cell)
    {
        BoundsInt b = _tilemapBounds;

        if (cell.x < b.xMin || cell.x >= b.xMax || cell.y < b.yMin || cell.y >= b.yMax)
        {
            playerMarker.gameObject.SetActive(false);
            return;
        }

        playerMarker.gameObject.SetActive(true);

        int   localX      = cell.x - b.xMin;
        int   localY      = cell.y - b.yMin;
        Rect  rect        = _minimapRect.rect;
        float normalizedX = (localX + 0.5f) / b.size.x;
        float normalizedY = (localY + 0.5f) / b.size.y;
        float x = normalizedX * rect.width  - _minimapRect.pivot.x * rect.width;
        float y = normalizedY * rect.height - _minimapRect.pivot.y * rect.height; // no flip: Tilemap Y↑ = UI Y↑

        playerMarker.anchoredPosition = SnapMarkerPosition(x, y);
    }

    private Vector2 SnapMarkerPosition(float x, float y)
    {
        float scale = GetCanvasScaleFactor();
        return new Vector2(
            Mathf.Round(x * scale) / scale,
            Mathf.Round(y * scale) / scale);
    }

    private void SnapPlayerMarkerSize()
    {
        if (playerMarker == null)
            return;

        float scale = GetCanvasScaleFactor();
        playerMarker.sizeDelta = new Vector2(
            Mathf.Max(1f, Mathf.Round(_playerMarkerBaseSize.x * scale)) / scale,
            Mathf.Max(1f, Mathf.Round(_playerMarkerBaseSize.y * scale)) / scale);
    }

    private float GetCanvasScaleFactor()
    {
        if (_rootCanvas == null)
            _rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;

        if (_rootCanvas == null || _rootCanvas.scaleFactor <= 0f)
            return 1f;

        return _rootCanvas.scaleFactor;
    }

    private static BoundsInt UnionBounds(BoundsInt a, BoundsInt b)
    {
        int xMin = Mathf.Min(a.xMin, b.xMin);
        int yMin = Mathf.Min(a.yMin, b.yMin);
        int xMax = Mathf.Max(a.xMax, b.xMax);
        int yMax = Mathf.Max(a.yMax, b.yMax);
        return new BoundsInt(xMin, yMin, 0, xMax - xMin, yMax - yMin, 1);
    }

    // ═════════════════════════════════════════════════════════════════
    // Bootstrap coroutine — polls until minimap state is ready
    // ═════════════════════════════════════════════════════════════════

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
            if (_mode == MinimapMode.Tilemap)
            {
                if (TryResolvePendingTilemapSource())
                {
                    InitializeFromTilemapSource();
                    _initializationRoutine = null;
                    yield break;
                }
            }
            else
            {
                if (CanInitializeDungeonFromCurrentState())
                {
                    InitializeFromCurrentDungeon();
                    _initializationRoutine = null;
                    yield break;
                }
            }

            yield return null;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_warnedInitialInitializeFailed)
        {
            if (_mode == MinimapMode.Dungeon)
                Debug.LogWarning("[MinimapController] Dungeon minimap state was not ready within the bootstrap window.", this);
            else if (_pendingTilemapLocationId != null)
                Debug.LogWarning("[MinimapController] TilemapMinimapSource '" + _pendingTilemapLocationId + "' was not registered within the bootstrap window. Check LocationId and OnEnable.", this);
            _warnedInitialInitializeFailed = true;
        }
#endif
        // Ensure no stale texture remains after a failed tilemap poll.
        if (_mode == MinimapMode.Tilemap && minimapImage != null && minimapImage.texture == null)
            playerMarker?.gameObject.SetActive(false);

        _initializationRoutine = null;
    }

    // ═════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════

    private void WarnIfMissingReferences()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedMissingReferences)
            return;

        if (minimapImage != null && playerMarker != null && player != null)
            return;

        Debug.LogWarning(
            "[MinimapController] minimapImage / playerMarker / player are not assigned. " +
            "Minimap will not render until all three are set.",
            this);
        _warnedMissingReferences = true;
#endif
    }

    private void Warn(string message)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning("[MinimapController] " + message, this);
#endif
    }
}
