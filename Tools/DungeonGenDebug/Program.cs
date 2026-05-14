using System;
using System.Collections.Generic;

internal static class Program
{
    static int Main(string[] args)
    {
        long seedLong = 283321776792L;
        int floor = 3;
        bool dumpAll = false;
        bool sceneSettings = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--seed" && i + 1 < args.Length) seedLong = long.Parse(args[++i]);
            else if (args[i] == "--floor" && i + 1 < args.Length) floor = int.Parse(args[++i]);
            else if (args[i] == "--all") dumpAll = true;
            else if (args[i] == "--scene-settings") sceneSettings = true;
        }

        if (dumpAll)
        {
            for (int f = 1; f <= 5; f++)
                RunOne(seedLong, f, verbose: false, sceneSettings);
            long[] otherSeeds = { 111111111111L, 999999999999L, 424242424242L };
            for (int i = 0; i < otherSeeds.Length; i++)
                RunOne(otherSeeds[i], 3, verbose: false, sceneSettings);
            return 0;
        }

        RunOne(seedLong, floor, verbose: true, sceneSettings);
        return 0;
    }

    static void RunOne(long seedLong, int floor, bool verbose, bool sceneSettings)
    {
        DungeonGenerator.DebugSink = verbose ? Console.WriteLine : null;
        DungeonGenerator.DebugCorridorCarving = verbose;

        var s = DungeonSettings.Default;
        s.MapWidth = sceneSettings ? 120 : 80;
        s.MapHeight = sceneSettings ? 80 : 50;
        s.MinRoomSize = sceneSettings ? 10 : 5;
        s.MaxRoomSize = sceneSettings ? 50 : 14;
        s.BspDepth = 4;
        s.ExtraConnProb = 0.5f;
        s.Floor = floor;
        s.MaxFloor = 100;
        s.Seed = (int)(seedLong % int.MaxValue);

        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine($"Seed(long)={seedLong} Settings.Seed={s.Seed} Floor={s.Floor} DerivedSeed={s.DeriveSeed()}");
        Console.WriteLine($"Settings={s.MapWidth}x{s.MapHeight} RoomSize={s.MinRoomSize}-{s.MaxRoomSize}");
        Console.WriteLine("==================================================");

        var grid = DungeonGenerator.GenerateDungeon(s, out var rooms);

        Console.WriteLine();
        Console.WriteLine($"=== Rooms ({rooms.Length}) ===");
        for (int i = 0; i < rooms.Length; i++)
        {
            var r = rooms[i];
            Console.WriteLine($"  R{i,2}: X={r.X,2} Y={r.Y,2} W={r.W,2} H={r.H,2} Right={r.Right,2} Bottom={r.Bottom,2} Cx={r.X + r.W / 2,2} Cy={r.Y + r.H / 2,2}  size={r.W * r.H}");
        }

        int bigIdx = PickCentralBottomBigRoom(rooms, s.MapWidth, s.MapHeight);
        if (bigIdx >= 0)
        {
            var big = rooms[bigIdx];
            Console.WriteLine();
            Console.WriteLine($"=== Central-bottom big-ish room candidate: R{bigIdx} (X={big.X} Y={big.Y} W={big.W} H={big.H}) ===");
            DumpAroundRoom(grid, big, 3, s);
        }

        Console.WriteLine();
        Console.WriteLine("=== Door candidate contiguous runs by room side ===");
        DoorRunScan(grid, s, rooms);

        Console.WriteLine();
        Console.WriteLine("=== Door candidates per room (perimeter CORRIDOR cells) ===");
        for (int i = 0; i < rooms.Length; i++)
        {
            var r = rooms[i];
            var doors = new List<(int x, int y)>();
            for (int col = r.X; col < r.Right; col++)
            {
                if (InBounds(s, col, r.Y - 1) && grid[r.Y - 1, col] == DungeonGenerator.CORRIDOR) doors.Add((col, r.Y - 1));
                if (InBounds(s, col, r.Bottom) && grid[r.Bottom, col] == DungeonGenerator.CORRIDOR) doors.Add((col, r.Bottom));
            }
            for (int row = r.Y; row < r.Bottom; row++)
            {
                if (InBounds(s, r.X - 1, row) && grid[row, r.X - 1] == DungeonGenerator.CORRIDOR) doors.Add((r.X - 1, row));
                if (InBounds(s, r.Right, row) && grid[row, r.Right] == DungeonGenerator.CORRIDOR) doors.Add((r.Right, row));
            }

            var doorListStr = new System.Text.StringBuilder();
            for (int d = 0; d < doors.Count; d++)
            {
                if (d > 0) doorListStr.Append(',');
                doorListStr.Append('(').Append(doors[d].x).Append(',').Append(doors[d].y).Append(')');
            }
            Console.WriteLine($"  R{i,2}: doors={doors.Count} [{doorListStr}]");
        }

        Console.WriteLine();
        Console.WriteLine("=== Walkable connectivity (4-conn flood from R0) ===");
        WalkableBFS(grid, s, rooms);

        Console.WriteLine();
        Console.WriteLine("=== Detached corridor scan (CORRIDOR cell w/ no 4-conn ROOM via walkable BFS to any room interior) ===");
        DetachedCorridorScan(grid, s, rooms);

        Console.WriteLine();
        Console.WriteLine("=== Phantom perimeter scan (CORRIDOR cell on roomA perimeter, but no 4-conn ROOM cell of roomA) ===");
        PhantomPerimeterScan(grid, s, rooms);

        Console.WriteLine();
        Console.WriteLine("=== Orphan door/stub scan (perimeter CORRIDOR whose outward neighbor is not CORRIDOR) ===");
        OrphanDoorStubScan(grid, s, rooms);

        Console.WriteLine();
        Console.WriteLine("=== Dual-door wall-sliver scan (two perim CORRIDOR cells with exactly 1 EMPTY between, same side) ===");
        DualDoorSliverScan(grid, s, rooms);

        Console.WriteLine();
        Console.WriteLine("=== Alongside corridor scan (CORRIDOR run at perim+1 distance spanning >=3 cells along a room side) ===");
        AlongsideCorridorScan(grid, s, rooms);
    }

    static void DualDoorSliverScan(int[,] grid, DungeonSettings s, DungeonGenerator.RoomRect[] rooms)
    {
        int hits = 0;
        for (int i = 0; i < rooms.Length; i++)
        {
            var r = rooms[i];

            // TOP: row Y-1, cols [X, Right)
            hits += ScanSideForSliver(grid, s, i, r.Y - 1, true, r.X, r.Right, "TOP");
            // BOTTOM: row Bottom, cols [X, Right)
            hits += ScanSideForSliver(grid, s, i, r.Bottom, true, r.X, r.Right, "BOTTOM");
            // LEFT: col X-1, rows [Y, Bottom)
            hits += ScanSideForSliver(grid, s, i, r.X - 1, false, r.Y, r.Bottom, "LEFT");
            // RIGHT: col Right, rows [Y, Bottom)
            hits += ScanSideForSliver(grid, s, i, r.Right, false, r.Y, r.Bottom, "RIGHT");
        }
        Console.WriteLine($"  total dual-door wall-sliver hits = {hits}");
    }

    static void DoorRunScan(int[,] grid, DungeonSettings s, DungeonGenerator.RoomRect[] rooms)
    {
        int badRuns = 0;
        for (int i = 0; i < rooms.Length; i++)
        {
            var r = rooms[i];
            badRuns += ScanDoorRuns(grid, s, i, r.Y - 1, true, r.X, r.Right, "TOP", r);
            badRuns += ScanDoorRuns(grid, s, i, r.Bottom, true, r.X, r.Right, "BOTTOM", r);
            badRuns += ScanDoorRuns(grid, s, i, r.X - 1, false, r.Y, r.Bottom, "LEFT", r);
            badRuns += ScanDoorRuns(grid, s, i, r.Right, false, r.Y, r.Bottom, "RIGHT", r);
        }
        Console.WriteLine($"  total bad door runs (len > 1) = {badRuns}");
    }

    static int ScanDoorRuns(int[,] grid, DungeonSettings s, int roomIdx,
                            int fixedCoord, bool horizontal, int rangeStart, int rangeEnd,
                            string sideName, DungeonGenerator.RoomRect room)
    {
        int badRuns = 0;
        int runLen = 0;
        int runStart = -1;
        for (int k = rangeStart; k < rangeEnd; k++)
        {
            int x = horizontal ? k : fixedCoord;
            int y = horizontal ? fixedCoord : k;
            bool isDoor = InBounds(s, x, y) && grid[y, x] == DungeonGenerator.CORRIDOR;
            if (isDoor)
            {
                if (runLen == 0) runStart = k;
                runLen++;
                continue;
            }

            badRuns += FlushDoorRun(roomIdx, sideName, horizontal, fixedCoord, runStart, runLen, room);
            runLen = 0;
            runStart = -1;
        }

        badRuns += FlushDoorRun(roomIdx, sideName, horizontal, fixedCoord, runStart, runLen, room);
        return badRuns;
    }

    static int FlushDoorRun(int roomIdx, string sideName, bool horizontal, int fixedCoord,
                            int runStart, int runLen, DungeonGenerator.RoomRect room)
    {
        if (runLen <= 1) return 0;

        int sx = horizontal ? runStart : fixedCoord;
        int sy = horizontal ? fixedCoord : runStart;
        int ex = horizontal ? runStart + runLen - 1 : fixedCoord;
        int ey = horizontal ? fixedCoord : runStart + runLen - 1;
        Console.WriteLine(
            $"  BAD_DOOR_RUN R{roomIdx}.{sideName} rect=(X={room.X},Y={room.Y},W={room.W},H={room.H}) " +
            $"len={runLen} ({sx},{sy})..({ex},{ey})");
        return 1;
    }

    static int ScanSideForSliver(int[,] grid, DungeonSettings s, int roomIdx,
                                  int fixedCoord, bool horizontal, int rangeStart, int rangeEnd, string sideName)
    {
        int hits = 0;
        for (int k = rangeStart; k + 2 < rangeEnd; k++)
        {
            int xA = horizontal ? k     : fixedCoord;
            int yA = horizontal ? fixedCoord : k;
            int xB = horizontal ? k + 1 : fixedCoord;
            int yB = horizontal ? fixedCoord : k + 1;
            int xC = horizontal ? k + 2 : fixedCoord;
            int yC = horizontal ? fixedCoord : k + 2;
            if (!InBounds(s, xA, yA) || !InBounds(s, xB, yB) || !InBounds(s, xC, yC)) continue;
            int a = grid[yA, xA], b = grid[yB, xB], c = grid[yC, xC];
            if (a == DungeonGenerator.CORRIDOR && b == DungeonGenerator.EMPTY && c == DungeonGenerator.CORRIDOR)
            {
                Console.WriteLine($"  SLIVER R{roomIdx}.{sideName} doors=({xA},{yA}) / ({xC},{yC})  wall=({xB},{yB})");
                hits++;
            }
        }
        return hits;
    }

    static void AlongsideCorridorScan(int[,] grid, DungeonSettings s, DungeonGenerator.RoomRect[] rooms)
    {
        int totalRuns = 0;
        for (int i = 0; i < rooms.Length; i++)
        {
            var r = rooms[i];
            // perimeter+1 distance (two cells outside the room rect)
            totalRuns += ScanAlongsideRun(grid, s, i, r.Y - 2, true,  r.X, r.Right, "TOP+1");
            totalRuns += ScanAlongsideRun(grid, s, i, r.Bottom + 1, true,  r.X, r.Right, "BOT+1");
            totalRuns += ScanAlongsideRun(grid, s, i, r.X - 2, false, r.Y, r.Bottom, "LEFT+1");
            totalRuns += ScanAlongsideRun(grid, s, i, r.Right + 1, false, r.Y, r.Bottom, "RIGHT+1");
        }
        Console.WriteLine($"  total alongside corridor runs (>=3 contiguous CORRIDOR cells at perim+1) = {totalRuns}");
    }

    static int ScanAlongsideRun(int[,] grid, DungeonSettings s, int roomIdx,
                                 int fixedCoord, bool horizontal, int rangeStart, int rangeEnd, string sideName)
    {
        int runs = 0;
        int runLen = 0;
        int runStart = -1;
        for (int k = rangeStart; k < rangeEnd; k++)
        {
            int x = horizontal ? k : fixedCoord;
            int y = horizontal ? fixedCoord : k;
            if (!InBounds(s, x, y)) { runLen = 0; runStart = -1; continue; }
            if (grid[y, x] == DungeonGenerator.CORRIDOR)
            {
                if (runLen == 0) runStart = k;
                runLen++;
            }
            else
            {
                if (runLen >= 3)
                {
                    int sxStart = horizontal ? runStart : fixedCoord;
                    int syStart = horizontal ? fixedCoord : runStart;
                    int sxEnd   = horizontal ? runStart + runLen - 1 : fixedCoord;
                    int syEnd   = horizontal ? fixedCoord : runStart + runLen - 1;
                    Console.WriteLine($"  ALONGSIDE R{roomIdx}.{sideName} len={runLen}  ({sxStart},{syStart})..({sxEnd},{syEnd})");
                    runs++;
                }
                runLen = 0; runStart = -1;
            }
        }
        if (runLen >= 3)
        {
            int sxStart = horizontal ? runStart : fixedCoord;
            int syStart = horizontal ? fixedCoord : runStart;
            int sxEnd   = horizontal ? runStart + runLen - 1 : fixedCoord;
            int syEnd   = horizontal ? fixedCoord : runStart + runLen - 1;
            Console.WriteLine($"  ALONGSIDE R{roomIdx}.{sideName} len={runLen}  ({sxStart},{syStart})..({sxEnd},{syEnd})");
            runs++;
        }
        return runs;
    }

    static int PickCentralBottomBigRoom(DungeonGenerator.RoomRect[] rooms, int W, int H)
    {
        int best = -1;
        double bestScore = double.MinValue;
        for (int i = 0; i < rooms.Length; i++)
        {
            var r = rooms[i];
            int cx = r.X + r.W / 2;
            int cy = r.Y + r.H / 2;
            int size = r.W * r.H;
            int distCx = Math.Abs(cx - W / 2);
            int bottomBonus = cy - H / 2;
            double score = size - distCx * 3 + bottomBonus * 2;
            if (cy < H / 2) score -= 200;
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    static bool InBounds(DungeonSettings s, int col, int row)
        => col >= 0 && col < s.MapWidth && row >= 0 && row < s.MapHeight;

    static void DumpAroundRoom(int[,] grid, DungeonGenerator.RoomRect r, int padding, DungeonSettings s)
    {
        int x0 = Math.Max(0, r.X - padding);
        int x1 = Math.Min(s.MapWidth - 1, r.Right + padding - 1);
        int y0 = Math.Max(0, r.Y - padding);
        int y1 = Math.Min(s.MapHeight - 1, r.Bottom + padding - 1);
        Console.WriteLine($"Grid dump [{x0}..{x1}, {y0}..{y1}]   legend: . EMPTY  # ROOM  * CORRIDOR  < STAIR_UP  D DOOR_CLOSED");
        Console.Write("      ");
        for (int x = x0; x <= x1; x++) Console.Write(x.ToString("D2").Substring(0, 1));
        Console.WriteLine();
        Console.Write("      ");
        for (int x = x0; x <= x1; x++) Console.Write(x.ToString("D2").Substring(1, 1));
        Console.WriteLine();
        for (int y = y0; y <= y1; y++)
        {
            Console.Write($"  {y,3}: ");
            for (int x = x0; x <= x1; x++)
            {
                int v = grid[y, x];
                char c = v switch
                {
                    DungeonGenerator.EMPTY => '.',
                    DungeonGenerator.ROOM => '#',
                    DungeonGenerator.CORRIDOR => '*',
                    DungeonGenerator.STAIR_UP => '<',
                    DungeonGenerator.DOOR_CLOSED => 'D',
                    _ => '?',
                };
                Console.Write(c);
            }
            Console.WriteLine();
        }
    }

    static bool[,] WalkableFlood(int[,] grid, DungeonSettings s, int startX, int startY)
    {
        var visited = new bool[s.MapHeight, s.MapWidth];
        if (!InBounds(s, startX, startY)) return visited;
        int v0 = grid[startY, startX];
        if (v0 != DungeonGenerator.ROOM && v0 != DungeonGenerator.CORRIDOR && v0 != DungeonGenerator.STAIR_UP) return visited;
        var q = new Queue<(int x, int y)>();
        q.Enqueue((startX, startY));
        visited[startY, startX] = true;
        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i], ny = y + dy[i];
                if (!InBounds(s, nx, ny) || visited[ny, nx]) continue;
                int v = grid[ny, nx];
                if (v != DungeonGenerator.ROOM && v != DungeonGenerator.CORRIDOR && v != DungeonGenerator.STAIR_UP) continue;
                visited[ny, nx] = true;
                q.Enqueue((nx, ny));
            }
        }
        return visited;
    }

    static void WalkableBFS(int[,] grid, DungeonSettings s, DungeonGenerator.RoomRect[] rooms)
    {
        var start = rooms[0];
        var visited = WalkableFlood(grid, s, start.X + start.W / 2, start.Y + start.H / 2);
        for (int i = 0; i < rooms.Length; i++)
        {
            var r = rooms[i];
            bool reached = false;
            for (int row = r.Y; row < r.Bottom && !reached; row++)
                for (int col = r.X; col < r.Right && !reached; col++)
                    if (visited[row, col]) reached = true;
            Console.WriteLine($"  R{i,2}: reachable from R0 = {reached}");
        }

        int stairCount = 0;
        int reachableStairs = 0;
        for (int y = 0; y < s.MapHeight; y++)
            for (int x = 0; x < s.MapWidth; x++)
            {
                if (grid[y, x] != DungeonGenerator.STAIR_UP) continue;
                stairCount++;
                if (visited[y, x]) reachableStairs++;
            }
        Console.WriteLine($"  stair reachable = {reachableStairs}/{stairCount}");
    }

    static void DetachedCorridorScan(int[,] grid, DungeonSettings s, DungeonGenerator.RoomRect[] rooms)
    {
        var start = rooms[0];
        var visited = WalkableFlood(grid, s, start.X + start.W / 2, start.Y + start.H / 2);
        int detached = 0;
        for (int y = 0; y < s.MapHeight; y++)
            for (int x = 0; x < s.MapWidth; x++)
            {
                if (grid[y, x] != DungeonGenerator.CORRIDOR) continue;
                if (visited[y, x]) continue;
                Console.WriteLine($"  DETACHED CORRIDOR cell=({x},{y})");
                detached++;
            }
        Console.WriteLine($"  total detached corridor cells = {detached}");
    }

    static void PhantomPerimeterScan(int[,] grid, DungeonSettings s, DungeonGenerator.RoomRect[] rooms)
    {
        int phantom = 0;
        int[] dxs = { 0, 0, 1, -1 };
        int[] dys = { 1, -1, 0, 0 };
        for (int i = 0; i < rooms.Length; i++)
        {
            var r = rooms[i];
            for (int col = r.X; col < r.Right; col++)
            {
                CheckPhantom(grid, s, r, i, col, r.Y - 1, dxs, dys, "TOP", ref phantom);
                CheckPhantom(grid, s, r, i, col, r.Bottom, dxs, dys, "BOTTOM", ref phantom);
            }
            for (int row = r.Y; row < r.Bottom; row++)
            {
                CheckPhantom(grid, s, r, i, r.X - 1, row, dxs, dys, "LEFT", ref phantom);
                CheckPhantom(grid, s, r, i, r.Right, row, dxs, dys, "RIGHT", ref phantom);
            }
        }
        Console.WriteLine($"  total phantom perimeter cells = {phantom}");
    }

    static void OrphanDoorStubScan(int[,] grid, DungeonSettings s, DungeonGenerator.RoomRect[] rooms)
    {
        int orphan = 0;
        int outwardNotCorridor = 0;
        for (int i = 0; i < rooms.Length; i++)
        {
            var r = rooms[i];
            for (int col = r.X; col < r.Right; col++)
            {
                CheckOrphanDoor(grid, s, i, col, r.Y - 1, 0, -1, "TOP", ref orphan, ref outwardNotCorridor);
                CheckOrphanDoor(grid, s, i, col, r.Bottom, 0, 1, "BOTTOM", ref orphan, ref outwardNotCorridor);
            }
            for (int row = r.Y; row < r.Bottom; row++)
            {
                CheckOrphanDoor(grid, s, i, r.X - 1, row, -1, 0, "LEFT", ref orphan, ref outwardNotCorridor);
                CheckOrphanDoor(grid, s, i, r.Right, row, 1, 0, "RIGHT", ref orphan, ref outwardNotCorridor);
            }
        }

        Console.WriteLine($"  total orphan door/stub candidates = {orphan}");
        Console.WriteLine($"  total perimeter corridor cells with outward neighbor not CORRIDOR = {outwardNotCorridor}");
    }

    static void CheckOrphanDoor(int[,] grid, DungeonSettings s, int roomIdx,
                                int x, int y, int outDx, int outDy, string side,
                                ref int orphan, ref int outwardNotCorridor)
    {
        if (!InBounds(s, x, y)) return;
        if (grid[y, x] != DungeonGenerator.CORRIDOR) return;

        int ox = x + outDx;
        int oy = y + outDy;
        bool outwardCorridor = InBounds(s, ox, oy) && grid[oy, ox] == DungeonGenerator.CORRIDOR;
        if (outwardCorridor) return;

        outwardNotCorridor++;
        orphan++;
        string outward = InBounds(s, ox, oy) ? grid[oy, ox].ToString() : "OOB";
        Console.WriteLine($"  ORPHAN_DOOR R{roomIdx}.{side} cell=({x},{y}) outward=({ox},{oy}) value={outward}");
    }

    static void CheckPhantom(int[,] grid, DungeonSettings s, DungeonGenerator.RoomRect r, int idx,
                              int x, int y, int[] dxs, int[] dys, string side, ref int phantom)
    {
        if (!InBounds(s, x, y)) return;
        if (grid[y, x] != DungeonGenerator.CORRIDOR) return;

        bool hasRoomNeighbor = false;
        for (int k = 0; k < 4; k++)
        {
            int nx = x + dxs[k], ny = y + dys[k];
            if (!InBounds(s, nx, ny)) continue;
            if (grid[ny, nx] != DungeonGenerator.ROOM) continue;
            if (!r.Contains(nx, ny)) continue;
            hasRoomNeighbor = true;
            break;
        }

        if (!hasRoomNeighbor)
        {
            Console.WriteLine($"  PHANTOM cell=({x},{y}) on R{idx}.{side}  (no 4-conn ROOM cell of R{idx})");
            phantom++;
        }
    }
}
