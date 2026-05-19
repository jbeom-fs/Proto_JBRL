using System.Collections.Generic;
using UnityEngine;

public class RoomSpawner : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private DungeonEventChannel eventChannel;
    [SerializeField] private EnemyData[] enemyTable;

    [Header("Budget")]
    [SerializeField] private float densityFactor = 0.1f;
    [SerializeField] private float normalMultiplier = 1.0f;
    [SerializeField] private float monsterDenMultiplier = 2.5f;

    [Header("Spawn Safety")]
    [SerializeField] private float spawnFootprintRadius = 0.34f;

    private readonly List<EnemyData> _candidates = new();
    private readonly List<EnemyData> _poolEnemyTable = new();
    private readonly List<GameObject> activeEnemies = new();
    private readonly HashSet<int> _startedRoomKeys = new();
    private RoomInfo? _activeRoom;
    private RoomInfo? _pendingRoomStart;
    private EliteKeyPlan _eliteKeyPlan;

    private struct EliteKeyPlan
    {
        public bool Active;
        public int RoomKey;
        public int SpawnIndexInRoom;
    }

    private void OnEnable()
    {
        if (eventChannel != null)
        {
            eventChannel.OnRoomEntered += OnRoomEntered;
            eventChannel.OnFloorChanged += OnFloorChanged;
        }
    }

    private void OnDisable()
    {
        if (eventChannel != null)
        {
            eventChannel.OnRoomEntered -= OnRoomEntered;
            eventChannel.OnFloorChanged -= OnFloorChanged;
        }

        ClearPendingRoomStart();
        UnsubscribeActiveEnemies();
    }

    private void LateUpdate()
    {
        if (!_pendingRoomStart.HasValue) return;
        if (!CanStartRoomEncounter(_pendingRoomStart.Value)) return;

        RoomInfo room = _pendingRoomStart.Value;
        StartRoomEncounter(room);
    }

    private void OnRoomEntered(RoomEnteredEventArgs args)
    {
        if (!args.IsFirstVisit) return;
        if (args.Room.IsElite) return;
        if (args.Room.Type != RoomType.Normal && args.Room.Type != RoomType.MonsterDen) return;
        if (_startedRoomKeys.Contains(GetRoomKey(args.Room))) return;

        if (!CanStartRoomEncounter(args.Room))
        {
            SetPendingRoomStart(args.Room);
            return;
        }

        StartRoomEncounter(args.Room);
    }

    public void ClearPendingRoomStart()
    {
        _pendingRoomStart = null;
    }

    public void ResetRoomEncounterState()
    {
        ClearPendingRoomStart();
        _startedRoomKeys.Clear();
    }

    public void ClearRuntimeEncounterState()
    {
        ClearPendingRoomStart();
        UnsubscribeActiveEnemies();
        activeEnemies.Clear();
        _activeRoom = null;
        _eliteKeyPlan = default;
    }

    public void PrepareEliteKeyPlan(DungeonManager dungeonManager)
    {
        _eliteKeyPlan = default;
        if (dungeonManager == null || dungeonManager.Data == null)
            return;

        DungeonData data = dungeonManager.Data;
        if (!data.HasEliteRoom)
            return;

        var candidates = new List<(int roomKey, int spawnIndexInRoom)>();
        for (int i = 0; i < data.RoomCount; i++)
        {
            RoomInfo room = dungeonManager.Registry != null
                ? dungeonManager.Registry.Resolve(data.GetRoom(i))
                : data.GetRoom(i);

            if (room.IsElite || room.Type != RoomType.Normal && room.Type != RoomType.MonsterDen)
                continue;

            int spawnCount = CountDeterministicSpawns(room, dungeonManager);
            for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex++)
                candidates.Add((GetRoomKey(room), spawnIndex));
        }

        if (candidates.Count == 0)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[RoomSpawner] Elite floor has no enemy spawn candidates for Elite Key.", this);
#endif
            return;
        }

        int seed = DeterministicSeedUtility.CreateSeed(
            dungeonManager.seed,
            (int)data.currentStageRegion,
            dungeonManager.floor,
            data.GetRoom(data.EliteRoomIndex).StableRoomKey,
            DeterministicSeedUtility.EliteKeyDomain);
        var rng = new System.Random(seed);
        var selected = candidates[rng.Next(candidates.Count)];
        _eliteKeyPlan = new EliteKeyPlan
        {
            Active = true,
            RoomKey = selected.roomKey,
            SpawnIndexInRoom = selected.spawnIndexInRoom,
        };
    }

    private void SetPendingRoomStart(RoomInfo room)
    {
        if (_startedRoomKeys.Contains(GetRoomKey(room)))
            return;

        if (_pendingRoomStart.HasValue && IsSameRoom(_pendingRoomStart.Value, room))
            return;

        _pendingRoomStart = room;
    }

    private bool CanStartRoomEncounter(RoomInfo room)
    {
        DungeonTilemapRenderer renderer = DungeonManager.Instance != null
            ? DungeonManager.Instance.dungeonRenderer
            : null;

        return renderer == null || renderer.CanStartRoomEncounter(room);
    }

    private void OnFloorChanged(int prevFloor, int newFloor)
    {
        ResetRoomEncounterState();
    }

    private void StartRoomEncounter(RoomInfo room)
    {
        ClearPendingRoomStart();
        _startedRoomKeys.Add(GetRoomKey(room));
        SpawnRoom(room);
    }

    private void SpawnRoom(RoomInfo room)
    {
        var dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null || dungeonManager.Data == null) return;
        if (room.IsElite) return;
        if (EnemyPoolManager.Instance == null)
        {
            Debug.LogWarning("[RoomSpawner] EnemyPoolManager.Instance is missing.");
            return;
        }

        List<Vector2Int> walkableTiles = dungeonManager.Data.GetWalkableTiles(room);
        FilterUnsafeSpawnTiles(walkableTiles, room, dungeonManager);
        if (walkableTiles.Count == 0) return;

        SortSpawnTiles(walkableTiles);

        SpawnRegion region = dungeonManager.Data.currentStageRegion;
        int roomSeed = DeterministicSeedUtility.CreateSeed(
            dungeonManager.seed,
            (int)region,
            dungeonManager.floor,
            room.StableRoomKey,
            DeterministicSeedUtility.EnemySpawnDomain);
        var roomRng = new System.Random(roomSeed);
        Shuffle(walkableTiles, roomRng);

        BeginRoomSpawnTracking(room);

        float budget = CalculateBudget(room);
        int tileIndex = 0;
        int spawnedIndex = 0;

        while (budget > 0f && tileIndex < walkableTiles.Count)
        {
            BuildCandidates(region, budget);
            if (_candidates.Count == 0)
            {
                // 예산이 남아도 현재 지역/비용 조건을 만족하는 적이 없으면 루프를 종료한다.
                break;
            }

            EnemyData selected = _candidates[roomRng.Next(_candidates.Count)];
            EnemyController enemy = EnemyPoolManager.Instance.Request(selected);
            if (enemy == null)
            {
                budget -= Mathf.Max(1, selected.spawnCost);
                continue;
            }

            Vector2Int tile = walkableTiles[tileIndex++];
            enemy.transform.position = dungeonManager.GridToWorld(tile);
            enemy.transform.SetParent(null);
            enemy.Initialize(selected);
            if (ShouldAssignEliteKey(room, spawnedIndex))
                enemy.MarkAsEliteKeyHolder();
            TrackEnemy(enemy);

            budget -= selected.spawnCost;
            spawnedIndex++;
        }

        if (activeEnemies.Count > 0)
        {
            dungeonManager.CloseCurrentRoomDoors(room);
        }
        else
        {
            _activeRoom = null;
            dungeonManager.OpenCurrentRoomDoors();
        }
    }

    private int CountDeterministicSpawns(RoomInfo room, DungeonManager dungeonManager)
    {
        List<Vector2Int> walkableTiles = dungeonManager.Data.GetWalkableTiles(room);
        FilterUnsafeSpawnTiles(walkableTiles, room, dungeonManager);
        if (walkableTiles.Count == 0)
            return 0;

        SortSpawnTiles(walkableTiles);

        SpawnRegion region = dungeonManager.Data.currentStageRegion;
        int roomSeed = DeterministicSeedUtility.CreateSeed(
            dungeonManager.seed,
            (int)region,
            dungeonManager.floor,
            room.StableRoomKey,
            DeterministicSeedUtility.EnemySpawnDomain);
        var roomRng = new System.Random(roomSeed);
        Shuffle(walkableTiles, roomRng);

        float budget = CalculateBudget(room);
        int tileIndex = 0;
        int spawnCount = 0;
        while (budget > 0f && tileIndex < walkableTiles.Count)
        {
            BuildCandidates(region, budget);
            if (_candidates.Count == 0)
                break;

            EnemyData selected = _candidates[roomRng.Next(_candidates.Count)];
            budget -= selected.spawnCost;
            tileIndex++;
            spawnCount++;
        }

        return spawnCount;
    }

    private bool ShouldAssignEliteKey(RoomInfo room, int spawnedIndex)
    {
        return _eliteKeyPlan.Active &&
               _eliteKeyPlan.RoomKey == GetRoomKey(room) &&
               _eliteKeyPlan.SpawnIndexInRoom == spawnedIndex;
    }

    private void BeginRoomSpawnTracking(RoomInfo room)
    {
        // 방마다 살아 있는 적 목록을 새로 관리합니다.
        // 적 사망 이벤트를 구독하는 Observer 방식이라 Update에서 계속 폴링하지 않아도 됩니다.
        UnsubscribeActiveEnemies();
        activeEnemies.Clear();
        _activeRoom = room;
    }

    private void TrackEnemy(EnemyController enemy)
    {
        if (enemy == null) return;

        activeEnemies.Add(enemy.gameObject);
        enemy.OnDied -= OnSpawnedEnemyDied;
        enemy.OnDied += OnSpawnedEnemyDied;
    }

    private void OnSpawnedEnemyDied(EnemyController enemy)
    {
        if (enemy == null) return;

        enemy.OnDied -= OnSpawnedEnemyDied;
        activeEnemies.Remove(enemy.gameObject);
        CheckRoomClear();
    }

    private void CheckRoomClear()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            GameObject enemyObject = activeEnemies[i];
            if (enemyObject == null || !enemyObject.activeInHierarchy)
                activeEnemies.RemoveAt(i);
        }

        if (activeEnemies.Count > 0) return;
        if (!_activeRoom.HasValue) return;

        // 추적 중인 방의 모든 적이 사라졌을 때만 현재 닫힌 문을 엽니다.
        // 실제 문 타일 제어는 DungeonManager.Instance가 담당해 DoorController와 책임이 겹치지 않습니다.
        _activeRoom = null;
        DungeonManager.Instance?.OpenCurrentRoomDoors();
    }

    private void UnsubscribeActiveEnemies()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            GameObject enemyObject = activeEnemies[i];
            if (enemyObject == null) continue;

            if (enemyObject.TryGetComponent<EnemyController>(out var enemy))
                enemy.OnDied -= OnSpawnedEnemyDied;
        }
    }

    private static bool IsSameRoom(RoomInfo a, RoomInfo b)
    {
        return a.StableRoomKey == b.StableRoomKey &&
               a.X == b.X && a.Y == b.Y && a.Right == b.Right && a.Bottom == b.Bottom;
    }

    private static int GetRoomKey(RoomInfo room)
    {
        return room.StableRoomKey;
    }

    private float CalculateBudget(RoomInfo room)
    {
        int width = Mathf.Max(0, room.Right - room.X);
        int height = Mathf.Max(0, room.Bottom - room.Y);

        // 방 면적에 밀도 계수를 곱해 기본 예산을 만들고, 방 타입별 배율을 적용한다.
        float baseBudget = width * height * densityFactor;
        float multiplier = room.Type == RoomType.MonsterDen ? monsterDenMultiplier : normalMultiplier;
        return baseBudget * multiplier;
    }

    private void BuildCandidates(SpawnRegion region, float budget)
    {
        _candidates.Clear();

        IList<EnemyData> source = enemyTable;
        bool hasSerializedTable = source != null && source.Count > 0;
        if (!hasSerializedTable && EnemyPoolManager.Instance != null)
        {
            // 인스펙터 enemyTable이 비어 있으면 풀에 등록된 EnemyData를 후보 테이블로 사용한다.
            EnemyPoolManager.Instance.GetRegisteredEnemyData(_poolEnemyTable);
            _poolEnemyTable.Sort(CompareEnemyDataDeterministic);
            source = _poolEnemyTable;
        }

        if (source == null) return;

        foreach (EnemyData enemy in source)
        {
            if (enemy == null) continue;

            // 비트 플래그 필터: 적의 허용 지역과 현재 스테이지 지역이 하나라도 겹쳐야 스폰 가능하다.
            bool regionMatches = (enemy.allowedRegions & region) != 0;
            if (!regionMatches) continue;
            if (enemy.spawnCost <= 0 || enemy.spawnCost > budget) continue;

            _candidates.Add(enemy);
        }
    }

    private void FilterUnsafeSpawnTiles(List<Vector2Int> tiles, RoomInfo room, DungeonManager dungeonManager)
    {
        for (int i = tiles.Count - 1; i >= 0; i--)
        {
            if (!IsSafeSpawnTile(tiles[i], room, dungeonManager))
                tiles.RemoveAt(i);
        }
    }

    private bool IsSafeSpawnTile(Vector2Int tile, RoomInfo room, DungeonManager dungeonManager)
    {
        // 방 테두리 타일은 벽/닫힌 문과 너무 가까워 실제 CircleCollider가 걸릴 수 있으므로 스폰 후보에서 제외합니다.
        if (tile.x <= room.X || tile.x >= room.Right - 1 ||
            tile.y <= room.Y || tile.y >= room.Bottom - 1)
        {
            return false;
        }

        Vector3 world = dungeonManager.GridToWorld(tile);
        float radius = Mathf.Max(0.01f, spawnFootprintRadius);

        Vector3 c0 = new Vector3(world.x - radius, world.y - radius, 0f);
        Vector3 c1 = new Vector3(world.x + radius, world.y - radius, 0f);
        Vector3 c2 = new Vector3(world.x - radius, world.y + radius, 0f);
        Vector3 c3 = new Vector3(world.x + radius, world.y + radius, 0f);

        // 스폰 전에 실제 콜라이더 발자국 네 모서리가 모두 walkable인지 검사해 벽 끼임을 예방합니다.
        return IsWorldPointWalkable(c0, dungeonManager) &&
               IsWorldPointWalkable(c1, dungeonManager) &&
               IsWorldPointWalkable(c2, dungeonManager) &&
               IsWorldPointWalkable(c3, dungeonManager);
    }

    private static bool IsWorldPointWalkable(Vector3 world, DungeonManager dungeonManager)
    {
        Vector2Int grid = dungeonManager.WorldToGrid(world);
        return dungeonManager.IsWalkable(grid.x, grid.y);
    }

    private static void SortSpawnTiles(List<Vector2Int> tiles)
    {
        tiles.Sort(CompareTileByRowThenColumn);
    }

    private static int CompareTileByRowThenColumn(Vector2Int a, Vector2Int b)
    {
        int y = a.y.CompareTo(b.y);
        return y != 0 ? y : a.x.CompareTo(b.x);
    }

    private static int CompareEnemyDataDeterministic(EnemyData a, EnemyData b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return 1;
        if (b == null) return -1;

        int nameCompare = string.CompareOrdinal(a.enemyName, b.enemyName);
        if (nameCompare != 0) return nameCompare;

        return string.CompareOrdinal(a.name, b.name);
    }

    private static void Shuffle<T>(IList<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
