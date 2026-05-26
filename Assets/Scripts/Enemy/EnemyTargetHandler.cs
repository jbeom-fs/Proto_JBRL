using UnityEngine;

/// <summary>
/// 플레이어 탐색과 현재 목표 좌표 관리를 담당합니다.
/// 방 입장 이벤트나 CheckRoomEntry에 의존하지 않고 매 프레임 월드 좌표에서 그리드 좌표를 얻습니다.
/// EnemyBrain의 public 멤버만 참조해 외부 파일로 안전하게 분리되어 있습니다.
/// </summary>
public class TargetHandler
{
    private readonly EnemyBrain _brain;
    private IDamageable _damageable;
    private Collider2D _targetCollider;
    private float _detectRangeSqr;

    public TargetHandler(EnemyBrain brain)
    {
        _brain = brain;
    }

    public bool HasTarget => _brain.player != null;
    public IDamageable Damageable => _damageable;
    public Collider2D TargetCollider => _targetCollider;
    public float DetectRangeSqr => _detectRangeSqr;
    public Vector3 TargetPosition => _brain.player != null ? _brain.player.position : _brain.transform.position;
    public float SqrDistanceToTarget => (_brain.player.position - _brain.transform.position).sqrMagnitude;

    public Vector2Int TargetGridPosition => _brain.dungeonManager != null && _brain.player != null
        ? _brain.dungeonManager.WorldToGrid(_brain.player.position)
        : Vector2Int.zero;

    public virtual void RecalculateRanges()
    {
        if (_brain.Data == null) return;
        _detectRangeSqr = _brain.Data.detectRange * _brain.Data.detectRange;
    }

    public virtual bool RefreshTarget()
    {
        if (_brain.player == null)
            FindPlayer();

        if (_brain.player == null)
            return false;

        if (_damageable == null)
            _damageable = ResolveDamageable(_brain.player);
        if (_targetCollider == null)
            _targetCollider = ResolveCollider(_brain.player);

        return IsTargetOnTrackableTile();
    }

    private void FindPlayer()
    {
        PlayerController active = PlayerController.Active;
        if (active == null) return;

        _brain.player = active.transform;
        _damageable = ResolveDamageable(_brain.player);
        _targetCollider = ResolveCollider(_brain.player);
    }

    private Collider2D ResolveCollider(Transform targetTransform)
    {
        return ResolveOnHierarchy<Collider2D>(targetTransform);
    }

    private IDamageable ResolveDamageable(Transform targetTransform)
    {
        if (targetTransform == null) return null;

        IDamageable damageable = ResolveOnHierarchy<IDamageable>(targetTransform);
        if (damageable != null) return damageable;

        // PlayerCombatController가 IDamageable을 구현하지만,
        // 인터페이스 lookup이 누락된 prefab 구성에서도 안전하게 fallback한다.
        return ResolveOnHierarchy<PlayerCombatController>(targetTransform);
    }

    private static T ResolveOnHierarchy<T>(Transform targetTransform) where T : class
    {
        if (targetTransform == null) return null;

        T result = targetTransform.GetComponent<T>();
        if (result != null) return result;

        result = targetTransform.GetComponentInParent<T>();
        if (result != null) return result;

        return targetTransform.GetComponentInChildren<T>();
    }

    private bool IsTargetOnTrackableTile()
    {
        if (EliteArenaEncounterController.TryIsArenaPointWalkable(TargetPosition, out bool arenaWalkable))
            return arenaWalkable;

        DungeonData data = _brain.DungeonData;
        if (data == null) return true;

        Vector2Int grid = TargetGridPosition;
        if (!data.InBounds(grid.x, grid.y)) return false;

        int tile = data.GetTileTypeUnchecked(grid.x, grid.y);

        // 핵심 수정: ROOM뿐 아니라 CORRIDOR/STair 등 EMPTY가 아닌 전체 그리드를 추적 대상으로 둡니다.
        // 그래서 플레이어가 복도에 나가도 적 AI의 목표 좌표가 사라지지 않습니다.
        return tile != DungeonGenerator.EMPTY;
    }
}
