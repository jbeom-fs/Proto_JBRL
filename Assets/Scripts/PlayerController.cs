// ═══════════════════════════════════════════════════════════════════
//  PlayerController.cs
//  Application Layer — 플레이어 입력·이동·방 감지
//
//  책임:
//    • 실시간 이동 + 그리드 충돌
//    • 방 진입 감지 → EventChannel 통해 이벤트 발행
//    • 계단 상호작용 → DungeonManager.NextFloor()
//
//  알지 말아야 할 것:
//    • 문 개폐 로직 (DoorController 담당)
//    • Tilemap (DungeonTilemapRenderer 담당)
//    • 던전 생성 알고리즘
// ═══════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(PlayerInputReader))]
public class PlayerController : MonoBehaviour
{
    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("Dependencies")]
    [Tooltip("던전 관리자 — 이동 가능 여부·방 쿼리 제공")]
    private DungeonManager dungeonManager => DungeonManager.Instance;

    [Tooltip("이벤트 채널 — 방 진입 이벤트 발행")]
    public DungeonEventChannel eventChannel;

    [Tooltip("문 컨트롤러 — F10키로 문 열기 호출")]
    public DoorController doorController;

    [Header("Movement")]
    [Tooltip("초당 이동 속도 (타일 단위)")]
    public float moveSpeed = 5f;

    [Header("Collision")]
    [Tooltip("충돌 반지름 비율 (0.05 ~ 0.49). 작을수록 좁은 통로 통과 쉬움.")]
    [Range(0.05f, 0.49f)]
    public float collisionRadius = 0.2f;

    private const int ROOM_ENTRY_SAMPLE_THRESHOLD = 3;

    // ── 내부 상태 ─────────────────────────────────────────────────────
    private PlayerInputReader _inputReader;
    private float      _tileSize;
    private float      _stairCooldown;
    private const float STAIR_COOLDOWN = 0.5f;

    /// <summary>마지막 이동 입력 방향 (그리드 단위). PlayerCombatController가 공격 방향으로 사용.</summary>
    public Vector2Int FacingDirection { get; private set; } = Vector2Int.down;

    private RoomInfo? _currentRoom;

    private readonly HashSet<(int x, int y)> _visitedRooms
        = new HashSet<(int x, int y)>();

    // CanMoveTo 코너 배열 재사용
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly Vector3[] _roomEntrySamples = new Vector3[9];

    // 마지막으로 방문안 Room
    // Room이 변경되거나 복도로 나가기 전까진 체크하지 않는다.
    private RoomInfo? _lastRoom;
    private Rigidbody2D _rb;
    private CircleCollider2D _circleCollider;
    private Vector3 _lastSafePosition;
    private static PhysicsMaterial2D s_NoFrictionMaterial;

    // ══════════════════════════════════════════════════════════════
    //  초기화
    // ══════════════════════════════════════════════════════════════

    private void Start()
    {
        ConfigurePhysics();

        _inputReader = GetComponent<PlayerInputReader>();
        if (_inputReader == null) { Debug.LogError("[PlayerController] PlayerInputReader 없음"); enabled = false; return; }

        if (dungeonManager == null) { Debug.LogError("[PlayerController] DungeonManager 없음"); enabled = false; return; }
        if (eventChannel   == null) { Debug.LogError("[PlayerController] EventChannel 없음");  enabled = false; return; }
        if (dungeonManager.dungeonRenderer == null) { Debug.LogError("[PlayerController] DungeonTilemapRenderer 없음"); enabled = false; return; }
        if (dungeonManager.dungeonRenderer.tilemap == null) { Debug.LogError("[PlayerController] DungeonTilemapRenderer.tilemap 없음"); enabled = false; return; }
        if (doorController == null)
            Debug.LogWarning("[PlayerController] DoorController 없음 — 문 열기 입력이 동작하지 않습니다.");

        if (dungeonManager.Data == null)
            dungeonManager.Generate();

        _tileSize = dungeonManager.dungeonRenderer.tilemap.cellSize.x;

        // 층 변경 완료(던전 생성 후) 시 스폰 → 이벤트로 타이밍 보장
        eventChannel.OnFloorChanged += OnFloorChangedHandler;

        SpawnAtStart();
    }

    private void OnDestroy()
    {
        if (eventChannel != null)
            eventChannel.OnFloorChanged -= OnFloorChangedHandler;
    }

    private void ConfigurePhysics()
    {
        // 플레이어도 적과 물리적으로 겹치지 않도록 Dynamic Rigidbody2D와 작은 원형 콜라이더를 보장합니다.
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.sharedMaterial = GetNoFrictionMaterial();
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        _rb = rb;

        CircleCollider2D circle = GetComponent<CircleCollider2D>();
        if (circle == null)
            circle = gameObject.AddComponent<CircleCollider2D>();
        circle.isTrigger = false;
        circle.radius = 0.32f;
        circle.offset = Vector2.zero;
        circle.sharedMaterial = GetNoFrictionMaterial();
        _circleCollider = circle;

        foreach (BoxCollider2D box in GetComponents<BoxCollider2D>())
            box.enabled = false;

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            gameObject.layer = playerLayer;
    }

    private static PhysicsMaterial2D GetNoFrictionMaterial()
    {
        if (s_NoFrictionMaterial != null) return s_NoFrictionMaterial;

        s_NoFrictionMaterial = new PhysicsMaterial2D("NoFriction")
        {
            friction = 0f,
            bounciness = 0f
        };
        return s_NoFrictionMaterial;
    }

    /// <summary>
    /// 층 변경 완료 이벤트 핸들러.
    /// FloorTransition 코루틴 안에서 Generate() 이후에 발행되므로
    /// 새 던전 데이터가 보장된 상태에서 스폰합니다.
    /// </summary>
    private void OnFloorChangedHandler(int prevFloor, int newFloor)
    {
        SpawnAtStart();
        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("player_spawned",
                "prevFloor=" + prevFloor + " newFloor=" + newFloor +
                " position=" + transform.position.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                ":" + transform.position.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
#if UNITY_EDITOR
        Debug.Log($"[Player] {prevFloor}층 → {newFloor}층, 스폰 완료");
#endif
    }

    /// <summary>스폰 위치에 플레이어를 배치하고 상태를 초기화합니다.</summary>
    public void SpawnAtStart()
    {
        Vector2Int gridPos = dungeonManager.GetSpawnTilePos();
        transform.position = dungeonManager.GridToWorld(gridPos);
        _lastSafePosition = transform.position;
        if (_rb != null)
            _rb.linearVelocity = Vector2.zero;
        _stairCooldown          = STAIR_COOLDOWN;
        _currentRoom            = null;
        _visitedRooms.Clear();
        _lastRoom               = null;

        var spawnRoom = dungeonManager.GetRoomAt(gridPos.x, gridPos.y);
        if (spawnRoom.HasValue)
            dungeonManager.SetRoomType(spawnRoom.Value, RoomType.Spawn);
    }

    // ══════════════════════════════════════════════════════════════
    //  매 프레임 처리
    // ══════════════════════════════════════════════════════════════

    private void Update()
    {
        _stairCooldown -= Time.deltaTime;

        if (dungeonManager != null && dungeonManager.IsTransitioning)
            return;

        if (_inputReader == null) return;

        if (_inputReader.WasStairPressed && _stairCooldown <= 0f)
        {
            TryInteractStair();
            return;
        }

        // f10키: 문 열기 — 입력 감지는 PlayerInputReader, 실행은 DoorController
        if (_inputReader.WasOpenDoorPressed)
        {
            doorController?.OpenAllDoors();
            return;
        }

        Vector2 input = _inputReader.MoveInput;
        if (input != Vector2.zero)
        {
            // 대각선 입력 시 X 축 우선으로 facing 결정
            FacingDirection = input.x != 0f
                ? new Vector2Int((int)Mathf.Sign(input.x), 0)
                : new Vector2Int(0, (int)Mathf.Sign(input.y));

            if (input.x != 0f && input.y != 0f)
                input = input.normalized;
            MoveWithCollision(input);
        }

        CheckRoomEntry();
    }

    private void LateUpdate()
    {
        if (dungeonManager == null || dungeonManager.Data == null) return;

        if (CanMoveTo(transform.position))
        {
            _lastSafePosition = transform.position;
            return;
        }

        // 유닛끼리 Rigidbody 충돌로 밀렸더라도 벽/닫힌 문 타일 안에 들어가면 마지막 안전 위치로 되돌립니다.
        // 이동 로직의 그리드 충돌 체크와 물리 충돌 해소 사이의 빈틈을 막는 최종 안전장치입니다.
        transform.position = _lastSafePosition;
        if (_rb != null)
            _rb.linearVelocity = Vector2.zero;
    }

    // ══════════════════════════════════════════════════════════════
    //  이동
    // ══════════════════════════════════════════════════════════════

    private void MoveWithCollision(Vector2 input)
    {
        float   step   = moveSpeed * Time.deltaTime;
        Vector3 origin = transform.position;
        Vector3 current = origin;
        bool movedX = false;
        bool movedY = false;
        Vector3 xMove = new Vector3(input.x * step, 0f, 0f);
        Vector3 yMove = new Vector3(0f, input.y * step, 0f);

        if (input.x != 0f)
        {
            Vector3 next = current + xMove;
            if (CanMoveTo(next))
            {
                current = next;
                movedX = true;
            }
        }

        if (input.y != 0f)
        {
            Vector3 next = current + yMove;
            if (CanMoveTo(next))
            {
                current = next;
                movedY = true;
            }
        }

        bool isDiagonalInput = input.x != 0f && input.y != 0f;
        if (isDiagonalInput && !movedX && !movedY)
        {
            if (TrySlideWithNudge(origin, yMove, -Mathf.Sign(input.x) * Vector3.right, out Vector3 ySlide))
                current = ySlide;
            else if (TrySlideWithNudge(origin, xMove, -Mathf.Sign(input.y) * Vector3.up, out Vector3 xSlide))
                current = xSlide;
        }

        transform.position = current;
    }

    private bool TrySlideWithNudge(Vector3 origin, Vector3 primaryMove, Vector3 nudgeDirection, out Vector3 result)
    {
        result = origin;

        if (primaryMove.sqrMagnitude <= 0.000001f || nudgeDirection.sqrMagnitude <= 0.000001f)
            return false;

        float nudgeStep = Mathf.Max(0.001f, _tileSize * 0.05f);
        float maxNudgeDistance = Mathf.Max(nudgeStep, _tileSize * 0.45f);
        int maxAttempts = Mathf.Min(10, Mathf.CeilToInt(maxNudgeDistance / nudgeStep));

        for (int i = 1; i <= maxAttempts; i++)
        {
            float nudgeDistance = Mathf.Min(nudgeStep * i, maxNudgeDistance);
            Vector3 candidate = origin + primaryMove + nudgeDirection * nudgeDistance;
            if (!CanMoveTo(candidate)) continue;

            result = candidate;
            return true;
        }

        return false;
    }

    private bool CanMoveTo(Vector3 pos)
    {
        float r = _tileSize * collisionRadius;
        _corners[0] = new Vector3(pos.x - r, pos.y - r, 0);
        _corners[1] = new Vector3(pos.x + r, pos.y - r, 0);
        _corners[2] = new Vector3(pos.x - r, pos.y + r, 0);
        _corners[3] = new Vector3(pos.x + r, pos.y + r, 0);

        foreach (var c in _corners)
        {
            var g = dungeonManager.WorldToGrid(c);
            if (!dungeonManager.IsWalkable(g.x, g.y)) return false;
        }
        return true;
    }

    private bool IsPhysicsFootprintWalkable(Vector3 pos)
    {
        float r = GetWorldColliderRadius();
        _corners[0] = new Vector3(pos.x - r, pos.y - r, 0f);
        _corners[1] = new Vector3(pos.x + r, pos.y - r, 0f);
        _corners[2] = new Vector3(pos.x - r, pos.y + r, 0f);
        _corners[3] = new Vector3(pos.x + r, pos.y + r, 0f);

        for (int i = 0; i < _corners.Length; i++)
        {
            Vector2Int grid = dungeonManager.WorldToGrid(_corners[i]);
            if (!dungeonManager.IsWalkable(grid.x, grid.y))
                return false;
        }

        return true;
    }

    private float GetWorldColliderRadius()
    {
        if (_circleCollider == null)
            return Mathf.Max(0.01f, _tileSize * collisionRadius);

        float maxScale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y));

        return Mathf.Max(0.01f, _circleCollider.radius * maxScale);
    }

    // ══════════════════════════════════════════════════════════════
    //  방 진입 감지
    // ══════════════════════════════════════════════════════════════

    private void CheckRoomEntry()
    {
        Vector2Int centerGrid = dungeonManager.WorldToGrid(transform.position);
        if (!TryResolveRoomEntry(out RoomInfo room, out Vector2Int gridPos))
        {
            if (!dungeonManager.IsCorr(centerGrid.x, centerGrid.y))
                _currentRoom = null;

            _lastRoom = null;
            return;
        }

        if(room.Equals(_lastRoom)) return;
        _lastRoom = room;

        bool isNewEntry = !_currentRoom.HasValue ||
                          _currentRoom.Value.X != room.X ||
                          _currentRoom.Value.Y != room.Y;
        if (!isNewEntry) return;

        _currentRoom = room;

        var key = (room.X, room.Y);
        bool isFirstVisit = !_visitedRooms.Contains(key);
        if (isFirstVisit && room.Type == RoomType.Normal)
            _visitedRooms.Add(key);

        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("room_entered",
                "room=" + room.X + ":" + room.Y +
                " type=" + room.Type +
                " firstVisit=" + isFirstVisit +
                " grid=" + gridPos.x + ":" + gridPos.y);
        eventChannel.RaiseRoomEntered(room, isFirstVisit);
        
    }

    private bool TryResolveRoomEntry(out RoomInfo room, out Vector2Int gridPos)
    {
        Vector3 center = transform.position;
        float radius = Mathf.Min(GetWorldColliderRadius(), Mathf.Max(0.01f, _tileSize * 0.49f));

        _roomEntrySamples[0] = center;
        _roomEntrySamples[1] = center + new Vector3(-radius, 0f, 0f);
        _roomEntrySamples[2] = center + new Vector3(radius, 0f, 0f);
        _roomEntrySamples[3] = center + new Vector3(0f, radius, 0f);
        _roomEntrySamples[4] = center + new Vector3(0f, -radius, 0f);
        _roomEntrySamples[5] = center + new Vector3(-radius, -radius, 0f);
        _roomEntrySamples[6] = center + new Vector3(radius, -radius, 0f);
        _roomEntrySamples[7] = center + new Vector3(-radius, radius, 0f);
        _roomEntrySamples[8] = center + new Vector3(radius, radius, 0f);

        Vector2Int centerGrid = dungeonManager.WorldToGrid(_roomEntrySamples[0]);
        RoomInfo? centerRoom = dungeonManager.GetRoomAt(centerGrid.x, centerGrid.y);
        if (centerRoom.HasValue && IsRoomEntryTile(centerRoom.Value, centerGrid))
        {
            room = centerRoom.Value;
            gridPos = centerGrid;
            return true;
        }

        RoomInfo candidateEntryRoom = default;
        Vector2Int candidateEntryGrid = default;
        bool hasCandidateEntry = false;
        int roomSampleCount = 0;

        for (int i = 1; i < _roomEntrySamples.Length; i++)
        {
            Vector2Int candidateGrid = dungeonManager.WorldToGrid(_roomEntrySamples[i]);
            RoomInfo? candidateRoom = dungeonManager.GetRoomAt(candidateGrid.x, candidateGrid.y);
            if (!candidateRoom.HasValue) continue;
            if (!IsRoomEntryTile(candidateRoom.Value, candidateGrid)) continue;

            if (!hasCandidateEntry)
            {
                candidateEntryRoom = candidateRoom.Value;
                candidateEntryGrid = candidateGrid;
                hasCandidateEntry = true;
                roomSampleCount = 1;
                continue;
            }

            if (candidateRoom.Value.X != candidateEntryRoom.X ||
                candidateRoom.Value.Y != candidateEntryRoom.Y)
            {
                continue;
            }

            roomSampleCount++;
            if (roomSampleCount >= ROOM_ENTRY_SAMPLE_THRESHOLD)
            {
                room = candidateEntryRoom;
                gridPos = candidateEntryGrid;
                return true;
            }
        }

        room = default;
        gridPos = dungeonManager.WorldToGrid(center);
        return false;
    }

    private bool IsRoomEntryTile(RoomInfo room, Vector2Int gridPos)
    {
        return room.Contains(gridPos.x, gridPos.y) &&
               dungeonManager.GetTileType(gridPos.x, gridPos.y) == DungeonGenerator.ROOM;
    }

    // ══════════════════════════════════════════════════════════════
    //  계단 상호작용
    // ══════════════════════════════════════════════════════════════

    private void TryInteractStair()
    {
        if (dungeonManager.IsTransitioning)
            return;

        var gridPos  = dungeonManager.WorldToGrid(transform.position);
        int tileType = dungeonManager.GetTileType(gridPos.x, gridPos.y);

        if (tileType == DungeonGenerator.STAIR_UP)
        {
            if (RuntimePerfLogger.IsActive)
                RuntimePerfLogger.MarkEvent("stair_used",
                    "fromFloor=" + dungeonManager.floor +
                    " grid=" + gridPos.x + ":" + gridPos.y);
            // NextFloor()는 코루틴 → 완료 후 OnFloorChanged 발행
            // → OnFloorChangedHandler에서 SpawnAtStart() 호출
            // → 새 던전 생성 완료 후 스폰이 보장됨
            dungeonManager.NextFloor();
#if UNITY_EDITOR
            Debug.Log($"[Player] 계단 사용 → {dungeonManager.floor + 1}층으로 이동 중...");
#endif
        }
    }
}
