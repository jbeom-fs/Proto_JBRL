using UnityEngine;

/// <summary>
/// 이동과 충돌, 시야 검사를 담당합니다.
/// MonoBehaviour가 아닌 일반 C# 객체라서 일반 몬스터에 불필요한 Unity 컴포넌트를 추가하지 않습니다.
/// EnemyBrain의 public 멤버만 참조해 외부 파일로 안전하게 분리되어 있습니다.
/// </summary>
public class MovementHandler
{
    private readonly EnemyBrain _brain;
    private readonly Collider2D[] _separationBuffer = new Collider2D[16];
    private static readonly ContactFilter2D s_SeparationFilter = ContactFilter2D.noFilter;
    private float _tileSize = 1f;
    private Vector2 _smoothedSeparation;

    // Ranged Random/Kiting 런타임 상태 — pooling 재사용 시 ResetRuntimeState에서 초기화됩니다.
    private Vector3 _randomDestination;
    private bool _hasRandomDestination;
    private float _randomTimer;

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

    /// <summary>
    /// 풀에서 다시 꺼낸 적이 이전 인스턴스의 Random/Kiting 상태를 이어받지 않도록
    /// 런타임 상태를 초기화합니다. EnemyBrain.ResetRuntimeState에서 호출됩니다.
    /// </summary>
    public virtual void ResetRuntimeState()
    {
        _hasRandomDestination = false;
        _randomDestination = default;
        _randomTimer = 0f;
        _smoothedSeparation = default;
    }

    public virtual bool MoveToward(Vector3 target)
    {
        if (_brain.Data == null) return false;

        Vector2 dir = target - _brain.transform.position;
        if (dir.sqrMagnitude <= 0.0001f)
            return false;

        Vector2 desired = dir.normalized;
        Vector2 separation = CalculateSeparation();
        Vector2 blended = desired + separation * _brain.separationWeight;

        if (blended.sqrMagnitude > 1f)
            blended.Normalize();

        return MoveWithCollision(blended);
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
        if (dir.sqrMagnitude <= 0.0001f)
            return false;

        float step = _brain.CurrentMoveSpeed * Time.deltaTime;
        if (step <= 0f) return false;
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

    private Vector2 CalculateSeparation()
    {
        if (!_brain.enableSeparation) return Vector2.zero;

        int neighborCount = Physics2D.OverlapCircle(
            _brain.transform.position,
            _brain.separationRadius,
            s_SeparationFilter,
            _separationBuffer);

        Vector2 repel = Vector2.zero;
        int count = 0;
        Vector2 self = _brain.transform.position;

        for (int i = 0; i < neighborCount; i++)
        {
            Collider2D col = _separationBuffer[i];
            if (col == null) continue;
            if (col.transform == _brain.transform) continue;
            if (!col.TryGetComponent<EnemyController>(out _)) continue;

            Vector2 away = self - (Vector2)col.bounds.center;
            float sqrDistance = Mathf.Max(away.sqrMagnitude, 0.0001f);

            // 가까운 이웃일수록 더 강하게 밀어내 평균 반발 벡터를 만든다.
            repel += away.normalized / sqrDistance;
            count++;
        }

        Vector2 targetSeparation = count > 0 ? (repel / count).normalized : Vector2.zero;

        // 분리 벡터를 보간해 프레임마다 방향이 튀는 지터를 줄인다.
        float t = 1f - Mathf.Exp(-_brain.separationSmoothing * Time.deltaTime);
        _smoothedSeparation = Vector2.Lerp(_smoothedSeparation, targetSeparation, t);
        return _smoothedSeparation;
    }

    private bool CanMoveTo(Vector3 pos)
    {
        if (_brain.dungeonManager == null) return true;

        float radius = _brain.Enemy != null
            ? _brain.Enemy.CollisionFootprintRadius
            : _tileSize * _brain.collisionRadius;
        return _brain.dungeonManager.IsFootprintWalkable(pos, radius);
    }

    /// <summary>
    /// Ranged behaviorType 전용 이동 결정 진입점입니다.
    /// Kiting/Random일 때 true를 반환해 호출자(ChaseState)가 LOS/A* 흐름을 건너뛰도록 합니다.
    /// Contact 또는 Ranged-Chase는 false를 반환해 기존 흐름을 그대로 유지합니다.
    /// </summary>
    public bool TryTickRangedMovement(float sqrDistanceToTarget)
    {
        EnemyData data = _brain.Data;
        if (data == null || data.behaviorType != EnemyBehaviorType.Ranged)
            return false;

        switch (data.rangedMovementType)
        {
            case RangedMovementType.Kiting:
                TickKitingMovement(sqrDistanceToTarget);
                return true;

            case RangedMovementType.Random:
                TickRandomMovement();
                return true;

            case RangedMovementType.Chase:
            default:
                return false;
        }
    }

    private void TickKitingMovement(float sqrDistanceToTarget)
    {
        EnemyData data = _brain.Data;
        float kiteRetreatSqr = data.kiteRetreatRange * data.kiteRetreatRange;
        float preferredSqr = data.preferredRange * data.preferredRange;

        if (sqrDistanceToTarget < kiteRetreatSqr)
        {
            // 후퇴: Player 반대 방향으로 한 step 이동을 시도한다.
            // 벽이면 MoveWithCollision이 walkable 체크로 자체적으로 정지하므로 별도 fallback이 필요 없다.
            Vector3 self = _brain.transform.position;
            Vector3 toPlayer = _brain.Target.TargetPosition - self;
            if (toPlayer.sqrMagnitude <= 0.0001f)
            {
                _brain.StopMoving();
                return;
            }

            Vector3 away = -toPlayer.normalized;
            // preferredRange만큼 떨어진 가상 목적지를 만들어 MoveToward가 그 방향으로 step한다.
            Vector3 retreatTarget = self + away * Mathf.Max(0.01f, data.preferredRange);
            _brain.MoveToward(retreatTarget);
            return;
        }

        if (sqrDistanceToTarget > preferredSqr)
        {
            // 접근: 기존 Chase 직선 이동을 재사용한다 (separation 포함).
            _brain.DirectMoveToPlayer();
            return;
        }

        // 적정 거리: 공격 가능 거리에 있다면 ChaseState 진입부의 CanAttack 분기가 이미 Attack으로 보낸다.
        // 그렇지 않은 경우 한 곳에 뭉치지 않도록 그냥 정지한다.
        _brain.StopMoving();
    }

    private void TickRandomMovement()
    {
        _randomTimer -= Time.deltaTime;

        if (!_hasRandomDestination || _randomTimer <= 0f)
            SelectNewRandomDestination();

        if (!_hasRandomDestination)
        {
            _brain.StopMoving();
            return;
        }

        Vector3 self = _brain.transform.position;
        Vector3 delta = _randomDestination - self;
        float arriveSqr = _brain.waypointReachDistance * _brain.waypointReachDistance;
        if (delta.sqrMagnitude <= arriveSqr)
        {
            // 도달: 다음 interval 만료까지 정지한다.
            _hasRandomDestination = false;
            _brain.StopMoving();
            return;
        }

        _brain.MoveToward(_randomDestination);
    }

    private void SelectNewRandomDestination()
    {
        EnemyData data = _brain.Data;
        float intervalMin = Mathf.Max(0.01f, data.randomMoveIntervalMin);
        float intervalMax = Mathf.Max(intervalMin, data.randomMoveIntervalMax);
        _randomTimer = Random.Range(intervalMin, intervalMax);

        DungeonManager dungeon = _brain.dungeonManager;
        float radius = Mathf.Max(0.01f, data.randomMoveRadius);
        float footprintRadius = _brain.Enemy != null
            ? _brain.Enemy.CollisionFootprintRadius
            : _tileSize * _brain.collisionRadius;
        Vector3 self = _brain.transform.position;

        // 시도 횟수는 작은 상수로 제한 — Random.Range 반복 호출만 발생하고 alloc은 없다.
        const int MaxAttempts = 6;
        for (int i = 0; i < MaxAttempts; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(radius * 0.25f, radius);
            Vector3 candidate = new Vector3(
                self.x + Mathf.Cos(angle) * r,
                self.y + Mathf.Sin(angle) * r,
                0f);

            if (dungeon == null || dungeon.IsFootprintWalkable(candidate, footprintRadius))
            {
                _randomDestination = candidate;
                _hasRandomDestination = true;
                return;
            }
        }

        _hasRandomDestination = false;
    }
}
