using UnityEngine;

public sealed class EliteJumpPatternRuntime : ElitePatternRuntime
{
    private enum Phase
    {
        Windup,
        Jump,
        Impact,
        Recovery
    }

    private readonly EliteJumpPatternData _data;
    private ElitePatternContext _context;
    private Phase _phase;
    private Vector3 _startPosition;
    private Vector3 _targetPosition;
    private Vector2 _facingDirection = Vector2.down;
    private float _timer;
    private bool _appliedImpact;
    private bool _unlockFacing;

    public EliteJumpPatternRuntime(EliteJumpPatternData data)
    {
        _data = data;
    }

    public override void Start(ElitePatternContext context)
    {
        _context = context;
        IsFinished = false;
        _appliedImpact = false;

        if (!CanRun() || !TryResolveJumpTarget(out _targetPosition))
        {
            Finish();
            return;
        }

        _startPosition = _context.SelfTransform.position;
        _facingDirection = ResolveFacingDirection(_targetPosition);
        _context.Brain.StopMoving();

        if (_data.LockFacingDuringJump)
        {
            _context.Animation?.LockSpecialFacing(_facingDirection);
            _unlockFacing = true;
        }

        _context.Animation?.PlayEliteAnimation(_data.WindupAnimation, _targetPosition);
        _timer = _data.Windup;
        _phase = Phase.Windup;

        if (_timer <= 0f)
            StartJump();
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

            case Phase.Jump:
                TickJump(deltaTime);
                break;

            case Phase.Impact:
                ApplyImpactAndRecover();
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
        if (_data.LockFacingDuringJump)
            _context.Animation?.LockSpecialFacing(_facingDirection);

        if (_timer > 0f)
        {
            _timer -= deltaTime;
            if (_timer > 0f)
                return;
        }

        StartJump();
    }

    private void StartJump()
    {
        _startPosition = _context.SelfTransform.position;
        _context.Animation?.PlayEliteAnimation(_data.JumpAnimation, _targetPosition);
        _timer = 0f;
        _phase = Phase.Jump;

        if (_data.JumpDuration <= 0f)
        {
            _context.SelfTransform.position = _targetPosition;
            _phase = Phase.Impact;
        }
    }

    private void TickJump(float deltaTime)
    {
        if (_data.LockFacingDuringJump)
            _context.Animation?.LockSpecialFacing(_facingDirection);

        _timer += Mathf.Max(0f, deltaTime);
        float t = Mathf.Clamp01(_timer / _data.JumpDuration);
        _context.SelfTransform.position = Vector3.Lerp(_startPosition, _targetPosition, t);

        if (t >= 1f)
            _phase = Phase.Impact;
    }

    private void ApplyImpactAndRecover()
    {
        if (!_appliedImpact)
        {
            TryApplyImpactDamage();
            _appliedImpact = true;
        }

        _timer = _data.RecoveryDuration;
        _phase = Phase.Recovery;

        if (_timer <= 0f)
            Finish();
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

    private void TryApplyImpactDamage()
    {
        if (_context.Brain.Target == null || _context.Brain.Target.Damageable == null)
            return;

        IDamageable target = _context.Brain.Target.Damageable;
        if (!target.IsAlive || !IsTargetWithinRadius(_data.ImpactRadius))
            return;

        int damage = _data.ImpactDamage > 0 ? _data.ImpactDamage : (_context.Data != null ? _context.Data.attack : 1);
        target.TakeDamage(damage);
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

    private bool TryResolveJumpTarget(out Vector3 targetPosition)
    {
        targetPosition = _context.SelfTransform.position;
        DungeonManager dungeon = _context.DungeonManager;
        if (dungeon == null || dungeon.Data == null || _context.Target == null)
            return false;

        Vector2Int enemyGrid = dungeon.WorldToGrid(_context.SelfTransform.position);
        RoomInfo? currentRoom = dungeon.GetRoomAt(enemyGrid.x, enemyGrid.y);
        if (_data.StayInRoom && !currentRoom.HasValue)
            return false;

        Vector3 desired = ClampJumpDistance(_context.Target.position);
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
        Vector3 origin = _context.SelfTransform.position;
        Vector3 delta = desired - origin;
        float maxDistance = _data.MaxDistance;
        if (delta.sqrMagnitude <= maxDistance * maxDistance)
            return desired;

        return origin + delta.normalized * maxDistance;
    }

    private bool TryFindNearbyJumpTarget(DungeonManager dungeon, Vector2Int center, RoomInfo? room, out Vector3 targetPosition)
    {
        targetPosition = _context.SelfTransform.position;

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
        float maxDistance = _data.MaxDistance;
        if ((world - _context.SelfTransform.position).sqrMagnitude > maxDistance * maxDistance)
            return false;

        if (_data.StayInRoom)
            return room.HasValue && room.Value.Contains(grid.x, grid.y);

        return true;
    }

    private bool CanOccupy(Vector3 position)
    {
        if (_context.DungeonManager == null)
            return true;

        float radius = _context.Enemy != null
            ? _context.Enemy.CollisionFootprintRadius
            : 0.32f;
        return _context.DungeonManager.IsFootprintWalkable(position, radius);
    }

    private Vector2 ResolveFacingDirection(Vector3 targetPosition)
    {
        Vector2 direction = targetPosition - _context.SelfTransform.position;
        if (direction.sqrMagnitude <= 0.0001f)
            return _facingDirection.sqrMagnitude > 0.0001f ? _facingDirection : Vector2.down;

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
