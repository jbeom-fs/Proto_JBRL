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

public enum EnemySpecialAnimationType
{
    Charge,
    Rush,
    Jump,
    Land
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
    private static readonly int AnimMovingHash = Animator.StringToHash(ANIM_MOVING);
    private static readonly int AnimAttackHash = Animator.StringToHash(ANIM_ATTACK);

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
    private ElitePatternRunner _elitePatternRunner;

    private IEnemyState _idleState;
    private IEnemyState _chaseState;
    private IEnemyState _attackState;
    private IEnemyState _currentState;
    private EnemyAIStateId _currentStateId = EnemyAIStateId.Idle;

    private bool _animParamsScanned;
    private bool _hasMovingParam;
    private bool _hasAttackParam;
    private bool _lastMovingAnimValue;
    private bool _hasSetMovingAnim;
    private bool _warnedMissingElitePatternRunner;

    public EnemyController Enemy => _enemy;
    public EnemyData Data => _data;
    public EnemyAIStateId CurrentState => _currentStateId;
    public DungeonData DungeonData => dungeonManager != null ? dungeonManager.Data : null;
    public float CurrentMoveSpeed => Data != null && Enemy != null
        ? (Data.isStationary ? 0f : Data.moveSpeed * Enemy.MoveSpeedMultiplier)
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
        _elitePatternRunner = GetComponent<ElitePatternRunner>();

        Movement = CreateMovementHandler();
        Target = CreateTargetHandler();
        Action = CreateActionHandler();
    }

    protected virtual void Start()
    {
        TryCacheData();
        Movement.Initialize();
        Target.RefreshTarget();
        _elitePatternRunner?.Initialize(this);

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
        _warnedMissingElitePatternRunner = false;
        Movement?.Initialize();
        Movement?.ResetRuntimeState();
        Target?.RefreshTarget();
        Action?.ResetRuntimeState();
        _elitePatternRunner?.ResetRuntimeState();
        _animationController?.ResetAnimationState();
        _hasSetMovingAnim = false;
        _lastMovingAnimValue = false;

        if (_idleState != null)
        {
            _currentState?.OnExit();
            _currentState = _idleState;
            _currentStateId = EnemyAIStateId.Idle;
            _currentState.OnEnter();
        }
    }

    public virtual void HandleDeathStarted()
    {
        StopMoving();
        Action?.ResetRuntimeState();
        _elitePatternRunner?.ResetRuntimeState();
        UnlockSpecialFacing();
    }

    protected virtual void Update()
    {
        if (_enemy == null || _enemy.IsDead || !_enemy.IsAlive) return;
        if (!TryCacheData()) return;

        if (_enemy.IsKnockbackLocked)
        {
            StopMoving();
            return;
        }

        if (_enemy.IsStunned)
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
        _elitePatternRunner?.Tick(Time.deltaTime);
        if (_elitePatternRunner != null && _elitePatternRunner.IsRunning)
            return;

        Action.TickBehavior(sqrDistance);
        _currentState.Tick(sqrDistance);
        TryFaceTargetWhileChasing();
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
        if (Enemy != null && Enemy.IsStunned)
            return false;

        if (Data != null &&
            Data.IsElite &&
            Data.behaviorType == EnemyBehaviorType.Ranged)
        {
            WarnMissingElitePatternRunner();
            return false;
        }

        return Action.CanAttack(sqrDistanceToPlayer);
    }

    public bool ShouldKeepChasing(float sqrDistanceToPlayer)
    {
        return Target.HasTarget && sqrDistanceToPlayer <= Target.DetectRangeSqr;
    }

    public void DirectMoveToPlayer()
    {
        if (Data != null && Data.isStationary)
        {
            StopMoving();
            return;
        }

        if (!Target.HasTarget)
        {
            StopMoving();
            return;
        }

        MoveToward(Target.TargetPosition);
    }

    public void MoveToward(Vector3 target)
    {
        if (Data != null && Data.isStationary)
        {
            StopMoving();
            return;
        }

        bool moved = Movement.MoveToward(target);
        SetAnimBool(ANIM_MOVING, moved);

        float dirX = target.x - transform.position.x;
        if (TryFaceTargetWhileChasing())
            return;

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
        _warnedMissingElitePatternRunner = false;
        Target.RecalculateRanges();
        Action.RecalculateRanges();
        _elitePatternRunner?.Initialize(this);
        WarnMissingElitePatternRunner();
        return true;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void WarnMissingElitePatternRunner()
    {
        if (_data == null || !_data.IsElite || _data.ElitePatternSet == null || _elitePatternRunner != null)
            return;
        if (_warnedMissingElitePatternRunner)
            return;

        _warnedMissingElitePatternRunner = true;

        Debug.LogWarning(
            $"[EnemyBrain] {_data.enemyName}: ElitePatternRunner is missing. Elite patterns will not run.",
            this);
    }

    /// <summary>
    /// 외부 파일로 분리된 ActionHandler가 호출하므로 protected internal로 노출합니다.
    /// 보스 등 파생 클래스가 다른 어셈블리에 있어도 override할 수 있습니다.
    /// </summary>
    protected internal virtual void TriggerAttackAnimation()
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

    protected internal virtual void TriggerSpecialAnimation(EnemySpecialAnimationType animationType)
    {
        switch (animationType)
        {
            case EnemySpecialAnimationType.Charge:
                if (Target != null && Target.HasTarget)
                    _animationController?.PlayCharge(Target.TargetPosition);
                else
                    _animationController?.PlayAttack();
                break;

            case EnemySpecialAnimationType.Rush:
                _animationController?.PlayRush();
                break;

            case EnemySpecialAnimationType.Jump:
                _animationController?.PlayJump();
                break;

            case EnemySpecialAnimationType.Land:
                _animationController?.PlayLand();
                break;
        }

        if (animationType == EnemySpecialAnimationType.Charge)
            SetAnimTrigger(ANIM_ATTACK);
    }

    protected internal virtual void LockSpecialFacing(Vector2 direction)
    {
        _animationController?.LockFacing(direction);
    }

    protected internal virtual void UnlockSpecialFacing()
    {
        _animationController?.UnlockFacing();
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

    private bool TryFaceTargetWhileChasing()
    {
        if (_animationController == null || !_animationController.FaceTargetWhileChasing)
            return false;

        if (Target == null || !Target.HasTarget)
            return false;

        if (Enemy == null || Enemy.IsDead || !Enemy.IsAlive)
            return false;

        _animationController.FacePosition(Target.TargetPosition);
        return true;
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
        if (param != ANIM_MOVING) return;
        if (!_hasMovingParam) return;
        if (_hasSetMovingAnim && _lastMovingAnimValue == value) return;

        animator.SetBool(AnimMovingHash, value);
        _lastMovingAnimValue = value;
        _hasSetMovingAnim = true;
    }

    private void SetAnimTrigger(string param)
    {
        if (animator == null) return;

        EnsureAnimScanned();
        if (param != ANIM_ATTACK) return;
        if (!_hasAttackParam) return;

        animator.SetTrigger(AnimAttackHash);
    }

}
