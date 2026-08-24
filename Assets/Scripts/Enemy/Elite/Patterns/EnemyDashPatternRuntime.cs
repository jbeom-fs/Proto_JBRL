using UnityEngine;

public sealed class EnemyDashPatternRuntime : EnemyPatternRuntime
{
    private enum Phase
    {
        Windup,
        Dash,
        Recovery
    }

    private readonly EnemyDashPatternData _data;
    private EnemyPatternContext _context;
    private Phase _phase;
    private Vector2 _direction = Vector2.down;
    private Vector3 _targetPosition;
    private float _timer;
    private bool _hasHitPlayer;
    private bool _unlockFacing;

    public EnemyDashPatternRuntime(EnemyDashPatternData data)
    {
        _data = data;
    }

    public override bool Start(EnemyPatternContext context)
    {
        _context = context;
        IsFinished = false;
        _phase = Phase.Windup;
        _direction = Vector2.down;
        _targetPosition = default;
        _timer = 0f;
        _hasHitPlayer = false;
        _unlockFacing = false;

        if (!CanRun() || !TryResolveDashTarget(out _targetPosition))
        {
            Finish();
            return false;
        }

        _direction = ResolveDirection(_targetPosition);
        _context.Brain.StopMoving();
        if (_data.LockFacingDuringDash)
        {
            _context.Animation?.LockSpecialFacing(_direction);
            _unlockFacing = true;
        }

        _context.Animation?.PlayPatternAnimation(_data.WindupAnimation, _context.Target.position);
        _timer = _data.Windup;
        _phase = Phase.Windup;

        if (_timer <= 0f)
            StartDash();

        return true;
    }

    public override void Tick(float deltaTime)
    {
        if (IsFinished)
            return;

        if (!CanRun())
        {
            Finish();
            return;
        }

        _context.Brain.StopMoving();

        switch (_phase)
        {
            case Phase.Windup:
                TickWindup(deltaTime);
                break;

            case Phase.Dash:
                TickDash(deltaTime);
                break;

            case Phase.Recovery:
                TickRecovery(deltaTime);
                break;
        }
    }

    public override void Cancel()
    {
        Cleanup();
        base.Cancel();
    }

    private void TickWindup(float deltaTime)
    {
        if (_data.LockFacingDuringDash)
            _context.Animation?.LockSpecialFacing(_direction);

        if (_timer > 0f)
        {
            _timer -= deltaTime;
            if (_timer > 0f)
                return;
        }

        StartDash();
    }

    private void StartDash()
    {
        _context.Animation?.PlayPatternAnimation(_data.DashAnimation, _targetPosition);
        _timer = 0f;
        _phase = Phase.Dash;

        if (_data.DashSpeed <= 0f || HasReachedTarget())
            StartRecovery();
    }

    private void TickDash(float deltaTime)
    {
        if (_data.LockFacingDuringDash)
            _context.Animation?.LockSpecialFacing(_direction);

        bool dashFinished = TryMove(deltaTime);
        TryApplyDamage();

        if (dashFinished)
            StartRecovery();
    }

    private void TickRecovery(float deltaTime)
    {
        if (_timer > 0f)
        {
            _timer -= deltaTime;
            if (_timer > 0f)
                return;
        }

        Finish();
    }

    private bool TryMove(float deltaTime)
    {
        float step = _data.DashSpeed * deltaTime;
        if (step <= 0f)
            return false;

        Vector3 current = _context.SelfTransform.position;
        Vector3 toTarget = _targetPosition - current;
        float remaining = toTarget.magnitude;
        if (remaining <= 0.001f)
            return true;

        Vector3 direction = toTarget / remaining;
        _direction = (Vector2)direction;

        Vector3 next = step >= remaining
            ? _targetPosition
            : current + direction * step;

        if (_data.StopOnWall && !CanOccupy(next))
        {
            return true;
        }

        _context.SelfTransform.position = next;
        return step >= remaining || HasReachedTarget();
    }

    private void TryApplyDamage()
    {
        if (_hasHitPlayer || _context.Brain.Target == null || _context.Brain.Target.Damageable == null)
            return;

        IDamageable target = _context.Brain.Target.Damageable;
        if (!target.IsAlive || !IsTargetWithinRadius(_data.HitRadius))
            return;

        int damage = _data.Damage > 0 ? _data.Damage : (_context.Data != null ? _context.Data.attack : 1);
        target.TakeDamage(damage);
        _hasHitPlayer = true;
    }

    private bool IsTargetWithinRadius(float radius)
    {
        Collider2D self = _context.Collider;
        Collider2D target = _context.Brain.Target.TargetCollider;
        if (self != null && target != null && self.enabled && target.enabled)
        {
            ColliderDistance2D distance = self.Distance(target);
            return distance.isOverlapped || distance.distance <= radius;
        }

        Vector2 delta = _context.Brain.Target.TargetPosition - _context.SelfTransform.position;
        return delta.sqrMagnitude <= radius * radius;
    }

    private bool TryResolveDashTarget(out Vector3 targetPosition)
    {
        if (_context == null || _context.Target == null || _context.SelfTransform == null)
        {
            targetPosition = default;
            return false;
        }

        Vector3 selfPosition = _context.SelfTransform.position;
        targetPosition = selfPosition;
        Vector3 desired = _context.Target.position;
        desired.z = selfPosition.z;
        if (CanOccupy(desired))
        {
            targetPosition = desired;
            return true;
        }

        float footprintRadius = GetFootprintRadius();
        float maxDistanceFromOrigin = Vector3.Distance(selfPosition, desired) + Mathf.Max(1f, footprintRadius);
        if (WorldEnvironmentQuery.TryFindNearestWalkable(
                desired,
                selfPosition,
                maxDistanceFromOrigin,
                footprintRadius,
                3,
                out targetPosition))
        {
            targetPosition.z = selfPosition.z;
            return true;
        }

        if (WalkabilityQuery.TryGetNearestWalkableWorldPosition(desired, out targetPosition))
        {
            targetPosition.z = selfPosition.z;
            return CanOccupy(targetPosition);
        }

        return false;
    }

    private Vector2 ResolveDirection(Vector3 targetPosition)
    {
        if (_context == null || _context.SelfTransform == null)
            return _direction.sqrMagnitude > 0.0001f ? _direction : Vector2.down;

        Vector2 direction = targetPosition - _context.SelfTransform.position;
        if (direction.sqrMagnitude <= 0.0001f)
            return _direction.sqrMagnitude > 0.0001f ? _direction : Vector2.down;

        return direction.normalized;
    }

    private bool CanOccupy(Vector3 position)
    {
        if (_context.DungeonManager == null)
            return true;

        return WorldEnvironmentQuery.IsFootprintWalkable(position, GetFootprintRadius());
    }

    private float GetFootprintRadius()
    {
        return _context.Enemy != null
            ? _context.Enemy.CollisionFootprintRadius
            : 0.32f;
    }

    private bool HasReachedTarget()
    {
        if (_context == null || _context.SelfTransform == null)
            return true;

        return (_targetPosition - _context.SelfTransform.position).sqrMagnitude <= 0.000001f;
    }

    private bool CanRun()
    {
        return _data != null &&
               _context != null &&
               _context.Brain != null &&
               _context.Enemy != null &&
               _context.Enemy.IsAlive &&
               !_context.Enemy.IsDead &&
               _context.SelfTransform != null &&
               _context.HasLiveTarget;
    }

    private void StartRecovery()
    {
        _timer = _data.RecoveryDuration;
        _phase = Phase.Recovery;

        if (_timer <= 0f)
            Finish();
    }

    private void Finish()
    {
        Cleanup();
        IsFinished = true;
    }

    private void Cleanup()
    {
        if (_unlockFacing)
            _context?.Animation?.UnlockSpecialFacing();

        _unlockFacing = false;
    }
}
