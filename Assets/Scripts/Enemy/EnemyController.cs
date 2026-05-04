// ═══════════════════════════════════════════════════════════════════
//  EnemyController.cs
//  책임: 적 HP 관리, 피해 수신, 사망 처리
//
//  알지 말아야 할 것:
//    • 플레이어 구현 세부사항
//    • 공격 패턴 계산 (AttackPattern 담당)
//    • 던전 생성 로직
// ═══════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class EnemyController : MonoBehaviour, IDamageable
{
    [Header("Data")]
    public EnemyData data;

    [Header("Events")]
    public CombatEventChannel combatChannel;

    [Header("Debug")]
    [SerializeField] private bool logDamageInEditor = false;

    [Header("Knockback Collision")]
    [SerializeField] private LayerMask knockbackBlockLayers;
    [SerializeField] private float knockbackWallSkin = 0.03f;

    private int             _currentHp;
    private EnemyHealthBar  _healthBar;
    private Rigidbody2D     _rb;
    private CircleCollider2D _circleCollider;
    private HitFlashFeedback _hitFlash;
    private static PhysicsMaterial2D s_NoFrictionMaterial;
    private float _knockbackLockTimer;
    private float _activeSlowPercentage;
    private Vector3 _lastSafePosition;
    private readonly Vector3[] _footprintCorners = new Vector3[4];
    private readonly List<SlowEffect> _activeSlows = new();

    private struct SlowEffect
    {
        public float Percentage;
        public float Timer;
    }

    public bool IsAlive => _currentHp > 0;
    public bool IsKnockbackLocked => _knockbackLockTimer > 0f;
    public float MoveSpeedMultiplier => Mathf.Clamp01(1f - _activeSlowPercentage);
    public float CollisionFootprintRadius => GetWorldColliderRadius();

    public event Action<EnemyController> OnDied;

    private void Awake()
    {
        ConfigurePhysics();
        _rb = GetComponent<Rigidbody2D>();
        _circleCollider = GetComponent<CircleCollider2D>();
        _healthBar = GetComponent<EnemyHealthBar>();
        _hitFlash = ResolveHitFlashFeedback();
        _lastSafePosition = transform.position;
        if (data != null)
        {
            _currentHp = data.maxHp;
            _healthBar?.SetHp(_currentHp, data.maxHp);
        }
        else
        {
            Debug.LogWarning($"[EnemyController] {gameObject.name}: EnemyData 없음 — HP가 0으로 설정됩니다.");
        }
    }

    /// <summary>프리팹 풀에서 꺼낼 때 데이터를 주입합니다.</summary>
    public void Initialize(EnemyData enemyData)
    {
        data       = enemyData;
        _currentHp = data.maxHp;
        _healthBar?.SetHp(_currentHp, data.maxHp);
        _lastSafePosition = transform.position;
        ResetStatusEffects();
        _hitFlash = ResolveHitFlashFeedback();
        _hitFlash?.ResetColor();
        gameObject.SetActive(true);

        if (TryGetComponent<EnemyBrain>(out var brain))
            brain.ResetRuntimeState();
    }

    public void TakeDamage(int damage)
    {
        if (!IsAlive) return;

        int actual = Mathf.Max(1, damage - (data?.defense ?? 0));
        int hpBefore = _currentHp;
        _currentHp = Mathf.Max(0, _currentHp - actual);
        if (_currentHp < hpBefore)
            _hitFlash?.Play();
        _healthBar?.SetHp(_currentHp, data.maxHp);

#if UNITY_EDITOR
        if (logDamageInEditor)
            Debug.Log($"[Enemy:{data?.enemyName}] -{actual} HP → {_currentHp}/{data?.maxHp}");
#endif

        if (_currentHp == 0) Die();
    }

    public void ApplyCombatImpact(
        int damage,
        Vector2 attackerPosition,
        float knockbackForce,
        float knockbackDuration,
        float slowPercentage,
        float slowDuration)
    {
        TakeDamage(damage);
        if (!IsAlive) return;

        ApplyKnockback(attackerPosition, knockbackForce, knockbackDuration);
        ApplySlow(slowPercentage, slowDuration);
    }

    private void Update()
    {
        if (_knockbackLockTimer > 0f)
        {
            _knockbackLockTimer -= Time.deltaTime;

            if (_knockbackLockTimer <= 0f && _rb != null)
                _rb.linearVelocity = Vector2.zero;
        }

        TickSlowEffects(Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (!IsAlive) return;

        if (IsFootprintWalkable(transform.position))
        {
            _lastSafePosition = transform.position;
            return;
        }

        // 몬스터끼리 또는 플레이어와 물리 충돌로 밀려도 벽/닫힌 문 타일 안으로 들어가면 즉시 되돌립니다.
        // AI 이동, 분리 벡터, 넉백 이후에 한 번 더 검증해서 던전 그리드 경계를 최종적으로 보장합니다.
        transform.position = _lastSafePosition;
        if (_rb != null)
            _rb.linearVelocity = Vector2.zero;
    }

    private void Die()
    {
        combatChannel?.RaiseEnemyKilled(this);
        OnDied?.Invoke(this);
#if UNITY_EDITOR
        if (logDamageInEditor)
            Debug.Log($"[Enemy:{data?.enemyName}] 사망");
#endif
        gameObject.SetActive(false);
    }

    public void ResetStatusEffects()
    {
        _knockbackLockTimer = 0f;
        _activeSlowPercentage = 0f;
        _activeSlows.Clear();
        if (_rb != null)
            _rb.linearVelocity = Vector2.zero;
    }

    private HitFlashFeedback ResolveHitFlashFeedback()
    {
        HitFlashFeedback feedback = GetComponentInChildren<HitFlashFeedback>(true);
        return feedback != null ? feedback : gameObject.AddComponent<HitFlashFeedback>();
    }

    private void ApplyKnockback(Vector2 attackerPosition, float force, float duration)
    {
        if (force <= 0f || duration <= 0f || _rb == null) return;

        float resistance = data != null ? Mathf.Clamp01(data.knockbackResistance) : 0f;
        float finalForce = force * (1f - resistance);
        if (finalForce <= 0f) return;

        Vector2 direction = ((Vector2)transform.position - attackerPosition).normalized;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector2.up;

        finalForce = ClampKnockbackForceAgainstWall(direction, finalForce, duration);
        if (finalForce <= 0f) return;

        // 물리 임펄스는 즉시 튕기는 맛을 내고, lock 타이머 동안 AI 조향이 이를 덮어쓰지 못하게 합니다.
        _knockbackLockTimer = Mathf.Max(_knockbackLockTimer, duration);
        _rb.linearVelocity = Vector2.zero;
        _rb.AddForce(direction * finalForce, ForceMode2D.Impulse);
    }

    private float ClampKnockbackForceAgainstWall(Vector2 direction, float force, float duration)
    {
        int wallMask = GetKnockbackBlockMask();
        if (duration <= 0f) return force;

        float mass = _rb != null ? Mathf.Max(0.01f, _rb.mass) : 1f;
        float expectedDistance = force / mass * duration;
        if (expectedDistance <= 0f) return force;

        float clampedForce = force;
        float radius = GetWorldColliderRadius();
        Vector2 origin = _circleCollider != null
            ? (Vector2)transform.TransformPoint(_circleCollider.offset)
            : (Vector2)transform.position;

        // 넉백 전에 같은 반지름으로 CircleCast를 쏴서 벽까지 남은 거리를 확인합니다.
        // 벽이 예상 넉백 거리 안에 있으면 임펄스 크기를 줄여 콜라이더가 벽 안으로 파고들지 않게 합니다.
        if (wallMask != 0)
        {
            RaycastHit2D hit = Physics2D.CircleCast(origin, radius, direction, expectedDistance, wallMask);
            if (hit.collider != null)
            {
                float safeDistance = Mathf.Max(0f, hit.distance - knockbackWallSkin);
                float physicsClampedForce = safeDistance <= 0f
                    ? 0f
                    : force * Mathf.Clamp01(safeDistance / expectedDistance);

                clampedForce = Mathf.Min(clampedForce, physicsClampedForce);
            }
        }

        float gridClampedForce = ClampKnockbackForceAgainstWalkableGrid(
            origin,
            direction,
            force,
            expectedDistance,
            radius);

        return Mathf.Min(clampedForce, gridClampedForce);
    }

    private int GetKnockbackBlockMask()
    {
        if (knockbackBlockLayers.value != 0)
            return knockbackBlockLayers.value;

        return LayerMask.GetMask("Wall", "Obstacle", "Obstacles");
    }

    private float ClampKnockbackForceAgainstWalkableGrid(
        Vector2 origin,
        Vector2 direction,
        float force,
        float expectedDistance,
        float radius)
    {
        var dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null || dungeonManager.Data == null) return force;
        if (!IsFootprintWalkable(origin)) return force;

        float step = Mathf.Max(0.05f, radius * 0.5f);
        int steps = Mathf.CeilToInt(expectedDistance / step);
        float lastSafeDistance = 0f;

        for (int i = 1; i <= steps; i++)
        {
            float distance = Mathf.Min(expectedDistance, i * step);
            Vector2 candidate = origin + direction * distance;

            if (!IsFootprintWalkable(candidate))
                break;

            lastSafeDistance = distance;
        }

        float safeDistance = Mathf.Max(0f, lastSafeDistance - knockbackWallSkin);
        if (safeDistance <= 0f) return 0f;

        return force * Mathf.Clamp01(safeDistance / expectedDistance);
    }

    private float GetWorldColliderRadius()
    {
        if (_circleCollider == null)
            return 0.32f;

        float maxScale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y));

        return Mathf.Max(0.01f, _circleCollider.radius * maxScale);
    }

    private bool IsFootprintWalkable(Vector3 position)
    {
        var dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null || dungeonManager.Data == null) return true;

        float radius = GetWorldColliderRadius();
        _footprintCorners[0] = new Vector3(position.x - radius, position.y - radius, 0f);
        _footprintCorners[1] = new Vector3(position.x + radius, position.y - radius, 0f);
        _footprintCorners[2] = new Vector3(position.x - radius, position.y + radius, 0f);
        _footprintCorners[3] = new Vector3(position.x + radius, position.y + radius, 0f);

        for (int i = 0; i < _footprintCorners.Length; i++)
        {
            Vector2Int grid = dungeonManager.WorldToGrid(_footprintCorners[i]);
            if (!dungeonManager.IsWalkable(grid.x, grid.y))
                return false;
        }

        return true;
    }

    private void ApplySlow(float percentage, float duration)
    {
        if (percentage <= 0f || duration <= 0f) return;

        float clamped = Mathf.Clamp01(percentage);

        // 슬로우는 무한 중첩하지 않고, 활성 효과 목록 중 가장 강한 감속만 이동 속도에 반영합니다.
        _activeSlows.Add(new SlowEffect
        {
            Percentage = clamped,
            Timer = duration
        });
        RecalculateStrongestSlow();
    }

    private void TickSlowEffects(float deltaTime)
    {
        if (_activeSlows.Count == 0) return;

        for (int i = _activeSlows.Count - 1; i >= 0; i--)
        {
            SlowEffect effect = _activeSlows[i];
            effect.Timer -= deltaTime;

            if (effect.Timer <= 0f)
                _activeSlows.RemoveAt(i);
            else
                _activeSlows[i] = effect;
        }

        RecalculateStrongestSlow();
    }

    private void RecalculateStrongestSlow()
    {
        float strongest = 0f;
        for (int i = 0; i < _activeSlows.Count; i++)
            strongest = Mathf.Max(strongest, _activeSlows[i].Percentage);

        _activeSlowPercentage = strongest;
    }

    private void ConfigurePhysics()
    {
        // 적은 물리 충돌로 서로 밀려야 하므로 Dynamic Rigidbody2D를 사용하고, 2D 탑다운이라 중력/회전은 막습니다.
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.sharedMaterial = GetNoFrictionMaterial();
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        CircleCollider2D circle = GetComponent<CircleCollider2D>();
        if (circle == null)
            circle = gameObject.AddComponent<CircleCollider2D>();
        circle.isTrigger = false;
        circle.radius = 0.32f;
        circle.offset = Vector2.zero;
        circle.sharedMaterial = GetNoFrictionMaterial();

        foreach (BoxCollider2D box in GetComponents<BoxCollider2D>())
            box.enabled = false;

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0)
            gameObject.layer = enemyLayer;

        _circleCollider = circle;
    }

    private static PhysicsMaterial2D GetNoFrictionMaterial()
    {
        if (s_NoFrictionMaterial != null) return s_NoFrictionMaterial;

        s_NoFrictionMaterial = new PhysicsMaterial2D("NoFriction")
        {
            friction = 0f,
            bounciness = 0f
        };
        return s_NoFrictionMaterial;
    }
}
