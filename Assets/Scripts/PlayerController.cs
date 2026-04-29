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
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class PlayerController : MonoBehaviour
{
    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("Dependencies")]
    [Tooltip("던전 관리자 — 이동 가능 여부·방 쿼리 제공")]
    private DungeonManager dungeonManager => DungeonManager.Instance;

    [Tooltip("이벤트 채널 — 방 진입 이벤트 발행")]
    public DungeonEventChannel eventChannel;

    [Tooltip("문 컨트롤러 — R키로 문 열기 호출")]
    public DoorController doorController;

    [Header("Movement")]
    [Tooltip("초당 이동 속도 (타일 단위)")]
    public float moveSpeed = 5f;

    [Header("Collision")]
    [Tooltip("충돌 반지름 비율 (0.05 ~ 0.49). 작을수록 좁은 통로 통과 쉬움.")]
    [Range(0.05f, 0.49f)]
    public float collisionRadius = 0.2f;

    // ── 내부 상태 ─────────────────────────────────────────────────────
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

    // 마지막으로 CheckRoomEntry를 실행한 그리드 좌표
    // 같은 셀에 머무는 프레임에는 탐색 전체를 스킵합니다.
    private Vector2Int _lastCheckedGridPos = new Vector2Int(-1, -1);

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

        if (dungeonManager == null) { Debug.LogError("[PlayerController] DungeonManager 없음"); enabled = false; return; }
        if (eventChannel   == null) { Debug.LogError("[PlayerController] EventChannel 없음");  enabled = false; return; }

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
        _lastCheckedGridPos     = new Vector2Int(-1, -1);   // 강제 재탐색

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

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.zKey.wasPressedThisFrame && _stairCooldown <= 0f)
        {
            TryInteractStair();
            return;
        }

        // f10키: 문 열기 — 입력 감지는 Player, 실행은 DoorController
        if (keyboard.f10Key.wasPressedThisFrame)
        {
            doorController?.OpenAllDoors();
            return;
        }

        Vector2 input = ReadMovementInput(keyboard);
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

        if (IsPhysicsFootprintWalkable(transform.position))
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

    private static Vector2 ReadMovementInput(Keyboard kb)
    {
        float x = 0f, y = 0f;
        if (kb.upArrowKey.isPressed)    y =  1f;
        if (kb.downArrowKey.isPressed)  y = -1f;
        if (kb.leftArrowKey.isPressed)  x = -1f;
        if (kb.rightArrowKey.isPressed) x =  1f;
        return new Vector2(x, y);
    }

    private void MoveWithCollision(Vector2 input)
    {
        float   step   = moveSpeed * Time.deltaTime;
        Vector3 origin = transform.position;

        if (input.x != 0f)
        {
            Vector3 next = origin + new Vector3(input.x * step, 0f, 0f);
            if (CanMoveTo(next)) origin = next;
        }
        if (input.y != 0f)
        {
            Vector3 next = origin + new Vector3(0f, input.y * step, 0f);
            if (CanMoveTo(next)) origin = next;
        }

        transform.position = origin;
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
        // 그리드 좌표가 바뀐 프레임에만 실행
        // 같은 타일에 머무는 동안은 방 상태가 절대 변하지 않으므로 스킵
        
        var gridPos = dungeonManager.WorldToGrid(transform.position);
        if (dungeonManager.IsCorr(gridPos.x, gridPos.y))
        {
            return;
        }
        else
        {
            _lastRoom = null;
        }
        if (gridPos == _lastCheckedGridPos) return;
        _lastCheckedGridPos = gridPos;
        
        
        // ── 이 아래는 셀이 바뀐 프레임에만 실행 ──────────────────────
        var room = dungeonManager.GetRoomAt(gridPos.x, gridPos.y);
        

        if(room.Equals(_lastRoom)) return;
        _lastRoom = room;

        if (!room.HasValue)
        {
            _currentRoom = null;
            return;
        }

        bool isInterior =
            gridPos.x > room.Value.X     && gridPos.x < room.Value.Right  - 1 &&
            gridPos.y > room.Value.Y     && gridPos.y < room.Value.Bottom - 1;
        if (!isInterior) return;

        bool isNewEntry = !_currentRoom.HasValue ||
                          _currentRoom.Value.X != room.Value.X ||
                          _currentRoom.Value.Y != room.Value.Y;
        if (!isNewEntry) return;

        _currentRoom = room;

        var key = (room.Value.X, room.Value.Y);
        bool isFirstVisit = !_visitedRooms.Contains(key);
        if (isFirstVisit && room.Value.Type == RoomType.Normal)
            _visitedRooms.Add(key);

        if (RuntimePerfLogger.IsActive)
            RuntimePerfLogger.MarkEvent("room_entered",
                "room=" + room.Value.X + ":" + room.Value.Y +
                " type=" + room.Value.Type +
                " firstVisit=" + isFirstVisit +
                " grid=" + gridPos.x + ":" + gridPos.y);
        eventChannel.RaiseRoomEntered(room.Value, isFirstVisit);
        
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
