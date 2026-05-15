using System.Collections.Generic;
using UnityEngine;

internal enum EnemySpecialAttackPhase
{
    None,
    Windup,
    Rush,
    Jump,
    Recovery
}

/// <summary>
/// Handles enemy attack timing and damage application.
/// Contact specials reuse the same AttackState entry point as ranged attacks.
/// </summary>
public class ActionHandler
{
    private readonly EnemyBrain _brain;
    private float _attackRangeSqr;
    private float _contactDamageRangeSqr;
    private float _specialAttackRangeSqr;
    private Collider2D _selfCollider;
    private float _attackCooldownTimer;
    private float _windupTimer;
    private float _recoveryTimer;
    private Vector2 _aimDirection = Vector2.down;
    private bool _windupFired;
    private bool _warnedMissingProjectile;
    private readonly ProjectileFireService _projectileFireService = new();
    private readonly HashSet<IDamageable> _rushHitTargets = new();

    private EnemySpecialAttackPhase _specialPhase;
    private float _specialTimer;
    private Vector2 _specialDirection = Vector2.down;
    private Vector3 _jumpStartPosition;
    private Vector3 _jumpTargetPosition;
    private bool _jumpReady;

    public ActionHandler(EnemyBrain brain)
    {
        _brain = brain;
    }

    public virtual void RecalculateRanges()
    {
        if (_brain.Data == null) return;
        _attackRangeSqr = _brain.Data.attackRange * _brain.Data.attackRange;
        _contactDamageRangeSqr = _brain.Data.contactDamageRadius * _brain.Data.contactDamageRadius;
        _specialAttackRangeSqr = _brain.Data.specialAttackRange * _brain.Data.specialAttackRange;
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
        _specialPhase = EnemySpecialAttackPhase.None;
        _specialTimer = 0f;
        _specialDirection = Vector2.down;
        _jumpStartPosition = default;
        _jumpTargetPosition = default;
        _jumpReady = false;
        _rushHitTargets.Clear();
        _brain.UnlockSpecialFacing();
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
        if (IsContactSpecialActive())
            return;

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
        if (_brain.Data == null)
            return false;

        if (_brain.Data.behaviorType == EnemyBehaviorType.Ranged)
            return sqrDistanceToTarget <= _attackRangeSqr && _attackCooldownTimer <= 0f;

        if (_brain.Data.behaviorType != EnemyBehaviorType.Contact)
            return false;

        return _brain.Data.specialAttackType != EnemySpecialAttackType.None &&
               _specialPhase == EnemySpecialAttackPhase.None &&
               sqrDistanceToTarget <= _specialAttackRangeSqr &&
               _attackCooldownTimer <= 0f;
    }

    public virtual void BeginAttack()
    {
        if (_brain.Data == null)
            return;

        if (_brain.Data.behaviorType == EnemyBehaviorType.Contact)
        {
            BeginContactSpecialAttack();
            return;
        }

        if (_brain.Data.behaviorType != EnemyBehaviorType.Ranged)
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
        if (_brain.Data == null)
            return true;

        if (_brain.Data.behaviorType == EnemyBehaviorType.Contact)
            return TickContactSpecialAttack();

        if (_brain.Data.behaviorType != EnemyBehaviorType.Ranged)
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

    private bool IsContactSpecialActive()
    {
        return _brain.Data != null &&
               _brain.Data.behaviorType == EnemyBehaviorType.Contact &&
               _specialPhase != EnemySpecialAttackPhase.None;
    }

    private void BeginContactSpecialAttack()
    {
        if (_brain.Data.specialAttackType == EnemySpecialAttackType.None)
            return;

        _specialDirection = ResolveAimDirection();
        _specialTimer = Mathf.Max(0f, _brain.Data.specialAttackWindup);
        _specialPhase = EnemySpecialAttackPhase.Windup;
        _jumpReady = _brain.Data.specialAttackType != EnemySpecialAttackType.Jump ||
                     TryResolveJumpTarget(out _jumpTargetPosition);
        _jumpStartPosition = _brain.transform.position;
        _rushHitTargets.Clear();
        _brain.TriggerSpecialAnimation(EnemySpecialAnimationType.Charge);
        _brain.StopMoving();

        if (!_jumpReady)
            CancelContactSpecialAttack();
    }

    private bool TickContactSpecialAttack()
    {
        switch (_specialPhase)
        {
            case EnemySpecialAttackPhase.Windup:
                return TickSpecialWindup();

            case EnemySpecialAttackPhase.Rush:
                return TickRush();

            case EnemySpecialAttackPhase.Jump:
                return TickJump();

            case EnemySpecialAttackPhase.Recovery:
                return TickSpecialRecovery();

            case EnemySpecialAttackPhase.None:
            default:
                return true;
        }
    }

    private bool TickSpecialWindup()
    {
        _brain.StopMoving();

        if (_specialTimer > 0f)
        {
            _specialTimer -= Time.deltaTime;
            if (_specialTimer > 0f)
                return false;
        }

        switch (_brain.Data.specialAttackType)
        {
            case EnemySpecialAttackType.Rush:
                StartRush();
                return false;

            case EnemySpecialAttackType.Jump:
                if (!_jumpReady)
                {
                    CancelContactSpecialAttack();
                    return true;
                }

                StartJump();
                return false;

            case EnemySpecialAttackType.None:
            default:
                ClearContactSpecialAttack();
                return true;
        }
    }

    private void StartRush()
    {
        _specialDirection = _specialDirection.sqrMagnitude > 0.0001f
            ? _specialDirection.normalized
            : Vector2.down;
        _specialTimer = Mathf.Max(0.01f, _brain.Data.rushDuration);
        _attackCooldownTimer = Mathf.Max(0f, _brain.Data.specialAttackCooldown);
        _rushHitTargets.Clear();
        _specialPhase = EnemySpecialAttackPhase.Rush;
        _brain.LockSpecialFacing(_specialDirection);
        _brain.TriggerSpecialAnimation(EnemySpecialAnimationType.Rush);
    }

    private bool TickRush()
    {
        float deltaTime = Time.deltaTime;
        _specialTimer -= deltaTime;

        float speed = Mathf.Max(0.01f, _brain.Data.rushSpeed);
        Vector3 next = _brain.transform.position + (Vector3)(_specialDirection * speed * deltaTime);
        if (!CanOccupy(next))
        {
            StartSpecialRecovery();
            return false;
        }

        _brain.transform.position = next;
        TryApplyRushDamage();

        if (_specialTimer <= 0f)
            StartSpecialRecovery();

        return false;
    }

    private void StartJump()
    {
        _jumpStartPosition = _brain.transform.position;
        _specialDirection = (_jumpTargetPosition - _jumpStartPosition).sqrMagnitude > 0.0001f
            ? ((Vector2)(_jumpTargetPosition - _jumpStartPosition)).normalized
            : ResolveAimDirection();
        _specialTimer = Mathf.Max(0.01f, _brain.Data.jumpDuration);
        _attackCooldownTimer = Mathf.Max(0f, _brain.Data.specialAttackCooldown);
        _specialPhase = EnemySpecialAttackPhase.Jump;
        _brain.LockSpecialFacing(_specialDirection);
        _brain.TriggerSpecialAnimation(EnemySpecialAnimationType.Jump);
    }

    private bool TickJump()
    {
        float duration = Mathf.Max(0.01f, _brain.Data.jumpDuration);
        _specialTimer -= Time.deltaTime;
        float t = 1f - Mathf.Clamp01(_specialTimer / duration);
        _brain.transform.position = Vector3.Lerp(_jumpStartPosition, _jumpTargetPosition, t);

        if (_specialTimer > 0f)
            return false;

        _brain.transform.position = _jumpTargetPosition;
        ApplyJumpImpactDamage();
        _brain.TriggerSpecialAnimation(EnemySpecialAnimationType.Land);
        StartSpecialRecovery();
        return false;
    }

    private bool TickSpecialRecovery()
    {
        _brain.StopMoving();

        if (_specialTimer > 0f)
        {
            _specialTimer -= Time.deltaTime;
            if (_specialTimer > 0f)
                return false;
        }

        ClearContactSpecialAttack();
        return true;
    }

    private void StartSpecialRecovery()
    {
        if (_specialPhase == EnemySpecialAttackPhase.Rush)
            _brain.UnlockSpecialFacing();

        _specialTimer = Mathf.Max(0f, _brain.Data.specialAttackRecovery);
        _specialPhase = EnemySpecialAttackPhase.Recovery;
        _brain.StopMoving();
    }

    private void CancelContactSpecialAttack()
    {
        _attackCooldownTimer = Mathf.Max(0.1f, _brain.Data.specialAttackCooldown);
        ClearContactSpecialAttack();
    }

    private void ClearContactSpecialAttack()
    {
        _specialPhase = EnemySpecialAttackPhase.None;
        _specialTimer = 0f;
        _jumpReady = false;
        _rushHitTargets.Clear();
        _brain.UnlockSpecialFacing();
    }

    private void TryApplyRushDamage()
    {
        IDamageable target = _brain.Target.Damageable;
        if (target == null || !target.IsAlive || _rushHitTargets.Contains(target))
            return;

        if (!IsTargetWithinRadius(_brain.Data.rushHitRadius))
            return;

        ApplyEnemyImpactToTarget(
            target,
            GetSpecialDamage(_brain.Data.rushDamage),
            _specialDirection,
            _brain.Data.rushImpact);
        _rushHitTargets.Add(target);
    }

    private void ApplyJumpImpactDamage()
    {
        IDamageable target = _brain.Target.Damageable;
        if (target == null || !target.IsAlive)
            return;

        if (!IsTargetWithinRadius(_brain.Data.jumpImpactRadius))
            return;

        Vector2 hitDirection = _brain.Target.TargetPosition - _brain.transform.position;
        ApplyEnemyImpactToTarget(
            target,
            GetSpecialDamage(_brain.Data.jumpDamage),
            hitDirection,
            _brain.Data.jumpImpact);
    }

    private static void ApplyEnemyImpactToTarget(
        IDamageable target,
        int damage,
        Vector2 hitDirection,
        EnemyAttackImpactData impact)
    {
        if (target is PlayerCombatController player)
        {
            player.ApplyEnemyCombatImpact(
                damage,
                hitDirection,
                impact.knockbackForce,
                impact.knockbackDuration,
                impact.EffectiveSlowMultiplier,
                impact.slowDuration,
                impact.stunDuration);
            return;
        }

        target.TakeDamage(damage);
    }

    private bool IsTargetWithinRadius(float radius)
    {
        float r = Mathf.Max(0.01f, radius);
        Collider2D self = ResolveSelfCollider();
        Collider2D targetCollider = _brain.Target.TargetCollider;
        if (self != null && targetCollider != null && self.enabled && targetCollider.enabled)
        {
            ColliderDistance2D distance = self.Distance(targetCollider);
            return distance.isOverlapped || distance.distance <= r;
        }

        return (_brain.Target.TargetPosition - _brain.transform.position).sqrMagnitude <= r * r;
    }

    private int GetSpecialDamage(int configuredDamage)
    {
        return configuredDamage > 0 ? configuredDamage : _brain.Data.attack;
    }

    private bool CanOccupy(Vector3 position)
    {
        DungeonManager dungeon = _brain.dungeonManager;
        if (dungeon == null) return true;

        float radius = _brain.Enemy != null
            ? _brain.Enemy.CollisionFootprintRadius
            : Mathf.Max(0.01f, _brain.collisionRadius);

        return dungeon.IsFootprintWalkable(position, radius);
    }

    private bool TryResolveJumpTarget(out Vector3 targetPosition)
    {
        targetPosition = _brain.transform.position;
        DungeonManager dungeon = _brain.dungeonManager;
        if (dungeon == null || dungeon.Data == null || !_brain.Target.HasTarget)
            return false;

        Vector2Int enemyGrid = dungeon.WorldToGrid(_brain.transform.position);
        RoomInfo? currentRoom = dungeon.GetRoomAt(enemyGrid.x, enemyGrid.y);
        if (_brain.Data.jumpStayInRoom && !currentRoom.HasValue)
            return false;

        Vector3 desired = ClampJumpDistance(_brain.Target.TargetPosition);
        Vector2Int desiredGrid = dungeon.WorldToGrid(desired);
        if (IsValidJumpGrid(dungeon, desiredGrid, currentRoom))
        {
            targetPosition = dungeon.GridToWorld(desiredGrid);
            return CanOccupy(targetPosition);
        }

        return TryFindNearbyJumpTarget(dungeon, desiredGrid, currentRoom, out targetPosition);
    }

    private Vector3 ClampJumpDistance(Vector3 desired)
    {
        Vector3 origin = _brain.transform.position;
        Vector3 delta = desired - origin;
        float maxDistance = Mathf.Max(0.01f, _brain.Data.jumpMaxDistance);
        if (delta.sqrMagnitude <= maxDistance * maxDistance)
            return desired;

        return origin + delta.normalized * maxDistance;
    }

    private bool TryFindNearbyJumpTarget(
        DungeonManager dungeon,
        Vector2Int center,
        RoomInfo? room,
        out Vector3 targetPosition)
    {
        targetPosition = _brain.transform.position;

        const int MaxSearchRadius = 3;
        for (int radius = 1; radius <= MaxSearchRadius; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius)
                        continue;

                    Vector2Int candidate = new Vector2Int(center.x + dx, center.y + dy);
                    if (!IsValidJumpGrid(dungeon, candidate, room))
                        continue;

                    Vector3 world = dungeon.GridToWorld(candidate);
                    if (!CanOccupy(world))
                        continue;

                    targetPosition = world;
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsValidJumpGrid(DungeonManager dungeon, Vector2Int grid, RoomInfo? room)
    {
        if (!dungeon.IsWalkable(grid.x, grid.y))
            return false;

        Vector3 world = dungeon.GridToWorld(grid);
        float maxDistance = Mathf.Max(0.01f, _brain.Data.jumpMaxDistance);
        if ((world - _brain.transform.position).sqrMagnitude > maxDistance * maxDistance)
            return false;

        if (_brain.Data.jumpStayInRoom)
            return room.HasValue && room.Value.Contains(grid.x, grid.y);

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
            BurstInterval = _brain.Data.burstInterval,
            KnockbackForce = _brain.Data.projectileImpact.knockbackForce,
            KnockbackDuration = _brain.Data.projectileImpact.knockbackDuration,
            SlowPercentage = _brain.Data.projectileImpact.EffectiveSlowMultiplier,
            SlowDuration = _brain.Data.projectileImpact.slowDuration,
            StunDuration = _brain.Data.projectileImpact.stunDuration
        };
    }

    protected virtual void ApplyDamage()
    {
        IDamageable target = _brain.Target.Damageable;
        if (target == null || !target.IsAlive) return;

        target.TakeDamage(_brain.Data.attack);
    }
}
