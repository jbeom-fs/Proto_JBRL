using UnityEngine;

/// <summary>
/// 공격 쿨다운, 선딜레이, 피해 적용을 담당합니다.
/// 보스는 이 핸들러를 상속해 패턴 큐, 페이즈 전환, 광역 공격 등을 추가할 수 있습니다.
/// EnemyBrain.TriggerAttackAnimation은 protected internal로 노출되어 외부 파일에서도 호출 가능합니다.
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
    private bool _windupFired;
    private bool _warnedMissingProjectile;
    private readonly ProjectileFireService _projectileFireService = new();

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
        ProjectileFireRequest request = CreateProjectileFireRequest(direction);
        int requestedProjectiles = ProjectileFireService.GetProjectileRequestCount(request);
        if (_brain.Data.projectilePrefab == null)
        {
            if (!_warnedMissingProjectile)
            {
                Debug.LogWarning($"[EnemyBrain] {_brain.Data.enemyName}: Ranged projectilePrefab is missing.");
                _warnedMissingProjectile = true;
            }
        }
        else
        {
            _projectileFireService.Fire(request);
        }

        RuntimePerfTraceLogger.RecordFireEvent(
            _brain.Data,
            requestedProjectiles,
            RuntimePerfTraceLogger.Timestamp() - fireStart);
    }

    private ProjectileFireRequest CreateProjectileFireRequest(Vector2 direction)
    {
        int damage = _brain.Data.projectileDamage > 0
            ? _brain.Data.projectileDamage
            : _brain.Data.attack;

        return new ProjectileFireRequest
        {
            ProjectilePrefab = _brain.Data.projectilePrefab,
            OriginTransform = _brain.transform,
            CoroutineRunner = _brain,
            Caster = _brain.Enemy,
            Owner = _brain.Enemy,
            Direction = direction,
            Damage = damage,
            Speed = _brain.Data.projectileSpeed,
            Lifetime = _brain.Data.projectileLifetime,
            ProjectileCount = _brain.Data.projectileCount,
            SpreadAngle = _brain.Data.spreadAngle,
            FirePattern = _brain.Data.firePattern,
            WallHitMode = _brain.Data.projectileWallHitMode,
            TargetHitMode = ProjectileTargetHitMode.DestroyOnHit,
            TargetMode = ProjectileController.TargetMode.Player,
            MaxBounceCount = _brain.Data.projectileMaxBounceCount,
            SpawnOffset = 0f,
            BurstInterval = _brain.Data.burstInterval
        };
    }

    protected virtual void ApplyDamage()
    {
        IDamageable target = _brain.Target.Damageable;
        if (target == null || !target.IsAlive) return;

        target.TakeDamage(_brain.Data.attack);
    }
}
