using UnityEngine;

public enum EnemyAIStateId
{
    Idle = 0,
    Chase = 1,
    Attack = 2,

    // 에픽/보스 전용 상태는 이 뒤 번호를 사용하면 기본 상태와 충돌하지 않습니다.
    Phase2 = 100,
    Berserk = 101,
}

/// <summary>
/// 모든 적 상태가 공유하는 최소 인터페이스입니다.
/// BossEnemyBrain은 CreateState를 오버라이드해서 Phase2, Berserk 같은 고유 상태를 반환하면 됩니다.
/// </summary>
public interface IEnemyState
{
    void OnEnter();
    void Tick(float sqrDistanceToTarget);
    void OnExit();
}

/// <summary>
/// 적 AI의 추상 베이스입니다.
/// 이 클래스는 FSM 조율만 담당하고, 이동/타겟/액션은 Handler로 분리합니다.
/// 일반 몬스터는 NormalEnemyBrain을 사용하고, 에픽/보스는 이 클래스를 상속해 확장합니다.
/// </summary>
[RequireComponent(typeof(EnemyController))]
public abstract class EnemyBrain : MonoBehaviour
{
    protected const string ANIM_MOVING = "Walk";
    protected const string ANIM_ATTACK = "Attack";

    public DungeonManager dungeonManager => DungeonManager.Instance;
    [Header("Dependencies")]
    public Transform player;

    [Header("Pathfinding")]
    [Tooltip("ChaseState에서 A* 경로를 다시 계산하는 주기입니다. 매 프레임 탐색을 막아 CPU/GC 부담을 줄입니다.")]
    public float pathUpdateInterval = 0.3f;

    [Tooltip("웨이포인트에 도착했다고 판단하는 거리입니다.")]
    public float waypointReachDistance = 0.08f;

    [Header("Collision")]
    [Range(0.05f, 0.49f)]
    public float collisionRadius = 0.2f;

    [Header("Separation")]
    public bool enableSeparation = true;
    [Min(0.05f)] public float separationRadius = 0.7f;
    [Range(0f, 2f)] public float separationWeight = 0.55f;
    [Range(0f, 30f)] public float separationSmoothing = 12f;

    [Header("Animation")]
    public Animator animator;

    private EnemyController _enemy;
    private EnemyData _data;
    private SpriteRenderer _spriteRenderer;
    private EnemyAnimationController _animationController;

    private IEnemyState _idleState;
    private IEnemyState _chaseState;
    private IEnemyState _attackState;
    private IEnemyState _currentState;
    private EnemyAIStateId _currentStateId = EnemyAIStateId.Idle;

    private bool _animParamsScanned;
    private bool _hasMovingParam;
    private bool _hasAttackParam;

    public EnemyController Enemy => _enemy;
    public EnemyData Data => _data;
    public EnemyAIStateId CurrentState => _currentStateId;
    public DungeonData DungeonData => dungeonManager != null ? dungeonManager.Data : null;
    public float CurrentMoveSpeed => Data != null && Enemy != null
        ? Data.moveSpeed * Enemy.MoveSpeedMultiplier
        : 0f;

    public MovementHandler Movement { get; private set; }
    public TargetHandler Target { get; private set; }
    public ActionHandler Action { get; private set; }

    public Vector2Int GridPosition => Movement != null ? Movement.GridPosition : Vector2Int.zero;
    public Vector2Int PlayerGridPosition => Target != null ? Target.TargetGridPosition : Vector2Int.zero;

    protected virtual void Awake()
    {
        _enemy = GetComponent<EnemyController>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _animationController = GetComponentInChildren<EnemyAnimationController>(true);

        Movement = CreateMovementHandler();
        Target = CreateTargetHandler();
        Action = CreateActionHandler();
    }

    protected virtual void Start()
    {
        TryCacheData();
        Movement.Initialize();
        Target.RefreshTarget();

        _idleState = CreateState(EnemyAIStateId.Idle);
        _chaseState = CreateState(EnemyAIStateId.Chase);
        _attackState = CreateState(EnemyAIStateId.Attack);

        _currentState = _idleState;
        _currentStateId = EnemyAIStateId.Idle;
        _currentState.OnEnter();
    }

    public virtual void ResetRuntimeState()
    {
        // 풀에서 다시 꺼낸 적은 현재 층의 DungeonManager.Instance 기준으로 이동/타겟 캐시를 새로 잡는다.
        _data = null;
        Movement?.Initialize();
        Target?.RefreshTarget();
        Action?.ResetRuntimeState();
        _animationController?.ResetAnimationState();

        if (_idleState != null)
        {
            _currentState?.OnExit();
            _currentState = _idleState;
            _currentStateId = EnemyAIStateId.Idle;
            _currentState.OnEnter();
        }
    }

    protected virtual void Update()
    {
        if (_enemy == null || !_enemy.IsAlive) return;
        if (!TryCacheData()) return;

        if (_enemy.IsKnockbackLocked)
        {
            StopMoving();
            return;
        }

        // CheckRoomEntry가 복도에서 조기 종료하더라도 AI는 매 프레임 실제 월드 좌표를 그리드로 변환합니다.
        // 따라서 플레이어가 ROOM 밖 CORRIDOR에 있어도 목표 좌표가 끊기지 않습니다.
        if (!Target.RefreshTarget())
        {
            StopMoving();
            return;
        }

        Action.TickCooldown(Time.deltaTime);

        float sqrDistance = Target.SqrDistanceToTarget;
        Action.TickBehavior(sqrDistance);
        _currentState.Tick(sqrDistance);
    }

    public void ChangeState(EnemyAIStateId next)
    {
        if (_currentStateId == next) return;

        _currentState?.OnExit();
        _currentStateId = next;
        _currentState = GetOrCreateState(next);
        _currentState.OnEnter();
    }

    public bool CanAttack(float sqrDistanceToPlayer)
    {
        return Action.CanAttack(sqrDistanceToPlayer);
    }

    public bool ShouldKeepChasing(float sqrDistanceToPlayer)
    {
        return Target.HasTarget && sqrDistanceToPlayer <= Target.DetectRangeSqr;
    }

    public void DirectMoveToPlayer()
    {
        if (!Target.HasTarget)
        {
            StopMoving();
            return;
        }

        MoveToward(Target.TargetPosition);
    }

    public void MoveToward(Vector3 target)
    {
        bool moved = Movement.MoveToward(target);
        SetAnimBool(ANIM_MOVING, moved);

        float dirX = target.x - transform.position.x;
        FlipSprite(dirX);
    }

    public void StopMoving()
    {
        Movement.Stop();
        SetAnimBool(ANIM_MOVING, false);
    }

    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        return Movement.GridToWorld(gridPos);
    }

    public bool HasLineOfSightToPlayer()
    {
        return Target.HasTarget && Movement.HasLineOfSight(GridPosition, Target.TargetGridPosition);
    }

    protected virtual MovementHandler CreateMovementHandler()
    {
        return new MovementHandler(this);
    }

    protected virtual TargetHandler CreateTargetHandler()
    {
        return new TargetHandler(this);
    }

    protected virtual ActionHandler CreateActionHandler()
    {
        return new ActionHandler(this);
    }

    /// <summary>
    /// 상태 생성 팩토리입니다.
    /// 보스는 이 메서드를 오버라이드해서 Phase2/Berserk 상태를 추가하고,
    /// 기본 상태는 base.CreateState(stateId)를 재사용하면 됩니다.
    /// </summary>
    protected virtual IEnemyState CreateState(EnemyAIStateId stateId)
    {
        switch (stateId)
        {
            case EnemyAIStateId.Idle:
                return new IdleState(this);

            case EnemyAIStateId.Chase:
                return new ChaseState(this);

            case EnemyAIStateId.Attack:
                return new AttackState(this);

            default:
                return CreateCustomState(stateId);
        }
    }

    protected virtual IEnemyState CreateCustomState(EnemyAIStateId stateId)
    {
        // 기본 몬스터는 커스텀 상태를 쓰지 않습니다.
        // 파생 클래스가 처리하지 않은 상태로 전환되면 안전하게 Idle로 되돌립니다.
        return new IdleState(this);
    }

    protected virtual bool TryCacheData()
    {
        if (_data != null) return true;
        if (_enemy == null || _enemy.data == null) return false;

        _data = _enemy.data;
        Target.RecalculateRanges();
        Action.RecalculateRanges();
        return true;
    }

    protected virtual void TriggerAttackAnimation()
    {
        if (Data != null && Data.behaviorType == EnemyBehaviorType.Ranged)
        {
            if (Target != null && Target.HasTarget)
                _animationController?.PlayAttack(Target.TargetPosition);
            else
                _animationController?.PlayAttack();
        }

        SetAnimTrigger(ANIM_ATTACK);
    }

    private IEnemyState GetOrCreateState(EnemyAIStateId stateId)
    {
        switch (stateId)
        {
            case EnemyAIStateId.Idle:
                return _idleState ?? (_idleState = CreateState(stateId));

            case EnemyAIStateId.Chase:
                return _chaseState ?? (_chaseState = CreateState(stateId));

            case EnemyAIStateId.Attack:
                return _attackState ?? (_attackState = CreateState(stateId));

            default:
                // 고유 상태는 파생 클래스가 필요할 때 생성합니다.
                // 일반 몬스터가 고유 상태를 쓰지 않도록 기본 메모리 사용량을 낮게 유지합니다.
                return CreateState(stateId);
        }
    }

    private void FlipSprite(float dirX)
    {
        if (_spriteRenderer == null || dirX == 0f) return;
        _spriteRenderer.flipX = dirX < 0f;
    }

    private void EnsureAnimScanned()
    {
        if (_animParamsScanned || animator == null) return;

        _animParamsScanned = true;
        _hasMovingParam = false;
        _hasAttackParam = false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == ANIM_MOVING) _hasMovingParam = true;
            if (parameter.name == ANIM_ATTACK) _hasAttackParam = true;
        }
    }

    private void SetAnimBool(string param, bool value)
    {
        if (animator == null) return;

        EnsureAnimScanned();
        if (param == ANIM_MOVING && !_hasMovingParam) return;
        animator.SetBool(param, value);
    }

    private void SetAnimTrigger(string param)
    {
        if (animator == null) return;

        EnsureAnimScanned();
        if (param == ANIM_ATTACK && !_hasAttackParam) return;
        animator.SetTrigger(param);
    }

    /// <summary>
    /// 이동과 충돌, 시야 검사를 담당합니다.
    /// MonoBehaviour가 아닌 일반 C# 객체라서 일반 몬스터에 불필요한 Unity 컴포넌트를 추가하지 않습니다.
    /// </summary>
    public class MovementHandler
    {
        private readonly EnemyBrain _brain;
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Collider2D[] _separationBuffer = new Collider2D[16];
        private static readonly ContactFilter2D s_SeparationFilter = ContactFilter2D.noFilter;
        private float _tileSize = 1f;
        private Vector2 _smoothedSeparation;

        public MovementHandler(EnemyBrain brain)
        {
            _brain = brain;
        }

        public Vector2Int GridPosition => _brain.dungeonManager != null
            ? _brain.dungeonManager.WorldToGrid(_brain.transform.position)
            : Vector2Int.zero;

        public virtual void Initialize()
        {
            if (_brain.dungeonManager != null &&
                _brain.dungeonManager.dungeonRenderer != null &&
                _brain.dungeonManager.dungeonRenderer.tilemap != null)
            {
                _tileSize = _brain.dungeonManager.dungeonRenderer.tilemap.cellSize.x;
            }
        }

        public virtual bool MoveToward(Vector3 target)
        {
            if (_brain.Data == null) return false;

            Vector2 dir = target - _brain.transform.position;
            if (dir.sqrMagnitude <= 0.0001f)
                return false;

            Vector2 desired = dir.normalized;
            Vector2 separation = CalculateSeparation();
            Vector2 blended = desired + separation * _brain.separationWeight;

            if (blended.sqrMagnitude > 1f)
                blended.Normalize();

            return MoveWithCollision(blended);
        }

        public virtual void Stop()
        {
        }

        public virtual Vector3 GridToWorld(Vector2Int gridPos)
        {
            return _brain.dungeonManager != null
                ? _brain.dungeonManager.GridToWorld(gridPos)
                : new Vector3(gridPos.x, gridPos.y, 0f);
        }

        public virtual bool HasLineOfSight(Vector2Int start, Vector2Int goal)
        {
            DungeonData data = _brain.DungeonData;
            if (data == null) return false;

            int x0 = start.x;
            int y0 = start.y;
            int x1 = goal.x;
            int y1 = goal.y;

            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (!data.InBounds(x0, y0))
                    return false;

                // 복도와 방을 가로지르는 시야에서는 EMPTY(0)만 벽으로 봅니다.
                // 닫힌 문이나 계단 같은 특수 타일이 시야 검사를 불필요하게 끊지 않게 합니다.
                if (data.GetTileTypeUnchecked(x0, y0) == DungeonGenerator.EMPTY)
                    return false;

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
            }
        }

        private bool MoveWithCollision(Vector2 dir)
        {
            if (dir.sqrMagnitude <= 0.0001f)
                return false;

            float step = _brain.CurrentMoveSpeed * Time.deltaTime;
            if (step <= 0f) return false;
            Vector3 origin = _brain.transform.position;
            bool moved = false;

            if (dir.x != 0f)
            {
                Vector3 next = origin + new Vector3(dir.x * step, 0f, 0f);
                if (CanMoveTo(next))
                {
                    origin = next;
                    moved = true;
                }
            }

            if (dir.y != 0f)
            {
                Vector3 next = origin + new Vector3(0f, dir.y * step, 0f);
                if (CanMoveTo(next))
                {
                    origin = next;
                    moved = true;
                }
            }

            _brain.transform.position = origin;
            return moved;
        }

        private Vector2 CalculateSeparation()
        {
            if (!_brain.enableSeparation) return Vector2.zero;

            int neighborCount = Physics2D.OverlapCircle(
                _brain.transform.position,
                _brain.separationRadius,
                s_SeparationFilter,
                _separationBuffer);

            Vector2 repel = Vector2.zero;
            int count = 0;
            Vector2 self = _brain.transform.position;

            for (int i = 0; i < neighborCount; i++)
            {
                Collider2D col = _separationBuffer[i];
                if (col == null) continue;
                if (col.transform == _brain.transform) continue;
                if (!col.TryGetComponent<EnemyController>(out _)) continue;

                Vector2 away = self - (Vector2)col.bounds.center;
                float sqrDistance = Mathf.Max(away.sqrMagnitude, 0.0001f);

                // 가까운 이웃일수록 더 강하게 밀어내 평균 반발 벡터를 만든다.
                repel += away.normalized / sqrDistance;
                count++;
            }

            Vector2 targetSeparation = count > 0 ? (repel / count).normalized : Vector2.zero;

            // 분리 벡터를 보간해 프레임마다 방향이 튀는 지터를 줄인다.
            float t = 1f - Mathf.Exp(-_brain.separationSmoothing * Time.deltaTime);
            _smoothedSeparation = Vector2.Lerp(_smoothedSeparation, targetSeparation, t);
            return _smoothedSeparation;
        }

        private bool CanMoveTo(Vector3 pos)
        {
            if (_brain.dungeonManager == null) return true;

            float radius = _brain.Enemy != null
                ? _brain.Enemy.CollisionFootprintRadius
                : _tileSize * _brain.collisionRadius;
            _corners[0] = new Vector3(pos.x - radius, pos.y - radius, 0f);
            _corners[1] = new Vector3(pos.x + radius, pos.y - radius, 0f);
            _corners[2] = new Vector3(pos.x - radius, pos.y + radius, 0f);
            _corners[3] = new Vector3(pos.x + radius, pos.y + radius, 0f);

            for (int i = 0; i < _corners.Length; i++)
            {
                Vector2Int grid = _brain.dungeonManager.WorldToGrid(_corners[i]);
                if (!_brain.dungeonManager.IsWalkable(grid.x, grid.y))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 플레이어 탐색과 현재 목표 좌표 관리를 담당합니다.
    /// 방 입장 이벤트나 CheckRoomEntry에 의존하지 않고 매 프레임 월드 좌표에서 그리드 좌표를 얻습니다.
    /// </summary>
    public class TargetHandler
    {
        private readonly EnemyBrain _brain;
        private IDamageable _damageable;
        private Collider2D _targetCollider;
        private float _detectRangeSqr;

        public TargetHandler(EnemyBrain brain)
        {
            _brain = brain;
        }

        public bool HasTarget => _brain.player != null;
        public IDamageable Damageable => _damageable;
        public Collider2D TargetCollider => _targetCollider;
        public float DetectRangeSqr => _detectRangeSqr;
        public Vector3 TargetPosition => _brain.player != null ? _brain.player.position : _brain.transform.position;
        public float SqrDistanceToTarget => (_brain.player.position - _brain.transform.position).sqrMagnitude;

        public Vector2Int TargetGridPosition => _brain.dungeonManager != null && _brain.player != null
            ? _brain.dungeonManager.WorldToGrid(_brain.player.position)
            : Vector2Int.zero;

        public virtual void RecalculateRanges()
        {
            if (_brain.Data == null) return;
            _detectRangeSqr = _brain.Data.detectRange * _brain.Data.detectRange;
        }

        public virtual bool RefreshTarget()
        {
            if (_brain.player == null)
                FindPlayer();

            if (_brain.player == null)
                return false;

            if (_damageable == null)
                _damageable = ResolveDamageable(_brain.player);
            if (_targetCollider == null)
                _targetCollider = ResolveCollider(_brain.player);

            return IsTargetOnTrackableTile();
        }

        private void FindPlayer()
        {
            GameObject playerObject = GameObject.FindWithTag("Player");
            if (playerObject == null)
            {
                PlayerController playerController = FindAnyObjectByType<PlayerController>();
                if (playerController != null)
                    playerObject = playerController.gameObject;
            }

            if (playerObject == null) return;

            _brain.player = playerObject.transform;
            _damageable = ResolveDamageable(_brain.player);
            _targetCollider = ResolveCollider(_brain.player);
        }

        private Collider2D ResolveCollider(Transform targetTransform)
        {
            if (targetTransform == null) return null;

            Collider2D col = targetTransform.GetComponent<Collider2D>();
            if (col != null) return col;

            col = targetTransform.GetComponentInParent<Collider2D>();
            if (col != null) return col;

            return targetTransform.GetComponentInChildren<Collider2D>();
        }

        private IDamageable ResolveDamageable(Transform targetTransform)
        {
            if (targetTransform == null) return null;

            IDamageable damageable = targetTransform.GetComponent<IDamageable>();
            if (damageable != null) return damageable;

            damageable = targetTransform.GetComponentInParent<IDamageable>();
            if (damageable != null) return damageable;

            damageable = targetTransform.GetComponentInChildren<IDamageable>();
            if (damageable != null) return damageable;

            PlayerCombatController combatController = targetTransform.GetComponent<PlayerCombatController>();
            if (combatController != null) return combatController;

            combatController = targetTransform.GetComponentInParent<PlayerCombatController>();
            if (combatController != null) return combatController;

            combatController = targetTransform.GetComponentInChildren<PlayerCombatController>();
            if (combatController != null) return combatController;

            return null;
        }

        private bool IsTargetOnTrackableTile()
        {
            DungeonData data = _brain.DungeonData;
            if (data == null) return true;

            Vector2Int grid = TargetGridPosition;
            if (!data.InBounds(grid.x, grid.y)) return false;

            int tile = data.GetTileTypeUnchecked(grid.x, grid.y);

            // 핵심 수정: ROOM뿐 아니라 CORRIDOR/STair 등 EMPTY가 아닌 전체 그리드를 추적 대상으로 둡니다.
            // 그래서 플레이어가 복도에 나가도 적 AI의 목표 좌표가 사라지지 않습니다.
            return tile != DungeonGenerator.EMPTY;
        }
    }

    /// <summary>
    /// 공격 쿨다운, 선딜레이, 피해 적용을 담당합니다.
    /// 보스는 이 핸들러를 상속해 패턴 큐, 페이즈 전환, 광역 공격 등을 추가할 수 있습니다.
    /// </summary>
    public class ActionHandler
    {
        private readonly EnemyBrain _brain;
        private float _attackRangeSqr;
        private float _contactDamageRangeSqr;
        private Collider2D _selfCollider;
        private float _attackCooldownTimer;
        private float _windupTimer;
        private float _recoveryTimer;
        private Vector2 _aimDirection = Vector2.down;
        private int _pendingBurstShots;
        private float _burstTimer;
        private bool _windupFired;
        private bool _warnedMissingProjectile;

        public ActionHandler(EnemyBrain brain)
        {
            _brain = brain;
        }

        public virtual void RecalculateRanges()
        {
            if (_brain.Data == null) return;
            _attackRangeSqr = _brain.Data.attackRange * _brain.Data.attackRange;
            _contactDamageRangeSqr = _brain.Data.contactDamageRadius * _brain.Data.contactDamageRadius;
        }

        public virtual void TickCooldown(float deltaTime)
        {
            _attackCooldownTimer -= deltaTime;
        }

        public virtual void ResetRuntimeState()
        {
            _attackCooldownTimer = 0f;
            _windupTimer = 0f;
            _recoveryTimer = 0f;
            _pendingBurstShots = 0;
            _burstTimer = 0f;
            _windupFired = false;
        }

        public virtual void TickBehavior(float sqrDistanceToTarget)
        {
            if (_brain.Data == null) return;

            switch (_brain.Data.behaviorType)
            {
                case EnemyBehaviorType.Contact:
                    TickContactBehavior(sqrDistanceToTarget);
                    break;

                case EnemyBehaviorType.Ranged:
                    TickRangedBehavior();
                    break;
            }
        }

        private void TickRangedBehavior()
        {
            if (_pendingBurstShots <= 0)
                return;

            float interval = Mathf.Max(0f, _brain.Data.burstInterval);
            if (interval <= 0f)
            {
                while (_pendingBurstShots > 0)
                {
                    FireProjectile(_aimDirection);
                    _pendingBurstShots--;
                }
                return;
            }

            _burstTimer -= Time.deltaTime;
            while (_pendingBurstShots > 0 && _burstTimer <= 0f)
            {
                FireProjectile(_aimDirection);
                _pendingBurstShots--;
                _burstTimer += interval;
            }
        }

        private void TickContactBehavior(float sqrDistanceToTarget)
        {
            if (!_brain.ShouldKeepChasing(sqrDistanceToTarget))
                return;

            if (!IsContactingTarget(sqrDistanceToTarget))
                return;

            ApplyDamage();
        }

        private bool IsContactingTarget(float sqrDistanceToTarget)
        {
            Collider2D self = ResolveSelfCollider();
            Collider2D target = _brain.Target.TargetCollider;
            if (self != null && target != null && self.enabled && target.enabled)
            {
                ColliderDistance2D distance = self.Distance(target);
                return distance.isOverlapped || distance.distance <= Mathf.Max(0f, _brain.Data.contactDamageSkin);
            }

            return sqrDistanceToTarget <= _contactDamageRangeSqr;
        }

        private Collider2D ResolveSelfCollider()
        {
            if (_selfCollider != null)
                return _selfCollider;

            if (_brain.Enemy != null)
                _selfCollider = _brain.Enemy.GetComponent<Collider2D>();
            if (_selfCollider == null)
                _selfCollider = _brain.GetComponent<Collider2D>();
            if (_selfCollider == null)
                _selfCollider = _brain.GetComponentInChildren<Collider2D>();

            return _selfCollider;
        }

        public virtual bool CanAttack(float sqrDistanceToTarget)
        {
            if (_brain.Data == null || _brain.Data.behaviorType == EnemyBehaviorType.Contact)
                return false;

            return sqrDistanceToTarget <= _attackRangeSqr && _attackCooldownTimer <= 0f;
        }

        public virtual void BeginAttack()
        {
            if (_brain.Data == null || _brain.Data.behaviorType != EnemyBehaviorType.Ranged)
                return;

            _windupTimer = Mathf.Max(0f, _brain.Data.attackWindup);
            _recoveryTimer = 0f;
            _aimDirection = ResolveAimDirection();
            _windupFired = false;
            _brain.TriggerAttackAnimation();

            if (_windupTimer > 0f)
                _brain.StopMoving();
        }

        public virtual bool TickAttack(float sqrDistanceToTarget)
        {
            if (_brain.Data == null || _brain.Data.behaviorType != EnemyBehaviorType.Ranged)
                return true;

            if (!_windupFired)
            {
                if (_windupTimer > 0f)
                {
                    _brain.StopMoving();
                    _aimDirection = ResolveAimDirection();
                    _windupTimer -= Time.deltaTime;

                    if (_windupTimer > 0f)
                        return false;
                }

                FireRangedPattern(_aimDirection);
                _windupFired = true;
                _attackCooldownTimer = Mathf.Max(0f, _brain.Data.attackCooldown);
                _recoveryTimer = Mathf.Max(0f, _brain.Data.attackRecovery);
            }

            if (_recoveryTimer > 0f)
            {
                _brain.StopMoving();
                _recoveryTimer -= Time.deltaTime;
                return _recoveryTimer <= 0f;
            }

            return true;
        }

        private Vector2 ResolveAimDirection()
        {
            if (_brain.Target == null || !_brain.Target.HasTarget)
                return _aimDirection.sqrMagnitude > 0.0001f ? _aimDirection : Vector2.down;

            Vector2 direction = _brain.Target.TargetPosition - _brain.transform.position;
            if (direction.sqrMagnitude <= 0.0001f)
                return _aimDirection.sqrMagnitude > 0.0001f ? _aimDirection : Vector2.down;

            return direction.normalized;
        }

        private void FireRangedPattern(Vector2 direction)
        {
            long fireStart = RuntimePerfTraceLogger.Timestamp();
            int requestedProjectiles = GetFireRequestCount();
            switch (_brain.Data.firePattern)
            {
                case ProjectileFirePattern.Single:
                    FireProjectile(direction);
                    break;

                case ProjectileFirePattern.Burst:
                    StartBurst(direction);
                    break;

                case ProjectileFirePattern.Spread:
                    FireSpread(direction);
                    break;

                case ProjectileFirePattern.Circle:
                    FireCircle(direction);
                    break;
            }

            RuntimePerfTraceLogger.RecordFireEvent(
                _brain.Data,
                requestedProjectiles,
                RuntimePerfTraceLogger.Timestamp() - fireStart);
        }

        private int GetFireRequestCount()
        {
            switch (_brain.Data.firePattern)
            {
                case ProjectileFirePattern.Burst:
                case ProjectileFirePattern.Spread:
                case ProjectileFirePattern.Circle:
                    return Mathf.Max(1, _brain.Data.projectileCount);

                default:
                    return 1;
            }
        }

        private void StartBurst(Vector2 direction)
        {
            int count = Mathf.Max(1, _brain.Data.projectileCount);
            FireProjectile(direction);
            _pendingBurstShots = count - 1;
            _burstTimer = Mathf.Max(0f, _brain.Data.burstInterval);
        }

        private void FireSpread(Vector2 direction)
        {
            int count = Mathf.Max(1, _brain.Data.projectileCount);
            if (count == 1)
            {
                FireProjectile(direction);
                return;
            }

            float startAngle = -_brain.Data.spreadAngle * 0.5f;
            float step = _brain.Data.spreadAngle / (count - 1);
            for (int i = 0; i < count; i++)
                FireProjectile(Rotate(direction, startAngle + step * i));
        }

        private void FireCircle(Vector2 direction)
        {
            int count = Mathf.Max(1, _brain.Data.projectileCount);
            if (count == 1)
            {
                FireProjectile(direction);
                return;
            }

            float step = 360f / count;
            for (int i = 0; i < count; i++)
                FireProjectile(Rotate(direction, step * i));
        }

        private void FireProjectile(Vector2 direction)
        {
            if (_brain.Data.projectilePrefab == null)
            {
                if (!_warnedMissingProjectile)
                {
                    Debug.LogWarning($"[EnemyBrain] {_brain.Data.enemyName}: Ranged projectilePrefab is missing.");
                    _warnedMissingProjectile = true;
                }
                return;
            }

            ProjectileController projectile = ProjectilePool.Instance.Get(
                _brain.Data.projectilePrefab,
                _brain.transform.position,
                Quaternion.identity);
            if (projectile == null) return;

            int damage = _brain.Data.projectileDamage > 0
                ? _brain.Data.projectileDamage
                : _brain.Data.attack;
            projectile.Initialize(
                direction,
                damage,
                _brain.Data.projectileSpeed,
                _brain.Data.projectileLifetime,
                _brain.Data.projectileWallHitMode,
                _brain.Data.projectileMaxBounceCount,
                _brain.Enemy);
        }

        private static Vector2 Rotate(Vector2 direction, float degrees)
        {
            if (direction.sqrMagnitude <= 0.0001f)
                direction = Vector2.down;

            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(
                direction.x * cos - direction.y * sin,
                direction.x * sin + direction.y * cos).normalized;
        }

        protected virtual void ApplyDamage()
        {
            IDamageable target = _brain.Target.Damageable;
            if (target == null || !target.IsAlive) return;

            target.TakeDamage(_brain.Data.attack);
        }
    }

    private sealed class IdleState : IEnemyState
    {
        private readonly EnemyBrain _brain;

        public IdleState(EnemyBrain brain)
        {
            _brain = brain;
        }

        public void OnEnter()
        {
            _brain.StopMoving();
        }

        public void Tick(float sqrDistanceToTarget)
        {
            _brain.StopMoving();

            if (_brain.Target.HasTarget && sqrDistanceToTarget <= _brain.Target.DetectRangeSqr)
                _brain.ChangeState(EnemyAIStateId.Chase);
        }

        public void OnExit()
        {
        }
    }

    private sealed class AttackState : IEnemyState
    {
        private readonly EnemyBrain _brain;

        public AttackState(EnemyBrain brain)
        {
            _brain = brain;
        }

        public void OnEnter()
        {
            _brain.Action.BeginAttack();
        }

        public void Tick(float sqrDistanceToTarget)
        {
            if (_brain.Action.TickAttack(sqrDistanceToTarget))
                _brain.ChangeState(EnemyAIStateId.Chase);
        }

        public void OnExit()
        {
        }
    }
}
