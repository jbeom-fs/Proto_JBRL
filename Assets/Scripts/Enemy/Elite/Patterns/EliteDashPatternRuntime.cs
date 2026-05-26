using UnityEngine;

public sealed class EliteDashPatternRuntime : ElitePatternRuntime
{
    private enum Phase
    {
        Windup,
        Dash,
        Recovery
    }

    private readonly EliteDashPatternData _data;
    private ElitePatternContext _context;
    private Phase _phase;
    private Vector2 _direction = Vector2.down;
    private float _timer;
    private bool _hasHitPlayer;
    private bool _unlockFacing;

    public EliteDashPatternRuntime(EliteDashPatternData data)
    {
        _data = data;
    }

    public override void Start(ElitePatternContext context)
    {
        _context = context;
        IsFinished = false;
        _hasHitPlayer = false;

        if (!CanRun())
        {
            Finish();
            return;
        }

        _direction = ResolveDirection();
        _context.Brain.StopMoving();
        if (_data.LockFacingDuringDash)
        {
            _context.Animation?.LockSpecialFacing(_direction);
            _unlockFacing = true;
        }

        _context.Animation?.PlayEliteAnimation(_data.WindupAnimation, _context.Target.position);
        _timer = _data.Windup;
        _phase = Phase.Windup;

        if (_timer <= 0f)
            StartDash();
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
        _context.Animation?.PlayEliteAnimation(_data.DashAnimation, _context.Target.position);
        _timer = _data.DashDuration;
        _phase = Phase.Dash;

        if (_timer <= 0f)
            StartRecovery();
    }

    private void TickDash(float deltaTime)
    {
        if (_data.LockFacingDuringDash)
            _context.Animation?.LockSpecialFacing(_direction);

        TryMove(deltaTime);
        TryApplyDamage();

        if (_timer > 0f)
        {
            _timer -= deltaTime;
            if (_timer > 0f)
                return;
        }

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

    private void TryMove(float deltaTime)
    {
        float step = _data.DashSpeed * deltaTime;
        if (step <= 0f)
            return;

        Vector3 next = _context.SelfTransform.position + (Vector3)(_direction * step);
        if (_data.StopOnWall && !CanOccupy(next))
        {
            StartRecovery();
            return;
        }

        _context.SelfTransform.position = next;
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

    private Vector2 ResolveDirection()
    {
        if (_context == null || _context.Target == null || _context.SelfTransform == null)
            return _direction.sqrMagnitude > 0.0001f ? _direction : Vector2.down;

        Vector2 direction = _context.Target.position - _context.SelfTransform.position;
        if (direction.sqrMagnitude <= 0.0001f)
            return _direction.sqrMagnitude > 0.0001f ? _direction : Vector2.down;

        return direction.normalized;
    }

    private bool CanOccupy(Vector3 position)
    {
        if (_context.DungeonManager == null)
            return true;

        float radius = _context.Enemy != null
            ? _context.Enemy.CollisionFootprintRadius
            : 0.32f;
        return WorldEnvironmentQuery.IsFootprintWalkable(position, radius);
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
