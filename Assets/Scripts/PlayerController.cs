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
//    • 문 개폐 로직 (DungeonManager.OpenCurrentRoomDoors 담당)
//    • Tilemap (DungeonTilemapRenderer 담당)
//    • 던전 생성 알고리즘
// ═══════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerInventory))]
public class PlayerController : MonoBehaviour
{
    public static PlayerController Active { get; private set; }

    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("Dependencies")]
    [Tooltip("던전 관리자 — 이동 가능 여부·방 쿼리 제공")]
    private DungeonManager dungeonManager => DungeonManager.Instance;

    [Tooltip("이벤트 채널 — 방 진입 이벤트 발행")]
    public DungeonEventChannel eventChannel;

    [Header("Movement")]
    [Tooltip("초당 이동 속도 (타일 단위)")]
    public float moveSpeed = 5f;

    [Header("Collision")]
    [Tooltip("충돌 반지름 비율 (0.05 ~ 0.49). 작을수록 좁은 통로 통과 쉬움.")]
    [Range(0.05f, 0.49f)]
    public float collisionRadius = 0.2f;

    private const float POSITION_RECHECK_EPSILON_SQR = 0.000001f;

    // ── 내부 상태 ─────────────────────────────────────────────────────
    private PlayerInputReader _inputReader;
    private float      _tileSize;
    private float      _stairCooldown;
    private const float STAIR_COOLDOWN = 0.5f;
    private const float MOUSE_FACING_EPSILON_SQR = 0.0001f;

    /// <summary>마지막 이동 입력 방향 (그리드 단위). PlayerCombatController가 공격 방향으로 사용.</summary>
    public Vector2Int FacingDirection { get; private set; } = Vector2Int.down;

    private RoomInfo? _currentRoom;

    private readonly HashSet<(int x, int y)> _visitedRooms
        = new HashSet<(int x, int y)>();

    private readonly Vector3[] _roomEntrySamples = new Vector3[RoomFootprintSampler.SampleCount];

    // 마지막으로 방문안 Room
    // Room이 변경되거나 복도로 나가기 전까진 체크하지 않는다.
    private RoomInfo? _lastRoom;
    private Rigidbody2D _rb;
    private CircleCollider2D _circleCollider;
    private PlayerCombatController _combat;
    private PlayerDashController _dashController;
    private PlayerInventory _inventory;
    private bool _warnedMissingEliteKeyItem;
    private Vector3 _lastSafePosition;

    // ══════════════════════════════════════════════════════════════
    //  초기화
    // ══════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Active == null || ReferenceEquals(Active, this))
        {
            Active = this;
        }
        else
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning(
                $"[PlayerController] Active 인스턴스가 이미 존재합니다 ({Active.name}) — 새 인스턴스({name})로 교체합니다.",
                this);
#endif
            Active = this;
        }

        _inventory = GetComponent<PlayerInventory>();
    }

    private void Start()
    {
        ConfigurePhysics();

        _inputReader = GetComponent<PlayerInputReader>();
        _combat = GetComponent<PlayerCombatController>();
        _dashController = GetComponent<PlayerDashController>();
        if (_inputReader == null) { Debug.LogError("[PlayerController] PlayerInputReader 없음"); enabled = false; return; }

        if (dungeonManager == null) { Debug.LogError("[PlayerController] DungeonManager 없음"); enabled = false; return; }
        if (eventChannel   == null) { Debug.LogError("[PlayerController] EventChannel 없음");  enabled = false; return; }
        if (dungeonManager.dungeonRenderer == null) { Debug.LogError("[PlayerController] DungeonTilemapRenderer 없음"); enabled = false; return; }
        if (dungeonManager.dungeonRenderer.tilemap == null) { Debug.LogError("[PlayerController] DungeonTilemapRenderer.tilemap 없음"); enabled = false; return; }

        LocationTransitionManager locationManager = LocationTransitionManager.Active;
        if (dungeonManager.Data == null && (locationManager == null || !locationManager.StartsInTown))
            dungeonManager.Generate();

        _tileSize = dungeonManager.dungeonRenderer != null && dungeonManager.dungeonRenderer.tilemap != null
            ? dungeonManager.dungeonRenderer.tilemap.cellSize.x
            : 1f;

        // 층 변경 완료(던전 생성 후) 시 스폰 → 이벤트로 타이밍 보장
        eventChannel.OnFloorChanged += OnFloorChangedHandler;

        if (ShouldUseDungeonSystems())
            SpawnAtStart();
    }

    private void OnDestroy()
    {
        if (eventChannel != null)
            eventChannel.OnFloorChanged -= OnFloorChangedHandler;

        if (ReferenceEquals(Active, this))
            Active = null;
    }

    private void ConfigurePhysics()
    {
        (_rb, _circleCollider) = CharacterPhysicsSetup.Configure(gameObject, "Player");
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
        TeleportTo(dungeonManager.GridToWorld(gridPos));
        _stairCooldown          = STAIR_COOLDOWN;
        _currentRoom            = null;
        _visitedRooms.Clear();
        _lastRoom               = null;

        var spawnRoom = dungeonManager.GetRoomAt(gridPos.x, gridPos.y);
        if (spawnRoom.HasValue)
            dungeonManager.SetRoomType(spawnRoom.Value, RoomType.Spawn);
    }

    public void TeleportTo(Vector3 worldPosition)
    {
        if (_dashController == null)
            _dashController = GetComponent<PlayerDashController>();
        _dashController?.CancelDash();

        transform.position = worldPosition;
        _lastSafePosition = transform.position;
        if (_rb != null)
            _rb.linearVelocity = Vector2.zero;
    }

    // ══════════════════════════════════════════════════════════════
    //  매 프레임 처리
    // ══════════════════════════════════════════════════════════════

    private void Update()
    {
        if (_combat != null && _combat.IsDead)
        {
            if (_rb != null)
                _rb.linearVelocity = Vector2.zero;
            return;
        }

        _stairCooldown -= Time.deltaTime;

        if (dungeonManager != null && dungeonManager.IsTransitioning)
            return;

        if (ShouldUseDungeonSystems())
            TryOpenEliteDoorOnContact();

        if (_dashController != null && _dashController.IsDashing)
        {
            if (ShouldUseDungeonSystems())
                CheckRoomEntry();
            return;
        }

        if (_inputReader == null) return;
        RefreshActionMouseFacing();

        if (_combat != null && _combat.IsStunned)
        {
            if (_rb != null)
                _rb.linearVelocity = Vector2.zero;
            if (ShouldUseDungeonSystems())
                CheckRoomEntry();
            return;
        }

        if (_inputReader.InteractConfirmPressedThisFrame && _stairCooldown <= 0f)
        {
            if (ShouldUseDungeonSystems())
                TryInteractStair();
            return;
        }

        if (_combat != null && _combat.BlocksPlayerMovement)
        {
            if (_rb != null)
                _rb.linearVelocity = Vector2.zero;
            if (ShouldUseDungeonSystems())
                CheckRoomEntry();
            return;
        }

        Vector2 input = _inputReader.MoveInput;
        if (input != Vector2.zero)
        {
            // 대각선 입력 시 X 축 우선으로 facing 결정
            if (!_inputReader.HasMouseAim)
            {
                FacingDirection = input.x != 0f
                    ? new Vector2Int((int)Mathf.Sign(input.x), 0)
                    : new Vector2Int(0, (int)Mathf.Sign(input.y));
            }

            if (input.x != 0f && input.y != 0f)
                input = input.normalized;
            MoveWithCollision(input);
        }

        if (ShouldUseDungeonSystems())
        {
            CheckRoomEntry();
        }
    }

    private void RefreshActionMouseFacing()
    {
        if (_inputReader == null || !_inputReader.HasMouseAim)
            return;

        Vector2 aim = _inputReader.AimWorldPoint - (Vector2)transform.position;
        if (aim.sqrMagnitude <= MOUSE_FACING_EPSILON_SQR)
            return;

        if (AimDirectionUtility.TryGetEightWayRaw(aim, out Vector2Int rawDirection))
            FacingDirection = AimDirectionUtility.ToCardinalDirection(rawDirection);
    }

    private void TryOpenEliteDoorOnContact()
    {
        if (_inventory == null || dungeonManager == null || dungeonManager.dungeonRenderer == null)
            return;

        if (!_inventory.TryGetDatabaseItem(DeterministicSeedUtility.EliteKeyDomain, out ItemData eliteKeyItem))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_warnedMissingEliteKeyItem)
            {
                Debug.LogWarning("[PlayerController] Elite Key item is missing from PlayerInventory database.", this);
                _warnedMissingEliteKeyItem = true;
            }
#endif
            return;
        }

        dungeonManager.dungeonRenderer.TryOpenEliteDoorWithKey(_inventory, eliteKeyItem);
    }

    public PlayerInventory Inventory => _inventory;

    private void LateUpdate()
    {
        if (!ShouldUseDungeonSystems()) return;

        Vector2 positionDelta = (Vector2)transform.position - (Vector2)_lastSafePosition;
        if (positionDelta.sqrMagnitude < POSITION_RECHECK_EPSILON_SQR)
            return;

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
        float   speedMultiplier = _combat != null ? _combat.MoveSpeedMultiplier : 1f;
        float   step   = moveSpeed * speedMultiplier * Time.deltaTime;
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
        LocationTransitionManager locationManager = LocationTransitionManager.Active;
        if (locationManager != null && locationManager.IsInTown)
        {
            float townRadius = GetWorldColliderRadius();
            return Physics2D.OverlapCircle(pos, townRadius, CombatLayers.WallMask) == null
                && !MovementBlockerQuery.IsPlayerMovementBlocked(pos, townRadius);
        }

        float radius = _tileSize * collisionRadius;
        return WorldEnvironmentQuery.IsFootprintWalkable(pos, radius) &&
               !MovementBlockerQuery.IsPlayerMovementBlocked(pos, radius);
    }

    public bool TryApplyExternalDisplacement(Vector2 delta)
    {
        if (delta.sqrMagnitude <= 0.000001f)
            return false;
        if (dungeonManager == null || dungeonManager.Data == null)
            return false;

        Vector3 origin = transform.position;
        Vector3 next = origin + new Vector3(delta.x, delta.y, 0f);
        if (!CanMoveTo(next))
            return false;

        transform.position = next;
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
        if (!ShouldUseDungeonSystems())
            return;

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

        RoomFootprintSampler.FillSamples(_roomEntrySamples, center, radius);

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
            if (roomSampleCount >= RoomFootprintSampler.Threshold)
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
        if (!ShouldUseDungeonSystems())
            return;

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

    private bool ShouldUseDungeonSystems()
    {
        LocationTransitionManager locationManager = LocationTransitionManager.Active;
        if (locationManager != null && !locationManager.IsInDungeon)
            return false;

        return dungeonManager != null && dungeonManager.Data != null;
    }
}
