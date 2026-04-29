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
    private RoomInfo? _activeRoom;

    private void OnEnable()
    {
        if (eventChannel != null)
            eventChannel.OnRoomEntered += OnRoomEntered;
    }

    private void OnDisable()
    {
        if (eventChannel != null)
            eventChannel.OnRoomEntered -= OnRoomEntered;

        UnsubscribeActiveEnemies();
    }

    private void OnRoomEntered(RoomEnteredEventArgs args)
    {
        if (!args.IsFirstVisit) return;
        if (args.Room.Type != RoomType.Normal && args.Room.Type != RoomType.MonsterDen) return;

        SpawnRoom(args.Room);
    }

    private void SpawnRoom(RoomInfo room)
    {
        var dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null || dungeonManager.Data == null) return;
        if (EnemyPoolManager.Instance == null)
        {
            Debug.LogWarning("[RoomSpawner] EnemyPoolManager.Instance is missing.");
            return;
        }

        List<Vector2Int> walkableTiles = dungeonManager.Data.GetWalkableTiles(room);
        FilterUnsafeSpawnTiles(walkableTiles, room, dungeonManager);
        if (walkableTiles.Count == 0) return;

        Shuffle(walkableTiles);

        BeginRoomSpawnTracking(room);

        float budget = CalculateBudget(room);
        SpawnRegion region = dungeonManager.Data.currentStageRegion;
        int tileIndex = 0;

        while (budget > 0f && tileIndex < walkableTiles.Count)
        {
            BuildCandidates(region, budget);
            if (_candidates.Count == 0)
            {
                // 예산이 남아도 현재 지역/비용 조건을 만족하는 적이 없으면 루프를 종료한다.
                break;
            }

            EnemyData selected = _candidates[Random.Range(0, _candidates.Count)];
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
            TrackEnemy(enemy);

            budget -= selected.spawnCost;
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

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
