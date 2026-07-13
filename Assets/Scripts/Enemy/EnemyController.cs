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
[RequireComponent(typeof(EnemyInventory))]
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
    private EnemyAilmentIndicator _ailmentIndicator;
    private Rigidbody2D     _rb;
    private CircleCollider2D _circleCollider;
    private EnemyInventory _inventory;
    private HitFlashFeedback _hitFlash;
    private EnemyAnimationController _animationController;
    private float _knockbackLockTimer;
    private float _activeSlowPercentage;
    private float _stunRemaining;
    private float _deathTimer;
    private bool _deathFinished;
    private bool _holdsEliteKey;
    private Vector3 _lastSafePosition;
    private bool _warnedMissingHitFlash;
    private int _lastAilmentFlashFrame = -1;
    private EnemyAilments _ailments;
    private readonly List<SlowEffect> _activeSlows = new();

    private struct SlowEffect
    {
        public float Percentage;
        public float Timer;
    }

    public bool IsAlive => _currentHp > 0 && !IsDead;
    public bool IsDead { get; private set; }
    public int CurrentHp => _currentHp;
    public int MaxHp => data?.maxHp ?? 0;
    public string DisplayName => data?.enemyName ?? string.Empty;
    public bool HoldsEliteKey => _holdsEliteKey;
    public bool IsKnockbackLocked => _knockbackLockTimer > 0f;
    public bool IsSlowed => _activeSlowPercentage > 0f;
    public bool IsStunned => _stunRemaining > 0f;
    public float MoveSpeedMultiplier => IsStunned ? 0f : Mathf.Clamp01(1f - _activeSlowPercentage);
    public float CollisionFootprintRadius => GetWorldColliderRadius();
    /// <summary>Marker anchor in world space. Uses collider center instead of transform pivot.</summary>
    public Vector3 MarkerAnchorWorld =>
        _circleCollider != null
            ? transform.TransformPoint(_circleCollider.offset)
            : transform.position;

    public event Action<EnemyController> OnDied;
    public event Action<EnemyController> OnDeathFinished;

    private void Awake()
    {
        (_rb, _circleCollider) = CharacterPhysicsSetup.Configure(gameObject, "Enemy");
        _inventory = GetComponent<EnemyInventory>();
        _healthBar = GetComponent<EnemyHealthBar>();
        _ailmentIndicator = GetComponent<EnemyAilmentIndicator>();
        _hitFlash = ResolveHitFlashFeedback();
        _animationController = GetComponentInChildren<EnemyAnimationController>(true);
        _ailments = new EnemyAilments(ApplyAilmentTickDamage);
        _lastSafePosition = transform.position;
        ApplyStationaryPhysicsSettings();
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
        DaggerMarkerRegistry.Instance.Clear(this);
        data       = enemyData;
        if (_inventory == null)
            _inventory = GetComponent<EnemyInventory>();
        _inventory?.Clear();
        _currentHp = data.maxHp;
        IsDead = false;
        _holdsEliteKey = false;
        _deathFinished = false;
        _deathTimer = 0f;
        _healthBar?.SetBarSuppressed(false);
        _ailmentIndicator?.SetSuppressed(false);
        _healthBar?.SetHp(_currentHp, data.maxHp);
        _lastSafePosition = transform.position;
        ResetStatusEffects();
        ApplyStationaryPhysicsSettings();
        if (_circleCollider != null)
            _circleCollider.enabled = true;
        _hitFlash = ResolveHitFlashFeedback();
        _hitFlash?.ResetColor();
        gameObject.SetActive(true);
        _animationController = GetComponentInChildren<EnemyAnimationController>(true);
        _animationController?.ResetAnimationState();

        if (TryGetComponent<EnemyBrain>(out var brain))
            brain.ResetRuntimeState();
    }

    public void TakeDamage(int damage)
    {
        ApplyDamageReturningActual(damage);
    }

    public void ApplyAilment(AilmentType type, float tickDamage, float duration)
    {
        if (IsDead || !IsAlive)
            return;

        _ailments?.Apply(type, tickDamage, duration);
    }

    public void ApplyStun(float duration)
    {
        if (duration <= 0f || IsDead || !IsAlive)
            return;

        _stunRemaining = Mathf.Max(_stunRemaining, duration);
    }

    public void ApplyAilments(AilmentApplication[] ailments, float damageMultiplier)
    {
        if (IsDead || !IsAlive || ailments == null || ailments.Length == 0)
            return;

        ApplyAilmentsOfType(ailments, AilmentType.Bleed, damageMultiplier);
        ApplyAilmentsOfType(ailments, AilmentType.Poison, damageMultiplier);
    }

    private void ApplyAilmentsOfType(AilmentApplication[] ailments, AilmentType type, float damageMultiplier)
    {
        for (int i = 0; i < ailments.Length; i++)
        {
            AilmentApplication entry = ailments[i];
            if (entry.type != type)
                continue;

            _ailments?.Apply(entry.type, entry.tickDamage * damageMultiplier, entry.duration);
        }
    }

    public int GetAilmentStacks(AilmentType type)
    {
        return _ailments != null ? _ailments.GetStacks(type) : 0;
    }

    private int ApplyDamageReturningActual(int damage)
    {
        if (IsDead || !IsAlive) return 0;

        int actual = Mathf.Max(1, damage - (data?.defense ?? 0));
        _currentHp = Mathf.Max(0, _currentHp - actual);
        _healthBar?.SetHp(_currentHp, data.maxHp);

#if UNITY_EDITOR
        if (logDamageInEditor)
            Debug.Log($"[Enemy:{data?.enemyName}] -{actual} HP → {_currentHp}/{data?.maxHp}");
#endif

        if (_currentHp == 0) Die();
        return actual;
    }

    private void ApplyAilmentTickDamage(int damage)
    {
        if (IsDead || !IsAlive)
            return;

        FlashAilmentTickOnce();

        int maxHp = data != null ? data.maxHp : 0;
        _currentHp = Mathf.Max(0, _currentHp - damage);
        _healthBar?.SetHp(_currentHp, maxHp);

#if UNITY_EDITOR
        if (logDamageInEditor)
            Debug.Log($"[Enemy:{data?.enemyName}] 상태이상 틱 -{damage} HP → {_currentHp}/{maxHp}");
#endif

        if (_currentHp == 0)
            Die();
    }

    private void FlashAilmentTickOnce()
    {
        if (_lastAilmentFlashFrame == Time.frameCount)
            return;

        if (_ailments == null || !_ailments.TryGetFirstActiveType(out AilmentType type))
            return;

        _lastAilmentFlashFrame = Time.frameCount;
        _hitFlash?.Flash(ToStatusEffectIconType(type));
    }

    private static StatusEffectIconType ToStatusEffectIconType(AilmentType type)
    {
        return type == AilmentType.Bleed ? StatusEffectIconType.Bleed : StatusEffectIconType.Poison;
    }

    // Developer console only: routes through the normal death pipeline.
    internal void ForceKillForDebug()
    {
        if (IsDead || !IsAlive)
            return;

        _currentHp = 0;
        _healthBar?.SetHp(_currentHp, data != null ? data.maxHp : 0);
        Die();
    }

    public void MarkAsEliteKeyHolder()
    {
        _holdsEliteKey = true;
        if (_inventory == null)
            _inventory = GetComponent<EnemyInventory>();
        _inventory?.AddDropItem(DeterministicSeedUtility.EliteKeyDomain);
    }

    public void RollDrops(EnemyDropGroup group, System.Random rng)
    {
        if (_inventory == null)
            _inventory = GetComponent<EnemyInventory>();

        EnemyDropRoller.Roll(group, _inventory, rng);
    }

    public void ClearEliteKeyHolder()
    {
        _holdsEliteKey = false;
    }

    public void ClearDropInventory()
    {
        if (_inventory == null)
            _inventory = GetComponent<EnemyInventory>();
        _inventory?.Clear();
    }

    public int ApplyCombatImpact(
        int damage,
        Vector2 attackerPosition,
        float knockbackForce,
        float knockbackDuration,
        float slowPercentage,
        float slowDuration,
        AilmentApplication[] ailments,
        float ailmentDamageMultiplier)
    {
        if (IsDead) return 0;

        int actualDamage = ApplyDamageReturningActual(damage);
        if (!IsAlive) return actualDamage;

        ApplyKnockback(attackerPosition, knockbackForce, knockbackDuration);
        ApplySlow(slowPercentage, slowDuration);
        ApplyAilments(ailments, ailmentDamageMultiplier);
        return actualDamage;
    }

    private void Update()
    {
        if (IsDead)
        {
            TickDeathDelay();
            return;
        }

        if (_knockbackLockTimer > 0f)
        {
            _knockbackLockTimer -= Time.deltaTime;

            if (_knockbackLockTimer <= 0f && _rb != null)
                _rb.linearVelocity = Vector2.zero;
        }

        TickSlowEffects(Time.deltaTime);
        TickStunEffect(Time.deltaTime);
        _ailments?.Tick(Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (IsDead || !IsAlive) return;

        // 이전 프레임에서 walkable로 검증된 위치 그대로면 4-corner 그리드 룩업을 건너뛴다.
        // _lastSafePosition은 walkable 분기에서만 갱신되므로 같은 좌표면 이미 안전이 보장돼 있다.
        Vector3 currentPosition = transform.position;
        if (currentPosition == _lastSafePosition) return;

        if (IsFootprintWalkable(currentPosition))
        {
            _lastSafePosition = currentPosition;
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
        if (IsDead) return;

        IsDead = true;
        _deathFinished = false;
        _deathTimer = data != null ? Mathf.Max(0f, data.deathDelay) : 0.5f;
        ResetStatusEffects();
        if (_circleCollider != null)
            _circleCollider.enabled = false;

        if (TryGetComponent<EnemyBrain>(out var brain))
            brain.HandleDeathStarted();
        DropInventoryItems();
        _animationController?.TriggerDeath();
        combatChannel?.RaiseEnemyKilled(this);
        OnDied?.Invoke(this);
#if UNITY_EDITOR
        if (logDamageInEditor)
            Debug.Log($"[Enemy:{data?.enemyName}] 사망");
#endif
        if (_deathTimer <= 0f)
            FinishDeath();
    }

    private void DropInventoryItems()
    {
        if (_inventory == null)
            _inventory = GetComponent<EnemyInventory>();
        if (_inventory == null)
            return;

        _holdsEliteKey = false;
        if (DropItemSpawner.Instance != null)
        {
            DropItemSpawner.Instance.SpawnDrops(_inventory, transform.position);
        }
        else
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[EnemyController] DropItemSpawner.Instance is missing; enemy drops were skipped.", this);
#endif
        }
        _inventory.Clear();
    }

    private void TickDeathDelay()
    {
        if (_deathFinished)
            return;

        _deathTimer -= Time.deltaTime;
        if (_deathTimer <= 0f)
            FinishDeath();
    }

    private void FinishDeath()
    {
        if (_deathFinished)
            return;

        _deathFinished = true;
        OnDeathFinished?.Invoke(this);
        gameObject.SetActive(false);
    }

    public void ResetStatusEffects()
    {
        _knockbackLockTimer = 0f;
        _activeSlowPercentage = 0f;
        _stunRemaining = 0f;
        _lastAilmentFlashFrame = -1;
        _activeSlows.Clear();
        _ailments?.Clear();
        _hitFlash?.ResetColor();
        if (_rb != null)
            _rb.linearVelocity = Vector2.zero;
    }

    private HitFlashFeedback ResolveHitFlashFeedback()
    {
        HitFlashFeedback feedback = GetComponentInChildren<HitFlashFeedback>(true);
        if (feedback != null)
            return feedback;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_warnedMissingHitFlash)
        {
            Debug.LogWarning("[EnemyController] HitFlashFeedback 참조가 없어 상태이상 틱 플래시를 생략합니다.", this);
            _warnedMissingHitFlash = true;
        }
#endif
        return null;
    }

    private void ApplyKnockback(Vector2 attackerPosition, float force, float duration)
    {
        if (force <= 0f || duration <= 0f || _rb == null) return;
        if (data != null && data.immuneToKnockback)
        {
            _rb.linearVelocity = Vector2.zero;
            return;
        }

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

        return CombatLayers.WallMask;
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
        return WorldEnvironmentQuery.IsFootprintWalkable(position, GetWorldColliderRadius());
    }

    private void ApplyStationaryPhysicsSettings()
    {
        if (_rb == null)
            return;

        _rb.constraints = data != null && data.isStationary
            ? RigidbodyConstraints2D.FreezeAll
            : RigidbodyConstraints2D.FreezeRotation;
        _rb.linearVelocity = Vector2.zero;
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

        bool anyExpired = false;
        for (int i = _activeSlows.Count - 1; i >= 0; i--)
        {
            SlowEffect effect = _activeSlows[i];
            effect.Timer -= deltaTime;

            if (effect.Timer <= 0f)
            {
                _activeSlows.RemoveAt(i);
                anyExpired = true;
            }
            else
                _activeSlows[i] = effect;
        }

        // 슬로우 강도는 Percentage 기준이며 timer 감소만으로는 바뀌지 않는다.
        // 따라서 만료가 발생한 프레임에만 재계산하면 충분하고 ApplySlow가 신규 추가 케이스를 커버한다.
        if (anyExpired)
            RecalculateStrongestSlow();
    }

    private void TickStunEffect(float deltaTime)
    {
        if (_stunRemaining <= 0f) return;

        _stunRemaining = Mathf.Max(0f, _stunRemaining - deltaTime);
    }

    private void RecalculateStrongestSlow()
    {
        float strongest = 0f;
        for (int i = 0; i < _activeSlows.Count; i++)
            strongest = Mathf.Max(strongest, _activeSlows[i].Percentage);

        _activeSlowPercentage = strongest;
    }

}
