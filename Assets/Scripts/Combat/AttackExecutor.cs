using System.Collections.Generic;
using UnityEngine;

public class AttackExecutor
{
    private static readonly Collider2D[] s_HitBuffer = new Collider2D[128];

    private readonly Transform _attackerTransform;
    private readonly IDamageable _owner;
    private readonly ContactFilter2D _targetFilter;
    private readonly HashSet<IDamageable> _hitTargetsThisAttack = new();
    private readonly List<EnemyController> _hitEnemiesThisAttack = new();
    private readonly List<HitCandidate> _hitCandidates = new();
    private bool _isAttackAlreadyProcessed;
    private int _damageDealtThisAttack;

    private struct HitCandidate
    {
        public IDamageable Target;
        public float SqrDistance;
    }

    public AttackExecutor(Transform attackerTransform, IDamageable owner, ContactFilter2D targetFilter)
    {
        _attackerTransform = attackerTransform;
        _owner = owner;
        _targetFilter = targetFilter;
    }

    public void BeginAttackActivation()
    {
        _isAttackAlreadyProcessed = false;
        _hitTargetsThisAttack.Clear();
        _hitEnemiesThisAttack.Clear();
        _hitCandidates.Clear();
        _damageDealtThisAttack = 0;
    }

    public int HitEnemyCount => _hitEnemiesThisAttack.Count;
    public int DamageDealtThisAttack => _damageDealtThisAttack;

    public EnemyController GetHitEnemy(int index)
    {
        return (uint)index < (uint)_hitEnemiesThisAttack.Count ? _hitEnemiesThisAttack[index] : null;
    }

    public void ExecuteAttackWorld(
        List<Vector3> targetWorldPositions,
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
        if (targetWorldPositions == null || targetWorldPositions.Count == 0) return;

        _hitCandidates.Clear();

        float queryRadius = BuildWorldQueryRadius(targetWorldPositions);
        float targetMatchRadius = Mathf.Max(
            Mathf.Max(0.01f, hitRadius),
            WorldEnvironmentQuery.GetCellSize(_attackerTransform.position) * 0.5f);
        float targetMatchRadiusSqr = targetMatchRadius * targetMatchRadius;

        int count = Physics2D.OverlapCircle(_attackerTransform.position, queryRadius + hitRadius, _targetFilter, s_HitBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = s_HitBuffer[i];
            if (!col.TryGetComponent<IDamageable>(out var target)) continue;
            if (ReferenceEquals(target, _owner)) continue;
            if (!target.IsAlive) continue;

            Vector3 targetPoint;
            if (!TryResolveMatchedTargetPoint(col.bounds.center, targetWorldPositions, targetMatchRadiusSqr, out targetPoint))
                continue;

            if (!canPenetrateWalls)
            {
                if (!WorldEnvironmentQuery.HasLineOfSight(_attackerTransform.position, col.bounds.center)) continue;
                if (!WorldEnvironmentQuery.HasLineOfSight(_attackerTransform.position, targetPoint)) continue;
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
            int actualDamage = enemy.ApplyCombatImpact(
                damage,
                _attackerTransform.position,
                knockbackForce,
                knockbackDuration,
                slowPercentage,
                slowDuration);
            _damageDealtThisAttack += actualDamage;
            _hitEnemiesThisAttack.Add(enemy);
            return;
        }

        target.TakeDamage(damage);
    }

    private float BuildWorldQueryRadius(List<Vector3> targetWorldPositions)
    {
        float maxSqrDistance = 0f;
        Vector2 origin = _attackerTransform.position;

        for (int i = 0; i < targetWorldPositions.Count; i++)
        {
            Vector2 world = targetWorldPositions[i];
            float sqrDistance = (world - origin).sqrMagnitude;
            if (sqrDistance > maxSqrDistance)
                maxSqrDistance = sqrDistance;
        }

        return Mathf.Sqrt(maxSqrDistance);
    }

    private static bool TryResolveMatchedTargetPoint(
        Vector3 targetCenter,
        List<Vector3> targetWorldPositions,
        float maxSqrDistance,
        out Vector3 targetPoint)
    {
        targetPoint = default;
        float bestSqrDistance = maxSqrDistance;
        bool found = false;

        for (int i = 0; i < targetWorldPositions.Count; i++)
        {
            Vector3 candidate = targetWorldPositions[i];
            float sqrDistance = ((Vector2)candidate - (Vector2)targetCenter).sqrMagnitude;
            if (sqrDistance > bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            targetPoint = candidate;
            found = true;
        }

        return found;
    }

}
