using System.Collections.Generic;
using UnityEngine;

public sealed class EnemySkillRuntime : EnemyPatternRuntime
{
    private static readonly Collider2D[] s_HitBuffer = new Collider2D[128];

    private enum Phase
    {
        Windup,
        Move,
        Impact,
        Recovery
    }

    private readonly EnemySkillData _data;
    private readonly List<Vector2Int> _targetOffsetBuffer = new();
    private readonly List<Vector2Int> _damageCellBuffer = new();
    private readonly HashSet<IDamageable> _hitThisImpact = new();
    private EnemyPatternContext _context;
    private Phase _phase;
    private Vector3 _startPosition;
    private Vector3 _targetPosition;
    private float _totalMoveDistance;
    private Transform _visualRoot;
    private Vector3 _visualBaseLocalPosition;
    private Vector2 _facingDirection = Vector2.down;
    private float _timer;
    private bool _appliedImpact;
    private bool _hasVisualRoot;
    private bool _unlockFacing;

    public EnemySkillRuntime(EnemySkillData data)
    {
        _data = data;
    }

    public override bool Start(EnemyPatternContext context)
    {
        InitializeState(context);

        if (_data == null)
        {
            Finish();
            return false;
        }

        if (_data.ExecutionType != EnemySkillExecutionType.Jump)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning(
                $"[EnemySkillRuntime] {_data.name}: execution type {_data.ExecutionType} is not implemented.",
                _data);
#endif
            Finish();
            return false;
        }

        return StartJumpExecution();
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

            case Phase.Move:
                TickMove(deltaTime);
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

    private void InitializeState(EnemyPatternContext context)
    {
        _context = context;
        IsFinished = false;
        _phase = Phase.Windup;
        _startPosition = default;
        _targetPosition = default;
        _totalMoveDistance = 0f;
        _visualRoot = null;
        _visualBaseLocalPosition = default;
        _facingDirection = Vector2.down;
        _timer = 0f;
        _appliedImpact = false;
        _hasVisualRoot = false;
        _unlockFacing = false;
    }

    private bool StartJumpExecution()
    {
        if (!CanRun() || !TryResolveJumpTarget(out _targetPosition))
        {
            Finish();
            return false;
        }

        CacheVisualRoot();
        _startPosition = _context.SelfTransform.position;
        _facingDirection = ResolveFacingDirection(_targetPosition);
        _context.Brain.StopMoving();

        if (_data.LockFacingDuringExecute)
        {
            _context.Animation?.LockSpecialFacing(_facingDirection);
            _unlockFacing = true;
        }

        _context.Animation?.PlayPatternAnimation(
            _data.CastAnimation,
            _data.CastAnimationTrigger,
            _targetPosition);
        _timer = _data.CastDelay;
        _phase = Phase.Windup;

        if (_timer <= 0f)
            StartMove();

        return true;
    }

    private void TickWindup(float deltaTime)
    {
        if (_data.LockFacingDuringExecute)
            _context.Animation?.LockSpecialFacing(_facingDirection);

        if (_timer > 0f)
        {
            _timer -= deltaTime;
            if (_timer > 0f)
                return;
        }

        StartMove();
    }

    private void StartMove()
    {
        SetWalkGuardSuppressed(true);
        _startPosition = _context.SelfTransform.position;
        _totalMoveDistance = Vector3.Distance(_startPosition, _targetPosition);
        _context.Animation?.PlayPatternAnimation(
            _data.ExecuteAnimation,
            _data.ExecuteAnimationTrigger,
            _targetPosition);
        _timer = 0f;
        _phase = Phase.Move;
        ApplyVisualOffset(0f);

        if (_data.MoveSpeed <= 0f || _totalMoveDistance <= 0.001f)
            CompleteMove();
    }

    private void TickMove(float deltaTime)
    {
        if (_data.LockFacingDuringExecute)
            _context.Animation?.LockSpecialFacing(_facingDirection);

        float step = _data.MoveSpeed * deltaTime;
        if (step <= 0f)
            return;

        Vector3 current = _context.SelfTransform.position;
        Vector3 toTarget = _targetPosition - current;
        float remaining = toTarget.magnitude;
        if (remaining <= 0.001f)
        {
            CompleteMove();
            return;
        }

        Vector3 next = step >= remaining
            ? _targetPosition
            : current + toTarget / remaining * step;

        _context.SelfTransform.position = next;
        ApplyVisualOffset(CalculateMoveProgress(next));

        if (step >= remaining || HasReachedTarget())
            CompleteMove();
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
        _damageCellBuffer.Clear();
        _hitThisImpact.Clear();

        PatternShapeData damageShape = _data.DamageShape;
        if (damageShape == null)
            return;

        AttackPattern.FillTargets(
            damageShape.PatternType,
            Vector2Int.zero,
            Vector2Int.up,
            _data.DamageRange,
            damageShape.ConeHalfAngle,
            _damageCellBuffer,
            damageShape.CustomCells);

        if (damageShape.PatternType != AttackPatternType.Custom &&
            !_damageCellBuffer.Contains(Vector2Int.zero))
        {
            _damageCellBuffer.Add(Vector2Int.zero);
        }

        if (_damageCellBuffer.Count == 0)
            return;

        int damage = _data.Damage > 0
            ? _data.Damage
            : (_context.Data != null ? _context.Data.attack : 1);

        Vector3 landingPosition = _context.SelfTransform.position;
        float angleDeg = AimDirectionUtility.ToAuthoredFacingAngle(_facingDirection);
        float cellSize = WorldEnvironmentQuery.GetCellSize(landingPosition);
        CustomShapeMatcher matcher = new CustomShapeMatcher(
            (Vector2)landingPosition,
            angleDeg,
            cellSize,
            _damageCellBuffer);
        Vector2 boxSize = new Vector2(cellSize, cellSize);

        for (int i = 0; i < _damageCellBuffer.Count; i++)
        {
            Vector2 cellCenter = matcher.GetCellWorldCenter(_damageCellBuffer[i]);
            int count = Physics2D.OverlapBox(
                cellCenter,
                boxSize,
                matcher.AngleDeg,
                CombatLayers.PlayerFilter,
                s_HitBuffer);

            for (int h = 0; h < count; h++)
            {
                Collider2D hit = s_HitBuffer[h];
                if (!hit.TryGetComponent<IDamageable>(out IDamageable target))
                    continue;
                if (!target.IsAlive)
                    continue;
                if (!_hitThisImpact.Add(target))
                    continue;

                target.TakeDamage(damage);
            }
        }
    }

    private bool TryResolveJumpTarget(out Vector3 targetPosition)
    {
        Vector3 selfPosition = _context.SelfTransform.position;
        targetPosition = selfPosition;

        Vector2 facing = (Vector2)(_context.Target.position - selfPosition);
        if (facing.sqrMagnitude <= 0.0001f)
            return false;

        facing.Normalize();
        float angle = AimDirectionUtility.ToAuthoredFacingAngle(facing);
        float cellSize = WorldEnvironmentQuery.GetCellSize(selfPosition);
        int searchCellRange = Mathf.RoundToInt(_data.MaxRange / cellSize);
        if (searchCellRange <= 0)
            return false;

        PatternShapeData searchShape = _data.SearchShape;
        if (searchShape == null)
            return false;

        _targetOffsetBuffer.Clear();
        AttackPattern.FillTargets(
            searchShape.PatternType,
            Vector2Int.zero,
            Vector2Int.up,
            searchCellRange,
            searchShape.ConeHalfAngle,
            _targetOffsetBuffer,
            searchShape.CustomCells);

        if (_targetOffsetBuffer.Count == 0)
            return false;

        Quaternion rotation = Quaternion.Euler(0f, 0f, angle);
        DungeonManager dungeon = _context.DungeonManager;
        bool filterToCurrentRoom =
            _data.StayInRoom &&
            !WorldEnvironmentQuery.IsInRegisteredArea(selfPosition) &&
            dungeon != null &&
            dungeon.Data != null;

        RoomInfo currentRoom = default;
        if (filterToCurrentRoom)
        {
            Vector2Int selfGrid = dungeon.WorldToGrid(selfPosition);
            RoomInfo? room = dungeon.GetRoomAt(selfGrid.x, selfGrid.y);
            if (!room.HasValue)
                return false;

            currentRoom = room.Value;
        }

        Vector3 playerPosition = _context.Target.position;
        float footprintRadius = _context.Enemy.CollisionFootprintRadius;
        float bestDistanceSqr = float.PositiveInfinity;
        bool foundTarget = false;

        for (int i = 0; i < _targetOffsetBuffer.Count; i++)
        {
            Vector2Int offset = _targetOffsetBuffer[i];
            Vector3 rotatedOffset = rotation * new Vector3(
                offset.x * cellSize,
                offset.y * cellSize,
                0f);
            Vector3 candidate = selfPosition + rotatedOffset;
            candidate.z = selfPosition.z;

            if (!WorldEnvironmentQuery.IsFootprintWalkable(candidate, footprintRadius))
                continue;

            if (filterToCurrentRoom && !IsInSameRoom(dungeon, candidate, currentRoom))
                continue;

            float distanceSqr = ((Vector2)(playerPosition - candidate)).sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            targetPosition = candidate;
            foundTarget = true;
        }

        return foundTarget;
    }

    private static bool IsInSameRoom(DungeonManager dungeon, Vector3 position, RoomInfo currentRoom)
    {
        Vector2Int grid = dungeon.WorldToGrid(position);
        RoomInfo? candidateRoom = dungeon.GetRoomAt(grid.x, grid.y);
        return candidateRoom.HasValue &&
               candidateRoom.Value.X == currentRoom.X &&
               candidateRoom.Value.Y == currentRoom.Y;
    }

    private Vector2 ResolveFacingDirection(Vector3 targetPosition)
    {
        Vector2 direction = targetPosition - _context.SelfTransform.position;
        if (direction.sqrMagnitude <= 0.0001f)
            return _facingDirection.sqrMagnitude > 0.0001f ? _facingDirection : Vector2.down;

        return direction.normalized;
    }

    private float CalculateMoveProgress(Vector3 currentPosition)
    {
        if (_totalMoveDistance <= 0.001f)
            return 1f;

        float moved = Vector3.Distance(_startPosition, currentPosition);
        return Mathf.Clamp01(moved / _totalMoveDistance);
    }

    private bool HasReachedTarget()
    {
        if (_context == null || _context.SelfTransform == null)
            return true;

        return (_targetPosition - _context.SelfTransform.position).sqrMagnitude <= 0.000001f;
    }

    private void CompleteMove()
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
