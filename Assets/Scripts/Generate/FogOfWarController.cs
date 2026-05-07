using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class FogOfWarController : MonoBehaviour
{
    [Header("Tilemap")]
    [SerializeField] private Tilemap fogTilemap;
    [SerializeField] private TileBase unexploredFogTile;
    [SerializeField] private TileBase exploredFogTile;

    [Header("Fog Colors")]
    [SerializeField] private Color unexploredFogColor = Color.black;
    [SerializeField] private Color exploredFogColor = new Color(0f, 0f, 0f, 0.55f);

    [Header("Dependencies")]
    [SerializeField] private Transform player;
    [SerializeField] private DungeonManager dungeonManager;
    [SerializeField] private DungeonEventChannel eventChannel;

    [Header("Visibility")]
    [SerializeField, Min(0)] private int visionRadius = 4;
    [SerializeField] private bool revealCurrentRoom = true;
    [SerializeField] private bool includeRoomDoorsOrPadding = true;
    [SerializeField, Min(0)] private int roomRevealPadding = 1;
    [SerializeField] private bool revealRoomBorderWalls = true;
    [SerializeField, Min(1)] private int roomBorderWallRevealThickness = 1;
    [SerializeField] private bool blockVisionByWalls = true;
    [SerializeField] private bool closedDoorsBlockVision = true;

    private bool[,] _explored;
    private DungeonData _data;
    private int _mapWidth;
    private int _mapHeight;
    private Vector2Int _lastPlayerGrid;
    private bool _hasLastPlayerGrid;
    private bool _needsFullInitialize = true;

    private readonly HashSet<Vector2Int> _previousVisibleCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> _currentVisibleCells = new HashSet<Vector2Int>();
    private readonly List<TileChangeData> _tileChangeBuffer = new List<TileChangeData>(256);
    private readonly Dictionary<int, TileChangeData[]> _tileChangeArraysBySize = new Dictionary<int, TileChangeData[]>();
    private static readonly Color ClearCellColor = Color.white;

    public Tilemap FogTilemap => fogTilemap;
    public TileBase UnexploredFogTile => unexploredFogTile;
    public TileBase ExploredFogTile => exploredFogTile;
    public Color UnexploredFogColor => unexploredFogColor;
    public Color ExploredFogColor => exploredFogColor;
    public Transform Player => player;
    public DungeonManager DungeonManager => dungeonManager;
    public int VisionRadius => visionRadius;
    public bool RevealCurrentRoom => revealCurrentRoom;
    public bool IncludeRoomDoorsOrPadding => includeRoomDoorsOrPadding;
    public int RoomRevealPadding => roomRevealPadding;
    public bool RevealRoomBorderWalls => revealRoomBorderWalls;
    public int RoomBorderWallRevealThickness => roomBorderWallRevealThickness;
    public bool BlockVisionByWalls => blockVisionByWalls;
    public bool ClosedDoorsBlockVision => closedDoorsBlockVision;

    private void Awake()
    {
        ResolveDependencies();
    }

    private void OnEnable()
    {
        SubscribeEvents();
    }

    private void Start()
    {
        ResolveDependencies();
        RequestFullInitialize();
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
    }

    private void LateUpdate()
    {
        if (!CanUpdateFog())
            return;

        DungeonData latestData = dungeonManager.Data;
        if (_needsFullInitialize || latestData != _data || _explored == null)
        {
            InitializeForDungeon(latestData);
            return;
        }

        Vector2Int playerGrid = dungeonManager.WorldToGrid(player.position);
        if (_hasLastPlayerGrid && playerGrid == _lastPlayerGrid)
            return;

        RefreshVisibility(playerGrid);
    }

    public void RequestFullInitialize()
    {
        _needsFullInitialize = true;
        _hasLastPlayerGrid = false;
    }

    public void ForceRefresh()
    {
        if (!CanUpdateFog())
            return;

        if (_data == null || _data != dungeonManager.Data)
        {
            RequestFullInitialize();
            return;
        }

        RefreshVisibility(dungeonManager.WorldToGrid(player.position));
    }

    private void ResolveDependencies()
    {
        if (dungeonManager == null)
            dungeonManager = DungeonManager.Instance != null ? DungeonManager.Instance : FindAnyObjectByType<DungeonManager>();

        if (eventChannel == null && dungeonManager != null)
            eventChannel = dungeonManager.eventChannel;

        if (player == null)
        {
            GameObject playerObject = GameObject.FindWithTag("Player");
            if (playerObject == null)
            {
                PlayerController controller = FindAnyObjectByType<PlayerController>();
                if (controller != null)
                    playerObject = controller.gameObject;
            }

            if (playerObject != null)
                player = playerObject.transform;
        }
    }

    private void SubscribeEvents()
    {
        ResolveDependencies();
        if (eventChannel == null)
            return;

        eventChannel.OnFloorChanged += OnFloorChanged;
        eventChannel.OnRoomEntered += OnRoomEntered;
    }

    private void UnsubscribeEvents()
    {
        if (eventChannel == null)
            return;

        eventChannel.OnFloorChanged -= OnFloorChanged;
        eventChannel.OnRoomEntered -= OnRoomEntered;
    }

    private void OnFloorChanged(int previousFloor, int newFloor)
    {
        RequestFullInitialize();
    }

    private void OnRoomEntered(RoomEnteredEventArgs args)
    {
        ForceRefresh();
    }

    private bool CanUpdateFog()
    {
        return fogTilemap != null &&
               unexploredFogTile != null &&
               exploredFogTile != null &&
               player != null &&
               dungeonManager != null &&
               dungeonManager.Data != null;
    }

    private void InitializeForDungeon(DungeonData data)
    {
        _data = data;
        _mapWidth = data.MapWidth;
        _mapHeight = data.MapHeight;
        _explored = new bool[_mapWidth, _mapHeight];
        _previousVisibleCells.Clear();
        _currentVisibleCells.Clear();
        _tileChangeBuffer.Clear();
        _hasLastPlayerGrid = false;
        _needsFullInitialize = false;

        FillFogWithUnexplored();
        RefreshVisibility(dungeonManager.WorldToGrid(player.position));
    }

    private void FillFogWithUnexplored()
    {
        fogTilemap.ClearAllTiles();

        int total = _mapWidth * _mapHeight;
        TileChangeData[] changes = GetTileChangeArray(total);
        int index = 0;

        for (int row = 0; row < _mapHeight; row++)
            for (int col = 0; col < _mapWidth; col++)
                changes[index++] = BuildFogChange(
                    new Vector2Int(col, row),
                    unexploredFogTile,
                    unexploredFogColor);

        fogTilemap.SetTiles(changes, true);
    }

    private void RefreshVisibility(Vector2Int playerGrid)
    {
        if (_data == null || !_data.InBounds(playerGrid.x, playerGrid.y))
            return;

        _lastPlayerGrid = playerGrid;
        _hasLastPlayerGrid = true;

        _currentVisibleCells.Clear();
        AddVisionRadiusCells(playerGrid);

        if (revealCurrentRoom)
        {
            RoomInfo? room = dungeonManager.GetRoomAt(playerGrid.x, playerGrid.y);
            if (room.HasValue)
                AddRoomVisibleCells(room.Value);
        }

        MarkCurrentVisibleExplored();
        ApplyVisibilityDelta();
        SwapVisibleSets();
    }

    private void AddVisionRadiusCells(Vector2Int center)
    {
        int radiusSqr = visionRadius * visionRadius;
        for (int y = center.y - visionRadius; y <= center.y + visionRadius; y++)
        {
            for (int x = center.x - visionRadius; x <= center.x + visionRadius; x++)
            {
                if (!_data.InBounds(x, y))
                    continue;

                int dx = x - center.x;
                int dy = y - center.y;
                if (dx * dx + dy * dy > radiusSqr)
                    continue;

                if (blockVisionByWalls && !HasLineOfSight(center, x, y))
                    continue;

                _currentVisibleCells.Add(new Vector2Int(x, y));
            }
        }
    }

    private bool HasLineOfSight(Vector2Int origin, int targetX, int targetY)
    {
        int x0 = origin.x;
        int y0 = origin.y;
        int x1 = targetX;
        int y1 = targetY;

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            if (x0 == x1 && y0 == y1)
                return true;

            int e2 = err * 2;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }

            if (!_data.InBounds(x0, y0))
                return false;

            if (IsVisionBlockingCell(x0, y0))
                return x0 == x1 && y0 == y1;
        }
    }

    private bool IsVisionBlockingCell(int x, int y)
    {
        int tileType = _data.GetTileType(x, y);
        if (tileType == DungeonGenerator.EMPTY)
            return true;

        return closedDoorsBlockVision &&
               tileType == DungeonGenerator.DOOR_CLOSED;
    }

    private void AddRoomVisibleCells(RoomInfo room)
    {
        AddRoomInteriorCells(room);

        if (revealRoomBorderWalls)
            AddRoomBorderWallCells(room, roomBorderWallRevealThickness);

        if (!includeRoomDoorsOrPadding || roomRevealPadding <= 0)
            return;

        AddRoomPaddingCells(room, roomRevealPadding);
    }

    private void AddRoomInteriorCells(RoomInfo room)
    {
        for (int y = room.Y; y < room.Bottom; y++)
            for (int x = room.X; x < room.Right; x++)
                if (_data.InBounds(x, y) && _data.GetTileType(x, y) == DungeonGenerator.ROOM)
                    _currentVisibleCells.Add(new Vector2Int(x, y));
    }

    private void AddRoomBorderWallCells(RoomInfo room, int thickness)
    {
        int left = Mathf.Max(0, room.X - thickness);
        int right = Mathf.Min(_mapWidth - 1, room.Right + thickness - 1);
        int top = Mathf.Max(0, room.Y - thickness);
        int bottom = Mathf.Min(_mapHeight - 1, room.Bottom + thickness - 1);

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (room.Contains(x, y))
                    continue;

                if (IsRoomBorderWallTile(x, y))
                    _currentVisibleCells.Add(new Vector2Int(x, y));
            }
        }
    }

    private bool IsRoomBorderWallTile(int x, int y)
    {
        return _data.InBounds(x, y) &&
               _data.GetTileType(x, y) == DungeonGenerator.EMPTY;
    }

    private void AddRoomPaddingCells(RoomInfo room, int padding)
    {
        int left = Mathf.Max(0, room.X - padding);
        int right = Mathf.Min(_mapWidth, room.Right + padding);
        int top = Mathf.Max(0, room.Y - padding);
        int bottom = Mathf.Min(_mapHeight, room.Bottom + padding);

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int tileType = _data.GetTileType(x, y);
                if (tileType == DungeonGenerator.EMPTY)
                    continue;

                _currentVisibleCells.Add(new Vector2Int(x, y));
            }
        }
    }

    private void MarkCurrentVisibleExplored()
    {
        foreach (Vector2Int cell in _currentVisibleCells)
            _explored[cell.x, cell.y] = true;
    }

    private void ApplyVisibilityDelta()
    {
        _tileChangeBuffer.Clear();

        foreach (Vector2Int previous in _previousVisibleCells)
        {
            if (_currentVisibleCells.Contains(previous))
                continue;

            AddFogChange(
                previous,
                GetFogTileForHiddenCell(previous),
                GetFogColorForHiddenCell(previous));
        }

        foreach (Vector2Int current in _currentVisibleCells)
        {
            if (_previousVisibleCells.Contains(current))
                continue;

            AddFogChange(current, null, ClearCellColor);
        }

        FlushFogChanges();
    }

    private TileBase GetFogTileForHiddenCell(Vector2Int cell)
    {
        return _explored[cell.x, cell.y] ? exploredFogTile : unexploredFogTile;
    }

    private Color GetFogColorForHiddenCell(Vector2Int cell)
    {
        return _explored[cell.x, cell.y] ? exploredFogColor : unexploredFogColor;
    }

    private void AddFogChange(Vector2Int gridCell, TileBase tile, Color color)
    {
        _tileChangeBuffer.Add(BuildFogChange(gridCell, tile, color));
    }

    private static TileChangeData BuildFogChange(Vector2Int gridCell, TileBase tile, Color color)
    {
        return new TileChangeData
        {
            position = new Vector3Int(gridCell.x, -gridCell.y, 0),
            tile = tile,
            color = color,
            transform = Matrix4x4.identity,
        };
    }

    private void FlushFogChanges()
    {
        int count = _tileChangeBuffer.Count;
        if (count == 0)
            return;

        TileChangeData[] changes = GetTileChangeArray(count);
        for (int i = 0; i < count; i++)
            changes[i] = _tileChangeBuffer[i];

        fogTilemap.SetTiles(changes, true);
    }

    private void SwapVisibleSets()
    {
        _previousVisibleCells.Clear();
        foreach (Vector2Int cell in _currentVisibleCells)
            _previousVisibleCells.Add(cell);
    }

    private TileChangeData[] GetTileChangeArray(int count)
    {
        if (!_tileChangeArraysBySize.TryGetValue(count, out TileChangeData[] changes))
        {
            changes = new TileChangeData[count];
            _tileChangeArraysBySize.Add(count, changes);
        }

        return changes;
    }
}
