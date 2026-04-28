public class DungeonData
{
    private readonly int[,] _grid;
    private readonly RoomInfo[] _rooms;
    private readonly int[,] _roomIndex;

    public int MapWidth { get; }
    public int MapHeight { get; }
    public int RoomCount => _rooms.Length;

    public DungeonData(int[,] grid, RoomInfo[] rooms)
    {
        _grid = grid;
        _rooms = rooms;
        MapWidth = grid.GetLength(1);
        MapHeight = grid.GetLength(0);
        _roomIndex = BuildRoomIndex(rooms, MapWidth, MapHeight);
    }

    public int GetTileType(int col, int row)
    {
        if (!InBounds(col, row)) return DungeonGenerator.EMPTY;
        return _grid[row, col];
    }

    public int GetTileTypeUnchecked(int col, int row)
    {
        return _grid[row, col];
    }

    public bool IsWalkable(int col, int row)
    {
        if (!InBounds(col, row)) return false;

        int v = _grid[row, col];
        return v == DungeonGenerator.ROOM ||
               v == DungeonGenerator.CORRIDOR ||
               v == DungeonGenerator.STAIR_UP;
    }

    public void SetTileValue(int col, int row, int value)
    {
        if (InBounds(col, row))
            _grid[row, col] = value;
    }

    public void ForEachTile(System.Action<int, int, int> action)
    {
        for (int row = 0; row < MapHeight; row++)
            for (int col = 0; col < MapWidth; col++)
                action(col, row, _grid[row, col]);
    }

    public RoomInfo? GetRoomAt(int col, int row)
    {
        if (!InBounds(col, row)) return null;

        int idx = _roomIndex[row, col];
        if (idx < 0) return null;
        return _rooms[idx];
    }

    public RoomInfo? GetRoomContaining(int tileType)
    {
        for (int row = 0; row < MapHeight; row++)
            for (int col = 0; col < MapWidth; col++)
            {
                if (_grid[row, col] != tileType) continue;

                var room = GetRoomAt(col, row);
                if (room.HasValue) return room;
            }

        return null;
    }

    public void ForEachRoom(System.Action<RoomInfo> action)
    {
        for (int i = 0; i < _rooms.Length; i++)
            action(_rooms[i]);
    }

    public RoomInfo GetRoom(int index)
    {
        return _rooms[index];
    }

    public bool InBounds(int col, int row)
    {
        return col >= 0 && col < MapWidth && row >= 0 && row < MapHeight;
    }

    private static int[,] BuildRoomIndex(RoomInfo[] rooms, int width, int height)
    {
        var index = new int[height, width];

        for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++)
                index[row, col] = -1;

        for (int i = 0; i < rooms.Length; i++)
        {
            var room = rooms[i];
            for (int row = room.Y; row < room.Bottom; row++)
                for (int col = room.X; col < room.Right; col++)
                    index[row, col] = i;
        }

        return index;
    }
}
