using System.Collections.Generic;
using UnityEngine;

public class AttackExecutor
{
    private static readonly Collider2D[]    s_HitBuffer = new Collider2D[128];
    private static readonly ContactFilter2D s_NoFilter  = ContactFilter2D.noFilter;

    private readonly Transform _attackerTransform;
    private readonly IDamageable _owner;
    private readonly HashSet<IDamageable> _hitTargetsThisAttack = new();
    private readonly HashSet<Vector2Int> _targetGridSet = new();
    private readonly List<HitCandidate> _hitCandidates = new();
    private bool _isAttackAlreadyProcessed;

    private struct HitCandidate
    {
        public IDamageable Target;
        public float SqrDistance;
    }

    public AttackExecutor(Transform attackerTransform, IDamageable owner)
    {
        _attackerTransform = attackerTransform;
        _owner = owner;
    }

    public void BeginAttackActivation()
    {
        _isAttackAlreadyProcessed = false;
        _hitTargetsThisAttack.Clear();
        _targetGridSet.Clear();
        _hitCandidates.Clear();
    }

    public void ExecuteAttack(
        List<Vector2Int> gridPositions,
        int damage,
        bool canPenetrateWalls,
        bool isMultiTarget,
        float knockbackForce,
        float knockbackDuration,
        float slowPercentage,
        float slowDuration,
        float hitRadius)
    {
        if (_isAttackAlreadyProcessed) return;
        if (_attackerTransform == null) return;

        var dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null) return;

        Vector2Int attackerGrid = dungeonManager.WorldToGrid(_attackerTransform.position);
        _hitCandidates.Clear();
        _targetGridSet.Clear();

        float queryRadius = BuildTargetGridSetAndRadius(gridPositions, dungeonManager);
        if (_targetGridSet.Count == 0) return;

        int count = Physics2D.OverlapCircle(_attackerTransform.position, queryRadius + hitRadius, s_NoFilter, s_HitBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = s_HitBuffer[i];
            if (!col.TryGetComponent<IDamageable>(out var target)) continue;
            if (ReferenceEquals(target, _owner)) continue;
            if (!target.IsAlive) continue;

            Vector2Int targetGrid = dungeonManager.WorldToGrid(col.bounds.center);
            if (!_targetGridSet.Contains(targetGrid)) continue;

            if (!canPenetrateWalls)
            {
                if (HasWallBetween(attackerGrid, targetGrid)) continue;
            }

            if (!_hitTargetsThisAttack.Add(target)) continue;

            _hitCandidates.Add(new HitCandidate
            {
                Target = target,
                SqrDistance = ((Vector2)col.bounds.center - (Vector2)_attackerTransform.position).sqrMagnitude
            });
        }

        if (_hitCandidates.Count == 0) return;

        if (isMultiTarget)
        {
            for (int i = 0; i < _hitCandidates.Count; i++)
                ApplyDamageAndStatus(_hitCandidates[i].Target, damage, knockbackForce, knockbackDuration, slowPercentage, slowDuration);
        }
        else
        {
            HitCandidate closest = _hitCandidates[0];
            for (int i = 1; i < _hitCandidates.Count; i++)
                if (_hitCandidates[i].SqrDistance < closest.SqrDistance)
                    closest = _hitCandidates[i];

            ApplyDamageAndStatus(closest.Target, damage, knockbackForce, knockbackDuration, slowPercentage, slowDuration);
        }

        _isAttackAlreadyProcessed = true;
    }

    private void ApplyDamageAndStatus(
        IDamageable target,
        int damage,
        float knockbackForce,
        float knockbackDuration,
        float slowPercentage,
        float slowDuration)
    {
        if (target == null || !target.IsAlive) return;

        if (target is EnemyController enemy)
        {
            enemy.ApplyCombatImpact(
                damage,
                _attackerTransform.position,
                knockbackForce,
                knockbackDuration,
                slowPercentage,
                slowDuration);
            return;
        }

        target.TakeDamage(damage);
    }

    private float BuildTargetGridSetAndRadius(List<Vector2Int> gridPositions, DungeonManager dungeonManager)
    {
        float maxSqrDistance = 0f;
        Vector2 origin = _attackerTransform.position;

        for (int i = 0; i < gridPositions.Count; i++)
        {
            Vector2Int grid = gridPositions[i];
            if (!_targetGridSet.Add(grid)) continue;

            Vector2 world = dungeonManager.GridToWorld(grid);
            float sqrDistance = (world - origin).sqrMagnitude;
            if (sqrDistance > maxSqrDistance)
                maxSqrDistance = sqrDistance;
        }

        return Mathf.Sqrt(maxSqrDistance);
    }

    private bool HasWallBetween(Vector2Int from, Vector2Int to)
    {
        DungeonData data = DungeonManager.Instance != null ? DungeonManager.Instance.Data : null;
        if (data == null) return false;

        int dx    = to.x - from.x;
        int dy    = to.y - from.y;
        int steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        if (steps == 0) return false;

        for (int i = 1; i <= steps; i++)
        {
            float t   = (float)i / steps;
            int   col = Mathf.RoundToInt(from.x + dx * t);
            int   row = Mathf.RoundToInt(from.y + dy * t);
            if (!data.IsWalkable(col, row)) return true;
        }
        return false;
    }
}
