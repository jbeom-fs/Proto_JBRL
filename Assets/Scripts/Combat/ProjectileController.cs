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

    private const float DefaultPlayerHitRadius = 0.5f;
    public enum TargetMode
    {
        Player,
        Enemy
    }

    private static PlayerCombatController s_PlayerCombat;
    private static Collider2D s_PlayerCollider;
    private static Transform s_PlayerTransform;
    private static float s_PlayerRadius = DefaultPlayerHitRadius;
    private static readonly Collider2D[] s_EnemyHitBuffer = new Collider2D[16];
    private static readonly ContactFilter2D s_NoFilter = ContactFilter2D.noFilter;

    private Vector2 _direction = Vector2.right;
    private float _speed = 6f;
    private int _damage = 1;
    private float _knockbackForce;
    private float _knockbackDuration;
    private float _slowPercentage;
    private float _slowDuration;
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
    private Action<ProjectileController, ProjectileReleaseReason> _releaseAction;
    private bool _released;
    private DungeonManager _dungeon;
    private readonly HashSet<EnemyController> _hitEnemies = new();

    private void Awake()
    {
        _collider = GetComponent<Collider2D>();
        _rigidbody = GetComponent<Rigidbody2D>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _animator = GetComponent<Animator>();
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
        if (_spriteRenderer != null && !_spriteRenderer.enabled)
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
        float slowDuration)
    {
        _released = false;
        _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        _damage = Mathf.Max(0, damage);
        _knockbackForce = Mathf.Max(0f, knockbackForce);
        _knockbackDuration = Mathf.Max(0f, knockbackDuration);
        _slowPercentage = Mathf.Clamp01(slowPercentage);
        _slowDuration = Mathf.Max(0f, slowDuration);
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
    }

    public void SetReleaseAction(Action<ProjectileController, ProjectileReleaseReason> releaseAction)
    {
        _releaseAction = releaseAction;
    }

    private void Update()
    {
        if (RuntimePerfTraceLogger.IsEnabled)
        {
            UpdateMeasured();
            return;
        }

        float deltaTime = Time.deltaTime;

        _lifetime -= deltaTime;
        if (_lifetime <= 0f)
        {
            Release(ProjectileReleaseReason.LifetimeExpired);
            return;
        }

        Vector2 currentPosition = transform.position;
        Vector2 nextPosition = currentPosition + _direction * (_speed * deltaTime);
        if (_wallHitMode == ProjectileWallHitMode.PassThrough)
        {
            transform.position = nextPosition;
        }
        else if (IsWallPosition(nextPosition))
        {
            if (!HandleWallHit(currentPosition, nextPosition))
                return;
        }
        else
        {
            transform.position = nextPosition;
        }

        TryHitTarget();
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

        _currentBounceCount++;

        Vector2 correctedPosition = currentPosition + _direction * Mathf.Max(0.01f, bounceExitOffset);
        transform.position = IsWallPosition(correctedPosition)
            ? currentPosition
            : correctedPosition;

        return true;
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

        s_PlayerCombat.TakeDamage(_damage);
        Release(ProjectileReleaseReason.PlayerHit);
    }

    private void TryHitEnemy()
    {
        int count = Physics2D.OverlapCircle(transform.position, hitRadius, s_NoFilter, s_EnemyHitBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = s_EnemyHitBuffer[i];
            if (col == null) continue;

            EnemyController enemy = col.GetComponent<EnemyController>();
            if (enemy == null)
                enemy = col.GetComponentInParent<EnemyController>();
            if (enemy == null || !enemy.IsAlive) continue;
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

    private void UpdateMeasured()
    {
        long updateStart = RuntimePerfTraceLogger.Timestamp();
        long moveTicks = 0L;
        long wallTicks = 0L;
        long hitTicks = 0L;
        long bounceTicks = 0L;

        float deltaTime = Time.deltaTime;

        _lifetime -= deltaTime;
        if (_lifetime <= 0f)
        {
            Release(ProjectileReleaseReason.LifetimeExpired);
            RuntimePerfTraceLogger.RecordProjectileUpdate(
                RuntimePerfTraceLogger.Timestamp() - updateStart,
                moveTicks,
                wallTicks,
                hitTicks,
                bounceTicks);
            return;
        }

        long moveStart = RuntimePerfTraceLogger.Timestamp();
        Vector2 currentPosition = transform.position;
        Vector2 nextPosition = currentPosition + _direction * (_speed * deltaTime);
        moveTicks += RuntimePerfTraceLogger.Timestamp() - moveStart;

        if (_wallHitMode == ProjectileWallHitMode.PassThrough)
        {
            transform.position = nextPosition;
        }
        else
        {
            long wallStart = RuntimePerfTraceLogger.Timestamp();
            bool hitWall = IsWallPosition(nextPosition);
            wallTicks += RuntimePerfTraceLogger.Timestamp() - wallStart;

            if (hitWall)
            {
                long bounceStart = RuntimePerfTraceLogger.Timestamp();
                bool keepAlive = HandleWallHit(currentPosition, nextPosition);
                bounceTicks += RuntimePerfTraceLogger.Timestamp() - bounceStart;
                if (!keepAlive)
                {
                    RuntimePerfTraceLogger.RecordProjectileUpdate(
                        RuntimePerfTraceLogger.Timestamp() - updateStart,
                        moveTicks,
                        wallTicks,
                        hitTicks,
                        bounceTicks);
                    return;
                }
            }
            else
            {
                transform.position = nextPosition;
            }
        }

        long hitStart = RuntimePerfTraceLogger.Timestamp();
        TryHitTarget();
        hitTicks += RuntimePerfTraceLogger.Timestamp() - hitStart;

        RuntimePerfTraceLogger.RecordProjectileUpdate(
            RuntimePerfTraceLogger.Timestamp() - updateStart,
            moveTicks,
            wallTicks,
            hitTicks,
            bounceTicks);
    }

    private static bool TryResolvePlayerCache()
    {
        if (s_PlayerCombat != null
            && s_PlayerCombat.isActiveAndEnabled
            && s_PlayerTransform != null)
            return true;

        s_PlayerCombat = UnityEngine.Object.FindAnyObjectByType<PlayerCombatController>();
        if (s_PlayerCombat == null || !s_PlayerCombat.isActiveAndEnabled)
        {
            ClearPlayerCache();
            return false;
        }

        s_PlayerTransform = s_PlayerCombat.transform;
        s_PlayerCollider = s_PlayerCombat.GetComponent<Collider2D>();
        if (s_PlayerCollider == null)
            s_PlayerCollider = s_PlayerCombat.GetComponentInParent<Collider2D>();
        if (s_PlayerCollider == null)
            s_PlayerCollider = s_PlayerCombat.GetComponentInChildren<Collider2D>();

        s_PlayerRadius = CalculateColliderRadius(s_PlayerCollider, DefaultPlayerHitRadius);
        return true;
    }

    private static void ClearPlayerCache()
    {
        s_PlayerCombat = null;
        s_PlayerCollider = null;
        s_PlayerTransform = null;
        s_PlayerRadius = DefaultPlayerHitRadius;
    }

    private static float CalculateColliderRadius(Collider2D collider, float fallback)
    {
        if (collider == null)
            return fallback;

        if (collider is CircleCollider2D circle)
        {
            Vector3 scale = circle.transform.lossyScale;
            return Mathf.Abs(circle.radius) * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }

        Bounds bounds = collider.bounds;
        return Mathf.Max(fallback, Mathf.Max(bounds.extents.x, bounds.extents.y));
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
