using UnityEngine;

public sealed class EnemyJumpPatternRuntime : EnemyPatternRuntime
{
    private enum Phase
    {
        Windup,
        Jump,
        Impact,
        Recovery
    }

    private readonly EnemyJumpPatternData _data;
    private EnemyPatternContext _context;
    private Phase _phase;
    private Vector3 _startPosition;
    private Vector3 _targetPosition;
    private float _totalJumpDistance;
    private Transform _visualRoot;
    private Vector3 _visualBaseLocalPosition;
    private Vector2 _facingDirection = Vector2.down;
    private float _timer;
    private bool _appliedImpact;
    private bool _hasVisualRoot;
    private bool _unlockFacing;

    public EnemyJumpPatternRuntime(EnemyJumpPatternData data)
    {
        _data = data;
    }

    public override bool Start(EnemyPatternContext context)
    {
        _context = context;
        IsFinished = false;
        _phase = Phase.Windup;
        _startPosition = default;
        _targetPosition = default;
        _totalJumpDistance = 0f;
        _visualRoot = null;
        _visualBaseLocalPosition = default;
        _facingDirection = Vector2.down;
        _timer = 0f;
        _appliedImpact = false;
        _hasVisualRoot = false;
        _unlockFacing = false;

        if (!CanRun() || !TryResolveJumpTarget(out _targetPosition))
        {
            Finish();
            return false;
        }

        CacheVisualRoot();
        _startPosition = _context.SelfTransform.position;
        _facingDirection = ResolveFacingDirection(_targetPosition);
        _context.Brain.StopMoving();

        if (_data.LockFacingDuringJump)
        {
            _context.Animation?.LockSpecialFacing(_facingDirection);
            _unlockFacing = true;
        }

        _context.Animation?.PlayPatternAnimation(_data.WindupAnimation, _targetPosition);
        _timer = _data.Windup;
        _phase = Phase.Windup;

        if (_timer <= 0f)
            StartJump();

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
        SetWalkGuardSuppressed(true);
        _startPosition = _context.SelfTransform.position;
        _totalJumpDistance = Vector3.Distance(_startPosition, _targetPosition);
        _context.Animation?.PlayPatternAnimation(_data.JumpAnimation, _targetPosition);
        _timer = 0f;
        _phase = Phase.Jump;
        ApplyVisualOffset(0f);

        if (_data.JumpSpeed <= 0f || _totalJumpDistance <= 0.001f)
            CompleteJumpMovement();
    }

    private void TickJump(float deltaTime)
    {
        if (_data.LockFacingDuringJump)
            _context.Animation?.LockSpecialFacing(_facingDirection);

        float step = _data.JumpSpeed * deltaTime;
        if (step <= 0f)
            return;

        Vector3 current = _context.SelfTransform.position;
        Vector3 toTarget = _targetPosition - current;
        float remaining = toTarget.magnitude;
        if (remaining <= 0.001f)
        {
            CompleteJumpMovement();
            return;
        }

        Vector3 next = step >= remaining
            ? _targetPosition
            : current + toTarget / remaining * step;

        _context.SelfTransform.position = next;
        ApplyVisualOffset(CalculateJumpProgress(next));

        if (step >= remaining || HasReachedTarget())
        {
            CompleteJumpMovement();
        }
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
        RestoreVisualOffset();

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
        if (dungeon == null || _context.Target == null)
            return false;

        Vector3 selfPosition = _context.SelfTransform.position;
        Vector3 desired = ClampJumpDistance(_context.Target.position);
        desired.z = selfPosition.z;

        // 등록된 Area(예: Elite Arena) 안에서는 room 개념이 없으므로 Area 기반 walkable spiral 검색을 사용합니다.
        // Dungeon이 활성화되어 있어도 enemy 위치가 Area 안이면 이 경로가 우선합니다.
        if (WorldEnvironmentQuery.IsInRegisteredArea(selfPosition))
        {
            float radius = _context.Enemy != null
                ? _context.Enemy.CollisionFootprintRadius
                : 0.32f;
            if (WorldEnvironmentQuery.TryFindNearestWalkable(
                    desired, selfPosition, _data.MaxDistance, radius, 3, out targetPosition))
            {
                targetPosition.z = selfPosition.z;
                return true;
            }

            return false;
        }

        if (dungeon.Data == null)
            return false;

        Vector2Int enemyGrid = dungeon.WorldToGrid(selfPosition);
        RoomInfo? currentRoom = dungeon.GetRoomAt(enemyGrid.x, enemyGrid.y);
        if (_data.StayInRoom && !currentRoom.HasValue)
            return false;

        Vector2Int desiredGrid = dungeon.WorldToGrid(desired);
        if (IsValidJumpGrid(dungeon, desiredGrid, currentRoom))
        {
            targetPosition = dungeon.GridToWorld(desiredGrid);
            targetPosition.z = selfPosition.z;
            return CanOccupy(targetPosition);
        }

        if (TryFindNearbyJumpTarget(dungeon, desiredGrid, currentRoom, out targetPosition))
        {
            targetPosition.z = selfPosition.z;
            return true;
        }

        return false;
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
        Vector3 world = dungeon.GridToWorld(grid);
        if (!WorldEnvironmentQuery.IsWalkable(world))
            return false;

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
        return WorldEnvironmentQuery.IsFootprintWalkable(position, radius);
    }

    private Vector2 ResolveFacingDirection(Vector3 targetPosition)
    {
        Vector2 direction = targetPosition - _context.SelfTransform.position;
        if (direction.sqrMagnitude <= 0.0001f)
            return _facingDirection.sqrMagnitude > 0.0001f ? _facingDirection : Vector2.down;

        return direction.normalized;
    }

    private float CalculateJumpProgress(Vector3 currentPosition)
    {
        if (_totalJumpDistance <= 0.001f)
            return 1f;

        float moved = Vector3.Distance(_startPosition, currentPosition);
        return Mathf.Clamp01(moved / _totalJumpDistance);
    }

    private bool HasReachedTarget()
    {
        if (_context == null || _context.SelfTransform == null)
            return true;

        return (_targetPosition - _context.SelfTransform.position).sqrMagnitude <= 0.000001f;
    }

    private void CompleteJumpMovement()
    {
        _context.SelfTransform.position = _targetPosition;
        SetWalkGuardSuppressed(false);
        RestoreVisualOffset();
        _phase = Phase.Impact;
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

    private void CacheVisualRoot()
    {
        _visualRoot = _context.Animation != null ? _context.Animation.VisualRoot : null;
        if (_visualRoot == null)
            return;

        _visualBaseLocalPosition = _visualRoot.localPosition;
        _hasVisualRoot = true;
    }

    private void ApplyVisualOffset(float progress)
    {
        if (!_hasVisualRoot || _visualRoot == null)
            return;

        float height = _data.JumpVisualHeight;
        if (height <= 0f)
        {
            RestoreVisualOffset();
            return;
        }

        float t = Mathf.Clamp01(progress);
        float offset = Mathf.Sin(t * Mathf.PI) * height;
        _visualRoot.localPosition = _visualBaseLocalPosition + Vector3.up * offset;
    }

    private void RestoreVisualOffset()
    {
        if (!_hasVisualRoot || _visualRoot == null)
            return;

        _visualRoot.localPosition = _visualBaseLocalPosition;
    }

    private void Finish()
    {
        Cleanup();
        IsFinished = true;
    }

    private void Cleanup()
    {
        RestoreVisualOffset();
        SetWalkGuardSuppressed(false);

        if (_unlockFacing)
            _context?.Animation?.UnlockSpecialFacing();

        _unlockFacing = false;
    }

    private void SetWalkGuardSuppressed(bool suppressed)
    {
        if (_context == null)
            return;

        EnemyController enemy = _context.Enemy;
        if (enemy != null)
            enemy.SetWalkGuardSuppressed(suppressed);
    }
}
