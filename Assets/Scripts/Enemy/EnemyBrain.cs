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

    [Header("Dependencies")]
    public DungeonManager dungeonManager;
    public Transform player;

    [Header("Pathfinding")]
    [Tooltip("ChaseState에서 A* 경로를 다시 계산하는 주기입니다. 매 프레임 탐색을 막아 CPU/GC 부담을 줄입니다.")]
    public float pathUpdateInterval = 0.3f;

    [Tooltip("웨이포인트에 도착했다고 판단하는 거리입니다.")]
    public float waypointReachDistance = 0.08f;

    [Header("Collision")]
    [Range(0.05f, 0.49f)]
    public float collisionRadius = 0.2f;

    [Header("Animation")]
    public Animator animator;

    private EnemyController _enemy;
    private EnemyData _data;
    private SpriteRenderer _spriteRenderer;

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

    public MovementHandler Movement { get; private set; }
    public TargetHandler Target { get; private set; }
    public ActionHandler Action { get; private set; }

    public Vector2Int GridPosition => Movement != null ? Movement.GridPosition : Vector2Int.zero;
    public Vector2Int PlayerGridPosition => Target != null ? Target.TargetGridPosition : Vector2Int.zero;

    protected virtual void Awake()
    {
        _enemy = GetComponent<EnemyController>();
        _spriteRenderer = GetComponent<SpriteRenderer>();

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

    protected virtual void Update()
    {
        if (_enemy == null || !_enemy.IsAlive) return;
        if (!TryCacheData()) return;

        // CheckRoomEntry가 복도에서 조기 종료하더라도 AI는 매 프레임 실제 월드 좌표를 그리드로 변환합니다.
        // 따라서 플레이어가 ROOM 밖 CORRIDOR에 있어도 목표 좌표가 끊기지 않습니다.
        if (!Target.RefreshTarget())
        {
            StopMoving();
            return;
        }

        Action.TickCooldown(Time.deltaTime);

        float sqrDistance = Target.SqrDistanceToTarget;
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
        private float _tileSize = 1f;

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

            return MoveWithCollision(dir.normalized);
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
            float step = _brain.Data.moveSpeed * Time.deltaTime;
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

        private bool CanMoveTo(Vector3 pos)
        {
            if (_brain.dungeonManager == null) return true;

            float radius = _tileSize * _brain.collisionRadius;
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
        private float _detectRangeSqr;

        public TargetHandler(EnemyBrain brain)
        {
            _brain = brain;
        }

        public bool HasTarget => _brain.player != null;
        public IDamageable Damageable => _damageable;
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
                _damageable = _brain.player.GetComponent<IDamageable>();

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
            _damageable = playerObject.GetComponent<IDamageable>();
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
        private float _attackCooldownTimer;
        private float _windupTimer;
        private bool _windupFired;

        public ActionHandler(EnemyBrain brain)
        {
            _brain = brain;
        }

        public virtual void RecalculateRanges()
        {
            if (_brain.Data == null) return;
            _attackRangeSqr = _brain.Data.attackRange * _brain.Data.attackRange;
        }

        public virtual void TickCooldown(float deltaTime)
        {
            _attackCooldownTimer -= deltaTime;
        }

        public virtual bool CanAttack(float sqrDistanceToTarget)
        {
            return sqrDistanceToTarget <= _attackRangeSqr && _attackCooldownTimer <= 0f;
        }

        public virtual void BeginAttack()
        {
            _windupTimer = _brain.Data.attackWindup;
            _windupFired = false;
            _brain.TriggerAttackAnimation();
        }

        public virtual bool TickAttack(float sqrDistanceToTarget)
        {
            _brain.StopMoving();
            _windupTimer -= Time.deltaTime;

            if (_windupFired || _windupTimer > 0f)
                return false;

            _windupFired = true;

            if (sqrDistanceToTarget <= _attackRangeSqr)
                ApplyDamage();

            _attackCooldownTimer = _brain.Data.attackCooldown;
            return true;
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
            _brain.StopMoving();
            _brain.Action.BeginAttack();
        }

        public void Tick(float sqrDistanceToTarget)
        {
            if (_brain.Action.TickAttack(sqrDistanceToTarget))
                _brain.ChangeState(EnemyAIStateId.Chase);
        }

        public void OnExit()
        {
            _brain.StopMoving();
        }
    }
}
