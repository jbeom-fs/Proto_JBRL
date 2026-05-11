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
}
