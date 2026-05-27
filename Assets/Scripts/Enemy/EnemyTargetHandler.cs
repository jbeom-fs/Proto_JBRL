using UnityEngine;

/// <summary>
/// Resolves and caches the current player target for enemy brains.
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
        return WorldEnvironmentQuery.IsWalkable(TargetPosition);
    }
}
