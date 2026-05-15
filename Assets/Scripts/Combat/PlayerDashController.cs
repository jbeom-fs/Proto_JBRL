using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles player dash movement for skill execution.
/// It owns only movement timing/path safety; damage, invulnerability, VFX, and camera effects can be layered later.
/// </summary>
public sealed class PlayerDashController : MonoBehaviour
{
    private const float MinDashMoveDistance = 0.05f;
    private const float MinSampleStep = 0.05f;
    private const int EnemyHitBufferSize = 64;
    private const int MaxDashDamageSamplesPerSegment = 16;

    private readonly Collider2D[] _enemyHitBuffer = new Collider2D[EnemyHitBufferSize];
    private readonly HashSet<EnemyController> _hitEnemiesThisDash = new();
    private Coroutine _dashRoutine;
    private CircleCollider2D _circleCollider;
    private DungeonManager _dungeonManager;
    private PlayerCombatController _activeInvincibilityOwner;
    private DashDamageRequest _damageRequest;
    private bool _hasDashDamage;

    public bool IsDashing => _dashRoutine != null;

    private void Awake()
    {
        _circleCollider = GetComponent<CircleCollider2D>();
        _dungeonManager = DungeonManager.Instance;
    }

    private void OnDisable()
    {
        if (_dashRoutine != null)
        {
            StopCoroutine(_dashRoutine);
            _dashRoutine = null;
        }

        ClearDashInvincibility();
        ClearDashDamageState();
    }

    public bool TryStartDash(
        PlayerCombatController caster,
        Vector2 direction,
        float distance,
        float duration,
        bool stopOnWall,
        bool invincibleDuringDash,
        DashDamageRequest damageRequest)
    {
        if (_dashRoutine != null) return false;
        if (caster == null || caster.IsDead || !caster.isActiveAndEnabled) return false;
        if (caster.IsStunned) return false;
        if (!gameObject.activeInHierarchy || !enabled) return false;

        direction = ResolveDirection(direction);
        distance = Mathf.Max(0f, distance);
        if (distance <= MinDashMoveDistance) return false;

        _dungeonManager = DungeonManager.Instance;
        if (_dungeonManager == null || _dungeonManager.Data == null) return false;

        Vector3 start = transform.position;
        if (!TryResolveDestination(start, direction, distance, stopOnWall, out Vector3 destination))
            return false;

        if ((destination - start).sqrMagnitude < MinDashMoveDistance * MinDashMoveDistance)
            return false;

        if (invincibleDuringDash)
            BeginDashInvincibility(caster, Mathf.Max(0f, duration));

        BeginDashDamageState(damageRequest);
        _dashRoutine = StartCoroutine(DashRoutine(caster, start, destination, Mathf.Max(0f, duration)));
        return true;
    }

    private IEnumerator DashRoutine(PlayerCombatController caster, Vector3 start, Vector3 destination, float duration)
    {
        try
        {
            if (duration <= 0f)
            {
                transform.position = destination;
                TryApplyDashPathDamage(start, destination);
                TryApplyDashContactDamage(destination);
                yield break;
            }

            Vector3 previousPosition = start;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (ShouldCancel(caster))
                    break;

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Vector3 currentPosition = Vector3.Lerp(start, destination, t);
                transform.position = currentPosition;
                TryApplyDashPathDamage(previousPosition, currentPosition);
                previousPosition = currentPosition;
                yield return null;
            }

            if (!ShouldCancel(caster))
            {
                transform.position = destination;
                TryApplyDashPathDamage(previousPosition, destination);
                TryApplyDashContactDamage(destination);
            }
        }
        finally
        {
            _dashRoutine = null;
            ClearDashInvincibility();
            ClearDashDamageState();
        }
    }

    private bool TryResolveDestination(
        Vector3 start,
        Vector2 direction,
        float distance,
        bool stopOnWall,
        out Vector3 destination)
    {
        destination = start;

        float tileSize = ResolveTileSize();
        float sampleStep = Mathf.Max(MinSampleStep, tileSize * 0.25f);
        Vector3 worldDirection = new Vector3(direction.x, direction.y, 0f);
        Vector3 lastWalkable = start;

        for (float moved = sampleStep; moved < distance + sampleStep; moved += sampleStep)
        {
            float clampedDistance = Mathf.Min(moved, distance);
            Vector3 candidate = start + worldDirection * clampedDistance;
            if (!IsFootprintWalkable(candidate))
            {
                if (!stopOnWall)
                    return false;

                destination = lastWalkable;
                return true;
            }

            lastWalkable = candidate;
        }

        destination = lastWalkable;
        return true;
    }

    private bool IsFootprintWalkable(Vector3 position)
    {
        float radius = ResolveFootprintRadius();
        return _dungeonManager.IsFootprintWalkable(position, radius) &&
               !MovementBlockerQuery.IsPlayerMovementBlocked(position, radius);
    }

    private float ResolveTileSize()
    {
        if (_dungeonManager != null &&
            _dungeonManager.dungeonRenderer != null &&
            _dungeonManager.dungeonRenderer.tilemap != null)
        {
            return Mathf.Max(0.01f, _dungeonManager.dungeonRenderer.tilemap.cellSize.x);
        }

        return 1f;
    }

    private float ResolveFootprintRadius()
    {
        if (_circleCollider == null)
            _circleCollider = GetComponent<CircleCollider2D>();

        if (_circleCollider == null)
            return Mathf.Max(0.01f, ResolveTileSize() * 0.2f);

        float maxScale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y));

        return Mathf.Max(0.01f, _circleCollider.radius * maxScale);
    }

    private static Vector2 ResolveDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector2.down;

        return direction.normalized;
    }

    private static bool ShouldCancel(PlayerCombatController caster)
    {
        return caster == null ||
               caster.IsDead ||
               !caster.isActiveAndEnabled ||
               !caster.gameObject.activeInHierarchy;
    }

    private void BeginDashInvincibility(PlayerCombatController caster, float visualDuration)
    {
        ClearDashInvincibility();
        _activeInvincibilityOwner = caster;
        _activeInvincibilityOwner.BeginExternalInvincibility(visualDuration);
    }

    private void ClearDashInvincibility()
    {
        if (_activeInvincibilityOwner == null)
            return;

        _activeInvincibilityOwner.EndExternalInvincibility();
        _activeInvincibilityOwner = null;
    }

    private void BeginDashDamageState(DashDamageRequest damageRequest)
    {
        _damageRequest = damageRequest;
        _hasDashDamage = damageRequest.Enabled;
        _hitEnemiesThisDash.Clear();
    }

    private void ClearDashDamageState()
    {
        _hasDashDamage = false;
        _damageRequest = default;
        _hitEnemiesThisDash.Clear();
    }

    private void TryApplyDashPathDamage(Vector3 previousPosition, Vector3 currentPosition)
    {
        if (!_hasDashDamage || !_damageRequest.DamageOnPath)
            return;

        Vector3 delta = currentPosition - previousPosition;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
            return;

        float radius = Mathf.Max(0.01f, _damageRequest.HitRadius);
        float spacing = Mathf.Max(0.05f, radius * 0.75f);
        int sampleCount = Mathf.Min(
            MaxDashDamageSamplesPerSegment,
            Mathf.CeilToInt(distance / spacing));
        if (sampleCount <= 0)
            return;

        for (int i = 1; i <= sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            ApplyDashDamageAt(Vector3.Lerp(previousPosition, currentPosition, t));
        }
    }

    private void TryApplyDashContactDamage(Vector3 position)
    {
        if (!_hasDashDamage || !_damageRequest.DamageOnContact)
            return;

        ApplyDashDamageAt(position);
    }

    private void ApplyDashDamageAt(Vector3 position)
    {
        if (!_hasDashDamage) return;

        float radius = Mathf.Max(0.01f, _damageRequest.HitRadius);
        int count = Physics2D.OverlapCircle(position, radius, CombatLayers.EnemyFilter, _enemyHitBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = _enemyHitBuffer[i];
            if (hit == null) continue;

            EnemyController enemy = ResolveEnemy(hit);
            if (enemy == null || !enemy.IsAlive) continue;
            if (!_hitEnemiesThisDash.Add(enemy)) continue;

            ApplyDamageToEnemy(enemy, position);
        }
    }

    private void ApplyDamageToEnemy(EnemyController enemy, Vector3 hitOrigin)
    {
        enemy.ApplyCombatImpact(
            _damageRequest.Damage,
            hitOrigin,
            _damageRequest.KnockbackForce,
            _damageRequest.KnockbackDuration,
            _damageRequest.SlowPercentage,
            _damageRequest.SlowDuration);
    }

    private static EnemyController ResolveEnemy(Collider2D hit)
    {
        if (hit.TryGetComponent(out EnemyController enemy))
            return enemy;

        return hit.GetComponentInParent<EnemyController>();
    }
}

public struct DashDamageRequest
{
    public bool DamageOnPath;
    public bool DamageOnContact;
    public int Damage;
    public float HitRadius;
    public float KnockbackForce;
    public float KnockbackDuration;
    public float SlowPercentage;
    public float SlowDuration;

    public bool Enabled => DamageOnPath || DamageOnContact;
}
