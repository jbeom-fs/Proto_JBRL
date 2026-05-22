using UnityEngine;

public sealed class EliteProjectilePatternRuntime : ElitePatternRuntime
{
    private enum Phase
    {
        Windup,
        Burst,
        Recovery
    }

    private readonly EliteProjectilePatternData _data;
    private ElitePatternContext _context;
    private Phase _phase;
    private Vector2 _aimDirection = Vector2.down;
    private float _timer;
    private int _remainingBurstShots;
    private bool _unlockFacing;
    private bool _warnedMissingProjectile;

    public EliteProjectilePatternRuntime(EliteProjectilePatternData data)
    {
        _data = data;
    }

    public override void Start(ElitePatternContext context)
    {
        _context = context;
        IsFinished = false;

        if (!CanRun())
        {
            Finish();
            return;
        }

        _aimDirection = ResolveAimDirection();
        _context.Brain.StopMoving();
        _context.Animation?.LockSpecialFacing(_aimDirection);
        _unlockFacing = true;
        _context.Animation?.PlayEliteAnimation(_data.WindupAnimation, _context.Target != null ? _context.Target.position : _context.SelfTransform.position);

        _timer = _data.WindupDuration;
        _phase = Phase.Windup;

        if (_timer <= 0f)
            FireOrStartBurst();
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

            case Phase.Burst:
                TickBurst(deltaTime);
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
        _aimDirection = ResolveAimDirection();
        _context.Animation?.LockSpecialFacing(_aimDirection);

        if (_timer > 0f)
        {
            _timer -= deltaTime;
            if (_timer > 0f)
                return;
        }

        FireOrStartBurst();
    }

    private void TickBurst(float deltaTime)
    {
        if (_remainingBurstShots <= 0)
        {
            StartRecovery();
            return;
        }

        _timer -= deltaTime;
        if (_timer > 0f)
            return;

        FireSingle(_aimDirection);
        _remainingBurstShots--;
        _timer = _data.BurstInterval;
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

    private void FireOrStartBurst()
    {
        _aimDirection = ResolveAimDirection();
        _context.Animation?.LockSpecialFacing(_aimDirection);
        _context.Animation?.PlayEliteAnimation(_data.FireAnimation, _context.Target != null ? _context.Target.position : _context.SelfTransform.position);

        if (_data.FirePattern == ProjectileFirePattern.Burst)
        {
            FireSingle(_aimDirection);
            _remainingBurstShots = Mathf.Max(1, _data.ProjectileCount) - 1;
            if (_remainingBurstShots > 0)
            {
                _timer = _data.BurstInterval;
                _phase = Phase.Burst;
                return;
            }
        }
        else
        {
            FirePattern(_aimDirection, _data.FirePattern);
        }

        StartRecovery();
    }

    private void StartRecovery()
    {
        _context.Animation?.PlayEliteAnimation(_data.RecoveryAnimation, _context.Target != null ? _context.Target.position : _context.SelfTransform.position);
        _timer = _data.RecoveryDuration;
        _phase = Phase.Recovery;

        if (_timer <= 0f)
            Finish();
    }

    private void FireSingle(Vector2 direction)
    {
        FirePattern(direction, ProjectileFirePattern.Single);
    }

    private void FirePattern(Vector2 direction, ProjectileFirePattern pattern)
    {
        if (_data.ProjectilePrefab == null)
        {
            if (!_warnedMissingProjectile)
            {
                Debug.LogWarning($"[EliteProjectilePattern] {_data.name}: projectilePrefab is missing.", _data);
                _warnedMissingProjectile = true;
            }
            return;
        }

        ProjectileFireRequest request = CreateRequest(direction, pattern);
        _context.ProjectileFireService?.Fire(request);
    }

    private ProjectileFireRequest CreateRequest(Vector2 direction, ProjectileFirePattern pattern)
    {
        EnemyAttackImpactData impact = _data.Impact;
        int damage = _data.Damage > 0
            ? _data.Damage
            : (_context.Data != null ? _context.Data.attack : 1);

        return new ProjectileFireRequest
        {
            ProjectilePrefab = _data.ProjectilePrefab,
            OriginTransform = _context.SelfTransform,
            CoroutineRunner = _context.CoroutineRunner,
            Caster = _context.Enemy,
            Owner = _context.Enemy,
            Direction = direction,
            Damage = damage,
            Speed = _data.ProjectileSpeed,
            Lifetime = _data.ProjectileLifetime,
            ProjectileCount = _data.ProjectileCount,
            SpreadAngle = _data.SpreadAngle,
            FirePattern = pattern,
            WallHitMode = _data.WallHitMode,
            TargetHitMode = ProjectileTargetHitMode.DestroyOnHit,
            TargetMode = ProjectileController.TargetMode.Player,
            MaxBounceCount = _data.MaxBounceCount,
            SpawnOffset = 0f,
            BurstInterval = _data.BurstInterval,
            KnockbackForce = impact.knockbackForce,
            KnockbackDuration = impact.knockbackDuration,
            SlowPercentage = impact.EffectiveSlowMultiplier,
            SlowDuration = impact.slowDuration,
            StunDuration = impact.stunDuration
        };
    }

    private Vector2 ResolveAimDirection()
    {
        if (_context == null || _context.SelfTransform == null || _context.Target == null)
            return _aimDirection.sqrMagnitude > 0.0001f ? _aimDirection : Vector2.down;

        Vector2 direction = _context.Target.position - _context.SelfTransform.position;
        if (direction.sqrMagnitude <= 0.0001f)
            return _aimDirection.sqrMagnitude > 0.0001f ? _aimDirection : Vector2.down;

        return direction.normalized;
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
               _context.ProjectileFireService != null &&
               _context.HasLiveTarget;
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
