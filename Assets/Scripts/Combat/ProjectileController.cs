using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;

public class ProjectileController : MonoBehaviour
{
    private static readonly ProfilerMarker s_ReleaseMarker = new ProfilerMarker("Projectile.Release");

    [SerializeField] private float hitRadius = 0.08f;
    [SerializeField] private float bounceExitOffset = 0.05f;
    [SerializeField] private bool disablePhysicsSimulation = true;
    [SerializeField] private ProjectileRotationMode rotationMode = ProjectileRotationMode.FaceMoveDirection;

    public enum TargetMode
    {
        Player,
        Enemy
    }

    public enum ProjectileRotationMode
    {
        KeepPrefabRotation,
        FaceMoveDirection
    }

    private static PlayerCombatController s_PlayerCombat;
    private static Transform s_PlayerTransform;
    private static float s_PlayerRadius;
    private static readonly Collider2D[] s_EnemyHitBuffer = new Collider2D[16];

    private Vector2 _direction = Vector2.right;
    private float _speed = 6f;
    private int _damage = 1;
    private float _knockbackForce;
    private float _knockbackDuration;
    private float _slowPercentage;
    private float _slowDuration;
    private float _stunDuration;
    private float _lifetime = 3f;
    private ProjectileWallHitMode _wallHitMode = ProjectileWallHitMode.Destroy;
    private TargetMode _targetMode = TargetMode.Player;
    private ProjectileTargetHitMode _targetHitMode = ProjectileTargetHitMode.DestroyOnHit;
    private UnityEngine.Object _owner;
    private int _maxBounceCount;
    private int _currentBounceCount;
    private Collider2D _collider;
    private Rigidbody2D _rigidbody;
    private SpriteRenderer _spriteRenderer;
    private Animator _animator;
    private FogVisibilityRenderer _fogVisibility;
    private Quaternion _initialLocalRotation;
    private Action<ProjectileController, ProjectileReleaseReason> _releaseAction;
    private bool _released;
    private DungeonManager _dungeon;
    private readonly HashSet<EnemyController> _hitEnemies = new();

    private void Awake()
    {
        _initialLocalRotation = transform.localRotation;
        _collider = GetComponent<Collider2D>();
        _rigidbody = GetComponent<Rigidbody2D>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _animator = GetComponent<Animator>();
        _fogVisibility = GetComponent<FogVisibilityRenderer>();
        if (_collider != null)
        {
            _collider.isTrigger = true;
            hitRadius = Mathf.Max(hitRadius, Mathf.Max(_collider.bounds.extents.x, _collider.bounds.extents.y));
        }

        if (disablePhysicsSimulation)
        {
            if (_rigidbody != null)
                _rigidbody.simulated = false;
            if (_collider != null)
                _collider.enabled = false;
        }
    }

    public void PrepareFromPool()
    {
        if (_fogVisibility == null && _spriteRenderer != null && !_spriteRenderer.enabled)
            _spriteRenderer.enabled = true;
        if (_animator != null)
        {
            if (!_animator.enabled)
                _animator.enabled = true;
            _animator.Play("Fly", 0, 0f);
        }
        if (!enabled)
            enabled = true;
    }

    public void HideForPool()
    {
        if (_animator != null && _animator.enabled)
            _animator.enabled = false;
        if (enabled)
            enabled = false;
        if (_fogVisibility != null)
        {
            _fogVisibility.HideImmediatelyForPool();
            if (_fogVisibility.enabled)
                _fogVisibility.enabled = false;
        }
        if (_spriteRenderer != null && _spriteRenderer.enabled)
            _spriteRenderer.enabled = false;
    }

    public void Initialize(
        Vector2 direction,
        int damage,
        float speed,
        float lifetime,
        ProjectileWallHitMode wallHitMode,
        int maxBounceCount,
        UnityEngine.Object owner)
    {
        Initialize(
            direction,
            damage,
            speed,
            lifetime,
            wallHitMode,
            maxBounceCount,
            owner,
            TargetMode.Player,
            ProjectileTargetHitMode.DestroyOnHit,
            0f,
            0f,
            0f,
            0f,
            0f);
    }

    public void Initialize(
        Vector2 direction,
        int damage,
        float speed,
        float lifetime,
        ProjectileWallHitMode wallHitMode,
        int maxBounceCount,
        UnityEngine.Object owner,
        TargetMode targetMode,
        ProjectileTargetHitMode targetHitMode,
        float knockbackForce,
        float knockbackDuration,
        float slowPercentage,
        float slowDuration,
        float stunDuration)
    {
        _released = false;
        _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        RefreshVisualRotation();
        _damage = Mathf.Max(0, damage);
        _knockbackForce = Mathf.Max(0f, knockbackForce);
        _knockbackDuration = Mathf.Max(0f, knockbackDuration);
        _slowPercentage = Mathf.Clamp01(slowPercentage);
        _slowDuration = Mathf.Max(0f, slowDuration);
        _stunDuration = Mathf.Max(0f, stunDuration);
        _speed = Mathf.Max(0f, speed);
        _lifetime = Mathf.Max(0.01f, lifetime);
        _wallHitMode = wallHitMode;
        _targetMode = targetMode;
        _targetHitMode = targetHitMode;
        _owner = owner;
        _maxBounceCount = Mathf.Max(0, maxBounceCount);
        _currentBounceCount = 0;
        _dungeon = DungeonManager.Instance;
        _hitEnemies.Clear();
        ApplyFogVisibilityForActiveProjectile();
    }

    private void ApplyFogVisibilityForActiveProjectile()
    {
        if (_fogVisibility == null) return;

        _fogVisibility.enabled = true;
        _fogVisibility.ResetToVisible();
        _fogVisibility.RefreshVisibilityImmediate();
    }

    public void SetReleaseAction(Action<ProjectileController, ProjectileReleaseReason> releaseAction)
    {
        _releaseAction = releaseAction;
    }

    // Floor transition / pool owner cleanup only. Normal gameplay release paths stay inside ProjectileController.
    public void ReleaseForCleanup(ProjectileReleaseReason reason)
    {
        Release(reason);
    }

    private void Update()
    {
        bool measure = RuntimePerfTraceLogger.IsEnabled;
        long updateStart = measure ? RuntimePerfTraceLogger.Timestamp() : 0L;
        long moveTicks = 0L;
        long wallTicks = 0L;
        long hitTicks = 0L;
        long bounceTicks = 0L;

        float deltaTime = Time.deltaTime;

        _lifetime -= deltaTime;
        if (_lifetime <= 0f)
        {
            Release(ProjectileReleaseReason.LifetimeExpired);
            FlushProjectileUpdateMetrics(measure, updateStart, moveTicks, wallTicks, hitTicks, bounceTicks);
            return;
        }

        long moveStart = measure ? RuntimePerfTraceLogger.Timestamp() : 0L;
        Vector2 currentPosition = transform.position;
        Vector2 nextPosition = currentPosition + _direction * (_speed * deltaTime);
        if (measure) moveTicks += RuntimePerfTraceLogger.Timestamp() - moveStart;

        long boundsStart = measure ? RuntimePerfTraceLogger.Timestamp() : 0L;
        bool outOfBounds = IsOutOfDungeonBounds(nextPosition);
        if (measure) wallTicks += RuntimePerfTraceLogger.Timestamp() - boundsStart;

        if (outOfBounds)
        {
            Release(ProjectileReleaseReason.OutOfBounds);
            FlushProjectileUpdateMetrics(measure, updateStart, moveTicks, wallTicks, hitTicks, bounceTicks);
            return;
        }

        if (_wallHitMode == ProjectileWallHitMode.PassThrough)
        {
            transform.position = nextPosition;
        }
        else
        {
            long wallStart = measure ? RuntimePerfTraceLogger.Timestamp() : 0L;
            bool hitWall = IsWallPosition(nextPosition);
            if (measure) wallTicks += RuntimePerfTraceLogger.Timestamp() - wallStart;

            if (hitWall)
            {
                long bounceStart = measure ? RuntimePerfTraceLogger.Timestamp() : 0L;
                bool keepAlive = HandleWallHit(currentPosition, nextPosition);
                if (measure) bounceTicks += RuntimePerfTraceLogger.Timestamp() - bounceStart;

                if (!keepAlive)
                {
                    FlushProjectileUpdateMetrics(measure, updateStart, moveTicks, wallTicks, hitTicks, bounceTicks);
                    return;
                }
            }
            else
            {
                transform.position = nextPosition;
            }
        }

        long hitStart = measure ? RuntimePerfTraceLogger.Timestamp() : 0L;
        TryHitTarget();
        if (measure) hitTicks += RuntimePerfTraceLogger.Timestamp() - hitStart;

        FlushProjectileUpdateMetrics(measure, updateStart, moveTicks, wallTicks, hitTicks, bounceTicks);
    }

    private static void FlushProjectileUpdateMetrics(
        bool measure,
        long updateStart,
        long moveTicks,
        long wallTicks,
        long hitTicks,
        long bounceTicks)
    {
        if (!measure) return;

        RuntimePerfTraceLogger.RecordProjectileUpdate(
            RuntimePerfTraceLogger.Timestamp() - updateStart,
            moveTicks,
            wallTicks,
            hitTicks,
            bounceTicks);
    }

    // Map bounds 밖이면 wall mode와 무관하게 lifecycle을 끝낸다.
    // _dungeon/Data가 아직 준비되지 않은 짧은 윈도에서는 false로 두어 기존 동작을 유지한다.
    private bool IsOutOfDungeonBounds(Vector2 worldPosition)
    {
        if (_dungeon == null)
            _dungeon = DungeonManager.Instance;
        if (_dungeon == null || _dungeon.Data == null)
            return false;

        Vector2Int grid = _dungeon.WorldToGrid(worldPosition);
        return !_dungeon.Data.InBounds(grid.x, grid.y);
    }

    private bool IsWallPosition(Vector2 position)
    {
        if (_dungeon == null)
            _dungeon = DungeonManager.Instance;
        if (_dungeon == null || _dungeon.Data == null)
            return false;

        Vector2Int grid = _dungeon.WorldToGrid(position);
        return !_dungeon.IsWalkable(grid.x, grid.y);
    }

    private bool HandleWallHit(Vector2 currentPosition, Vector2 nextPosition)
    {
        switch (_wallHitMode)
        {
            case ProjectileWallHitMode.Destroy:
                Release(ProjectileReleaseReason.WallHitDestroy);
                return false;

            case ProjectileWallHitMode.PassThrough:
                transform.position = nextPosition;
                return true;

            case ProjectileWallHitMode.Bounce:
                return TryBounce(currentPosition, nextPosition);
        }

        return true;
    }

    private bool TryBounce(Vector2 currentPosition, Vector2 nextPosition)
    {
        if (_currentBounceCount >= _maxBounceCount)
        {
            Release(ProjectileReleaseReason.BounceLimit);
            return false;
        }

        Vector2 xOnlyPosition = new Vector2(nextPosition.x, currentPosition.y);
        Vector2 yOnlyPosition = new Vector2(currentPosition.x, nextPosition.y);
        bool blockedX = IsWallPosition(xOnlyPosition);
        bool blockedY = IsWallPosition(yOnlyPosition);

        if (blockedX)
            _direction.x *= -1f;
        if (blockedY)
            _direction.y *= -1f;
        if (!blockedX && !blockedY)
            _direction = -_direction;

        if (_direction.sqrMagnitude <= 0.0001f)
            _direction = Vector2.right;
        else
            _direction.Normalize();

        if (rotationMode == ProjectileRotationMode.FaceMoveDirection)
            RefreshVisualRotation();
        _currentBounceCount++;

        Vector2 correctedPosition = currentPosition + _direction * Mathf.Max(0.01f, bounceExitOffset);
        transform.position = IsWallPosition(correctedPosition)
            ? currentPosition
            : correctedPosition;

        return true;
    }

    private void RefreshVisualRotation()
    {
        switch (rotationMode)
        {
            case ProjectileRotationMode.KeepPrefabRotation:
                transform.localRotation = _initialLocalRotation;
                break;

            case ProjectileRotationMode.FaceMoveDirection:
                float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
                break;
        }
    }

    private void TryHitTarget()
    {
        switch (_targetMode)
        {
            case TargetMode.Player:
                TryHitPlayer();
                break;

            case TargetMode.Enemy:
                TryHitEnemy();
                break;
        }
    }

    private void TryHitPlayer()
    {
        if (!TryResolvePlayerCache())
            return;
        if (!s_PlayerCombat.IsAlive)
            return;

        float hitDistance = hitRadius + s_PlayerRadius;
        Vector2 delta = (Vector2)s_PlayerTransform.position - (Vector2)transform.position;
        if (delta.sqrMagnitude > hitDistance * hitDistance)
            return;

        s_PlayerCombat.ApplyEnemyCombatImpact(
            _damage,
            delta,
            _knockbackForce,
            _knockbackDuration,
            _slowPercentage,
            _slowDuration,
            _stunDuration);
        Release(ProjectileReleaseReason.PlayerHit);
    }

    private void TryHitEnemy()
    {
        int count = Physics2D.OverlapCircle(transform.position, hitRadius, CombatLayers.EnemyFilter, s_EnemyHitBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = s_EnemyHitBuffer[i];
            if (col == null) continue;

            if (!col.TryGetComponent(out EnemyController enemy) || !enemy.IsAlive) continue;
            if (ReferenceEquals(enemy, _owner)) continue;
            if (_targetHitMode != ProjectileTargetHitMode.DestroyOnHit && _hitEnemies.Contains(enemy)) continue;

            enemy.ApplyCombatImpact(
                _damage,
                transform.position,
                _knockbackForce,
                _knockbackDuration,
                _slowPercentage,
                _slowDuration);
            if (_targetHitMode == ProjectileTargetHitMode.DestroyOnHit)
            {
                Release(ProjectileReleaseReason.EnemyHit);
                return;
            }

            _hitEnemies.Add(enemy);
        }
    }

    private static bool TryResolvePlayerCache()
    {
        PlayerCombatController active = PlayerCombatController.Active;
        if (active == null || !active.isActiveAndEnabled)
        {
            ClearPlayerCache();
            return false;
        }

        if (!ReferenceEquals(s_PlayerCombat, active))
        {
            s_PlayerCombat = active;
            s_PlayerTransform = active.CachedPlayerTransform;
            s_PlayerRadius = active.CachedHitRadius;
        }

        return true;
    }

    private static void ClearPlayerCache()
    {
        s_PlayerCombat = null;
        s_PlayerTransform = null;
        s_PlayerRadius = 0f;
    }

    private void Release(ProjectileReleaseReason reason)
    {
        if (_released)
            return;

        _released = true;

        if (!RuntimePerfTraceLogger.IsEnabled)
        {
            if (_releaseAction != null)
            {
                _releaseAction(this, reason);
                return;
            }

            Destroy(gameObject);
            return;
        }

        long totalStart = Stopwatch.GetTimestamp();
        s_ReleaseMarker.Begin();

        if (_releaseAction != null)
        {
            long callbackStart = Stopwatch.GetTimestamp();
            _releaseAction(this, reason);
            long callbackTicks = Stopwatch.GetTimestamp() - callbackStart;

            s_ReleaseMarker.End();
            long totalTicks = Stopwatch.GetTimestamp() - totalStart;
            RuntimePerfTraceLogger.RecordRelease(totalTicks, callbackTicks);
            return;
        }

        s_ReleaseMarker.End();
        long fallbackTotal = Stopwatch.GetTimestamp() - totalStart;
        RuntimePerfTraceLogger.RecordRelease(fallbackTotal, 0L);
        RuntimePerfTraceLogger.RecordPoolReturn(ProjectileReleaseReason.FallbackDestroy, 0, 0, 0L, 0L);
        Destroy(gameObject);
    }
}
