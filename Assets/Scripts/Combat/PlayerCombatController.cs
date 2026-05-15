// ═══════════════════════════════════════════════════════════════════
//  PlayerCombatController.cs
//  Application Layer — 플레이어 전투 (공격·스킬·피해 수신)
//
//  책임:
//    • WeaponData 기반 기본 공격 (Space)
//    • SkillData 기반 스킬 사용 (1~4)
//    • HP / MP 관리 및 이벤트 발행
//
//  알지 말아야 할 것:
//    • 이동 로직 (PlayerController 담당)
//    • 적 AI
//    • 던전 생성
// ═══════════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PlayerDashController))]
[RequireComponent(typeof(PlayerInputReader))]
public class PlayerCombatController : MonoBehaviour, IDamageable
{
    private const int SkillSlotCount = 4;
    private const float DefaultPlayerHitRadius = 0.5f;

    public static PlayerCombatController Active { get; private set; }

    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("Dependencies")]
    public CombatEventChannel combatChannel;
    public PlayerController  playerMovement;

    [Header("기본 스탯")]
    [SerializeField] private int maxHp      = 20;
    [SerializeField] private int maxMp      = 10;
    [SerializeField] private int baseAttack  = 3;
    [SerializeField] private int baseDefense = 1;

    [Header("무기 (런타임에 EquipWeapon()으로 교체 가능)")]
    public WeaponData currentWeapon;

    [Header("피해 감지")]
    [Tooltip("공격 판정 반경 (월드 단위). 타일 크기의 약 40% 권장.")]
    [SerializeField] private float hitRadius = 0.3f;

    [Header("Damage Invincibility")]
    [SerializeField, Min(0f)] private float damageInvincibleDuration = 0.5f;

    // ── 런타임 상태 ─────────────────────────────────────────────────

    private readonly PlayerResource _resource = new();
    private readonly SkillCooldownController _cooldownController = new();
    private readonly SkillSlotRuntime[] _skillSlots = CreateSkillSlots();
    private AttackExecutor _attackExecutor;
    private SkillExecutor _skillExecutor;
    private PlayerInputReader _inputReader;
    private PlayerDashController _dashController;
    private HitFlashFeedback _hitFlash;
    [SerializeField] private PlayerInvincibilityFlashFeedback invincibilityFlashFeedback;
    private WeaponData _boundSkillWeapon;
    private float _damageInvincibleTimer;
    private int _externalInvincibilityCount;
    private Transform _cachedTransform;
    private Collider2D _cachedHitCollider;
    private float _cachedHitRadius = DefaultPlayerHitRadius;
    private Vector2Int _lastAimDirection = Vector2Int.down;
    private bool _isSkillCasting;
    private float _skillRecoveryTimer;
    private Coroutine _skillCastRoutine;
    private Coroutine _enemyKnockbackRoutine;
    private readonly List<PlayerSlowEffect> _enemySlows = new();
    private float _enemySlowMultiplier = 1f;
    private float _stunTimer;

    private struct PlayerSlowEffect
    {
        public float Multiplier;
        public float Timer;
    }

    // ── 공개 프로퍼티 ────────────────────────────────────────────────

    public bool IsAlive     => _resource.IsAlive && !IsDead;
    public bool IsDead { get; private set; }
    public int  CurrentHp   => _resource.CurrentHp;
    public int  MaxHp       => maxHp;
    public int  CurrentMp   => _resource.CurrentMp;
    public int  MaxMp       => maxMp;
    public bool IsDamageInvincible => _damageInvincibleTimer > 0f || HasExternalInvincibility;
    public bool HasExternalInvincibility => _externalInvincibilityCount > 0;
    public bool IsDashing => _dashController != null && _dashController.IsDashing;
    public bool IsSkillBusy => _isSkillCasting || _skillRecoveryTimer > 0f;
    public bool IsStunned => _stunTimer > 0f;
    public bool BlocksPlayerMovement => IsSkillBusy;
    public float MoveSpeedMultiplier => IsStunned ? 0f : _enemySlowMultiplier;

    public Transform   CachedPlayerTransform => _cachedTransform;
    public Collider2D  CachedHitCollider     => _cachedHitCollider;
    public float       CachedHitRadius       => _cachedHitRadius;
    public Vector2     CurrentAimDirection   => AimDirectionUtility.ToNormalizedDirection(_lastAimDirection);
    public Vector2Int  CurrentAimRawDirection => _lastAimDirection;

    /// <summary>무기 보정치가 합산된 최종 공격력.</summary>
    public int TotalAttack  => baseAttack  + (currentWeapon?.bonusAttack  ?? 0);

    /// <summary>무기 보정치가 합산된 최종 방어력.</summary>
    public int TotalDefense => baseDefense + (currentWeapon?.bonusDefense ?? 0);

    public event Action<PlayerCombatController> OnDied;

    // ══════════════════════════════════════════════════════════════
    //  초기화
    // ══════════════════════════════════════════════════════════════

    private void Awake()
    {
        _resource.Initialize(maxHp, maxMp);
        CachePlayerHitInfo();
        RegisterAsActive();
        _attackExecutor = new AttackExecutor(transform, this, CombatLayers.EnemyFilter);
        _skillExecutor = new SkillExecutor(_attackExecutor);
        BindSkillSlots(currentWeapon);
        _inputReader = GetComponent<PlayerInputReader>();
        _dashController = GetComponent<PlayerDashController>();
        _hitFlash = ResolveHitFlashFeedback();
        if (invincibilityFlashFeedback == null)
            invincibilityFlashFeedback = ResolveInvincibilityFlashFeedback();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (combatChannel == null)
            Debug.LogWarning("[PlayerCombatController] CombatEventChannel 없음 — HP/MP/스킬 UI 이벤트가 발행되지 않습니다.", this);
        if (playerMovement == null)
            Debug.LogWarning("[PlayerCombatController] PlayerController 없음 — 공격 방향이 기본 방향을 사용합니다.", this);
        if (_inputReader == null)
            Debug.LogError("[PlayerCombatController] PlayerInputReader가 없습니다 — RequireComponent로 추가되어야 합니다.", this);
        if (_dashController == null)
            Debug.LogError("[PlayerCombatController] PlayerDashController가 없습니다 — RequireComponent로 추가되어야 합니다.", this);
        if (_hitFlash == null)
            Debug.LogWarning("[PlayerCombatController] HitFlashFeedback을 찾지 못했습니다 — 자식에 추가하거나 SerializeField로 연결하세요.", this);
        if (invincibilityFlashFeedback == null)
            Debug.LogWarning("[PlayerCombatController] PlayerInvincibilityFlashFeedback을 찾지 못했습니다 — 자식에 추가하거나 SerializeField로 연결하세요.", this);
#endif
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Active, this))
            Active = null;
    }

    private void OnDisable()
    {
        ClearSkillTimingState();
        ClearEnemyImpactState();
    }

    private void RegisterAsActive()
    {
        if (Active == null || ReferenceEquals(Active, this))
        {
            Active = this;
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning(
            $"[PlayerCombatController] Active 인스턴스가 이미 존재합니다 ({Active.name}) — 새 인스턴스({name})로 교체합니다.",
            this);
#endif
        Active = this;
    }

    private void CachePlayerHitInfo()
    {
        _cachedTransform = transform;
        _cachedHitCollider = GetComponent<Collider2D>();
        if (_cachedHitCollider == null)
            _cachedHitCollider = GetComponentInChildren<Collider2D>();
        _cachedHitRadius = CalculateColliderRadius(_cachedHitCollider, DefaultPlayerHitRadius);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_cachedHitCollider == null)
            Debug.LogWarning(
                "[PlayerCombatController] hit Collider2D를 찾지 못했습니다 — 기본 hit radius로 fallback합니다.",
                this);
#endif
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

    // ══════════════════════════════════════════════════════════════
    //  무기 장착 — WeaponData 하나만 넣으면 공격·스킬 전부 교체됨
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 무기를 교체합니다. 스킬 쿨다운이 초기화됩니다.
    /// </summary>
    public void EquipWeapon(WeaponData weapon)
    {
        currentWeapon   = weapon;
        _cooldownController.ResetAll();
        BindSkillSlots(weapon);
#if UNITY_EDITOR
        Debug.Log($"[Combat] 무기 장착: {weapon?.weaponName ?? "없음"}");
#endif
    }

    // ══════════════════════════════════════════════════════════════
    //  매 프레임 처리
    // ══════════════════════════════════════════════════════════════

    private void Update()
    {
        if (IsDead)
            return;

        if (_damageInvincibleTimer > 0f)
            _damageInvincibleTimer -= Time.deltaTime;

        TickEnemyStun(Time.deltaTime);
        TickEnemySlowEffects(Time.deltaTime);
        EnsureSkillSlotsBound();
        _cooldownController.Tick(Time.deltaTime);
        TickSkillSlots(Time.deltaTime);
        TickSkillRecovery(Time.deltaTime);

        if (_inputReader == null) return;

        if (DungeonManager.Instance != null && DungeonManager.Instance.IsTransitioning) return;
        if (IsDashing) return;
        if (IsStunned) return;
        if (IsSkillBusy) return;

        RefreshAimDirection();

        if (_inputReader.WasBasicAttackPressed)  TryBasicAttack();
        if (_inputReader.WasSkillPressed(0)) TryUseSkill(0);
        if (_inputReader.WasSkillPressed(1)) TryUseSkill(1);
        if (_inputReader.WasSkillPressed(2)) TryUseSkill(2);
        if (_inputReader.WasSkillPressed(3)) TryUseSkill(3);
    }

    public Vector2 RefreshAimDirection()
    {
        if (IsStunned)
            return CurrentAimDirection;

        if (_inputReader != null &&
            AimDirectionUtility.TryGetEightWayRaw(_inputReader.MoveInput, out Vector2Int rawDirection))
        {
            _lastAimDirection = rawDirection;
        }

        return CurrentAimDirection;
    }

    // ══════════════════════════════════════════════════════════════
    //  기본 공격
    // ══════════════════════════════════════════════════════════════

    private void TryBasicAttack()
    {
        if (IsDead) return;
        if (IsDashing) return;
        if (IsStunned) return;
        if (IsSkillBusy) return;
        if (!_cooldownController.IsAttackReady || currentWeapon == null) return;

        _cooldownController.SetAttackCooldown(currentWeapon.attackCooldown);
        _attackExecutor.BeginAttackActivation();

        var targets = ResolveTargets(
            currentWeapon.attackPattern,
            currentWeapon.patternRange);

        _attackExecutor.ExecuteAttack(
            targets,
            TotalAttack + currentWeapon.damage,
            currentWeapon.canPenetrateWalls,
            currentWeapon.basicAttackMultiTarget,
            currentWeapon.knockbackForce,
            currentWeapon.knockbackDuration,
            currentWeapon.slowPercentage,
            currentWeapon.slowDuration,
            hitRadius);
    }

    // ══════════════════════════════════════════════════════════════
    //  스킬 사용
    // ══════════════════════════════════════════════════════════════

    private void TryUseSkill(int slotIndex)
    {
        if (IsDead) return;
        if (IsDashing) return;
        if (IsStunned) return;
        if (IsSkillBusy) return;
        EnsureSkillSlotsBound();
        SkillSlotRuntime slot = GetSkillSlot(slotIndex);
        if (slot == null) return;
        if (!slot.CanUse(CurrentMp)) return;

        SkillData skill = slot.Data;
        float castDelay = Mathf.Max(0f, skill.castDelay);
        if (castDelay > 0f)
        {
            BeginSkillCast(slotIndex, skill, castDelay);
            return;
        }

        ExecuteSkillIfReady(slotIndex, skill);
    }

    // ══════════════════════════════════════════════════════════════
    //  공통 헬퍼
    // ══════════════════════════════════════════════════════════════

    private void BeginSkillCast(int slotIndex, SkillData skill, float castDelay)
    {
        if (_skillCastRoutine != null)
            StopCoroutine(_skillCastRoutine);

        _isSkillCasting = true;
        _skillCastRoutine = StartCoroutine(SkillCastRoutine(slotIndex, skill, castDelay));
    }

    private IEnumerator SkillCastRoutine(int slotIndex, SkillData skill, float castDelay)
    {
        float remaining = castDelay;
        while (remaining > 0f)
        {
            if (IsDead || !isActiveAndEnabled)
            {
                _isSkillCasting = false;
                _skillCastRoutine = null;
                yield break;
            }

            remaining -= Time.deltaTime;
            yield return null;
        }

        _isSkillCasting = false;
        _skillCastRoutine = null;
        ExecuteSkillIfReady(slotIndex, skill);
    }

    private bool ExecuteSkillIfReady(int slotIndex, SkillData expectedSkill)
    {
        if (IsDead) return false;
        if (IsDashing) return false;
        if (IsStunned) return false;
        if (DungeonManager.Instance != null && DungeonManager.Instance.IsTransitioning) return false;

        EnsureSkillSlotsBound();
        SkillSlotRuntime slot = GetSkillSlot(slotIndex);
        if (slot == null) return false;
        if (!ReferenceEquals(slot.Data, expectedSkill)) return false;
        if (!slot.CanUse(CurrentMp)) return false;

        SkillData skill = slot.Data;
        SkillExecutionContext context = CreateSkillExecutionContext(skill, slotIndex);
        if (!_skillExecutor.Execute(context)) return false;

        SpendMp(skill.mpCost);
        slot.StartCooldown();
        StartSkillRecovery(skill.recoveryDelay);
        combatChannel?.RaiseSkillUsed(skill);
#if UNITY_EDITOR
        Debug.Log($"[Combat] Skill [{slotIndex + 1}] {skill.skillName} used");
#endif

        return true;
    }

    private void TickSkillRecovery(float deltaTime)
    {
        if (_skillRecoveryTimer > 0f)
            _skillRecoveryTimer -= deltaTime;
    }

    private void StartSkillRecovery(float recoveryDelay)
    {
        _skillRecoveryTimer = Mathf.Max(_skillRecoveryTimer, Mathf.Max(0f, recoveryDelay));
    }

    private void ClearSkillTimingState()
    {
        if (_skillCastRoutine != null)
        {
            StopCoroutine(_skillCastRoutine);
            _skillCastRoutine = null;
        }

        _isSkillCasting = false;
        _skillRecoveryTimer = 0f;
    }

    private void ClearEnemyImpactState()
    {
        if (_enemyKnockbackRoutine != null)
        {
            StopCoroutine(_enemyKnockbackRoutine);
            _enemyKnockbackRoutine = null;
        }

        _enemySlows.Clear();
        _enemySlowMultiplier = 1f;
        _stunTimer = 0f;
    }

    private List<Vector2Int> ResolveTargets(AttackPatternType pattern, int range, float coneHalfAngle = 45f)
    {
        var dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null) return new List<Vector2Int>();

        var origin = dungeonManager.WorldToGrid(transform.position);

        // FacingDirection은 화면 공간 (Up키 → y=+1).
        // 그리드 좌표계는 GridToWorld에서 Y가 반전(tilemap y = -row)되므로
        // 화면 +Y = 그리드 -Y 로 변환해야 실제 방향과 일치한다.
        var screenFacing = playerMovement != null ? playerMovement.FacingDirection : Vector2Int.down;
        var gridFacing = SkillTargetResolver.ToGridAimDirection(screenFacing);

        return AttackPattern.GetTargets(pattern, origin, gridFacing, range, coneHalfAngle);
    }

    private SkillExecutionContext CreateSkillExecutionContext(SkillData skill, int slotIndex)
    {
        Vector2Int screenFacing = playerMovement != null ? playerMovement.FacingDirection : Vector2Int.down;
        Vector2Int gridFacing = SkillTargetResolver.ToGridAimDirection(screenFacing);
        Vector2 aimDirection = RefreshAimDirection();

        return new SkillExecutionContext(
            this,
            _dashController,
            transform,
            skill,
            slotIndex,
            aimDirection,
            gridFacing,
            TotalAttack,
            hitRadius);
    }

    // ══════════════════════════════════════════════════════════════
    //  피해 수신 (IDamageable)
    // ══════════════════════════════════════════════════════════════

    public void TakeDamage(int incomingDamage)
    {
        TryApplyDamage(incomingDamage);
    }

    public bool ApplyEnemyCombatImpact(
        int damage,
        Vector2 hitDirection,
        float knockbackForce,
        float knockbackDuration,
        float slowMultiplier,
        float slowDuration,
        float stunDuration)
    {
        if (!TryApplyDamage(damage))
            return false;

        ApplyEnemyKnockback(hitDirection, knockbackForce, knockbackDuration);
        ApplyEnemySlow(slowMultiplier, slowDuration);
        ApplyEnemyStun(stunDuration);
        return true;
    }

    private bool TryApplyDamage(int incomingDamage)
    {
        if (IsDead || !IsAlive) return false;
        if (IsDamageInvincible) return false;

        int actual = Mathf.Max(1, incomingDamage - TotalDefense);
        int hpBefore = CurrentHp;
        _resource.TakeDamage(actual);
        if (CurrentHp >= hpBefore)
            return false;

        _damageInvincibleTimer = damageInvincibleDuration;
        _hitFlash?.Play();
        combatChannel?.RaisePlayerHpChanged(CurrentHp, maxHp);
#if UNITY_EDITOR
        Debug.Log($"[Combat] Player -{actual} HP -> {CurrentHp}/{maxHp}");
#endif
        if (CurrentHp == 0)
            Die();

        return true;
    }

    private void ApplyEnemyKnockback(Vector2 hitDirection, float force, float duration)
    {
        if (playerMovement == null || force <= 0f || duration <= 0f)
            return;

        if (_enemyKnockbackRoutine != null)
            StopCoroutine(_enemyKnockbackRoutine);

        _enemyKnockbackRoutine = StartCoroutine(EnemyKnockbackRoutine(
            ResolveEnemyImpactDirection(hitDirection),
            force,
            duration));
    }

    private IEnumerator EnemyKnockbackRoutine(Vector2 direction, float distance, float duration)
    {
        float remaining = Mathf.Max(0.01f, duration);
        float speed = Mathf.Max(0f, distance) / remaining;

        while (remaining > 0f && !IsDead && playerMovement != null)
        {
            float deltaTime = Time.deltaTime;
            remaining -= deltaTime;
            playerMovement.TryApplyExternalDisplacement(direction * (speed * deltaTime));
            yield return null;
        }

        _enemyKnockbackRoutine = null;
    }

    private Vector2 ResolveEnemyImpactDirection(Vector2 hitDirection)
    {
        if (hitDirection.sqrMagnitude > 0.0001f)
            return hitDirection.normalized;

        Vector2 fallback = CurrentAimDirection;
        if (fallback.sqrMagnitude <= 0.0001f)
            fallback = Vector2.down;

        return -fallback.normalized;
    }

    private void ApplyEnemySlow(float multiplier, float duration)
    {
        if (duration <= 0f || multiplier <= 0f || multiplier >= 1f)
            return;

        _enemySlows.Add(new PlayerSlowEffect
        {
            Multiplier = Mathf.Clamp01(multiplier),
            Timer = duration
        });
        RecalculateEnemySlowMultiplier();
    }

    private void ApplyEnemyStun(float duration)
    {
        if (duration <= 0f)
            return;

        _stunTimer = Mathf.Max(_stunTimer, duration);
    }

    private void TickEnemyStun(float deltaTime)
    {
        if (_stunTimer <= 0f)
            return;

        _stunTimer = Mathf.Max(0f, _stunTimer - deltaTime);
    }

    private void TickEnemySlowEffects(float deltaTime)
    {
        if (_enemySlows.Count == 0)
            return;

        bool changed = false;
        for (int i = _enemySlows.Count - 1; i >= 0; i--)
        {
            PlayerSlowEffect effect = _enemySlows[i];
            effect.Timer -= deltaTime;
            if (effect.Timer <= 0f)
            {
                _enemySlows.RemoveAt(i);
                changed = true;
            }
            else
            {
                _enemySlows[i] = effect;
            }
        }

        if (changed)
            RecalculateEnemySlowMultiplier();
    }

    private void RecalculateEnemySlowMultiplier()
    {
        float multiplier = 1f;
        for (int i = 0; i < _enemySlows.Count; i++)
            multiplier = Mathf.Min(multiplier, _enemySlows[i].Multiplier);

        _enemySlowMultiplier = multiplier;
    }

    private void Die()
    {
        if (IsDead)
            return;

        IsDead = true;
        _damageInvincibleTimer = 0f;
        _externalInvincibilityCount = 0;
        ClearSkillTimingState();
        ClearEnemyImpactState();
        invincibilityFlashFeedback?.StopAndReset();
        OnDied?.Invoke(this);
        combatChannel?.RaisePlayerDied(this);
    }

    public void BeginExternalInvincibility()
    {
        if (_externalInvincibilityCount < int.MaxValue)
            _externalInvincibilityCount++;
    }

    public void BeginExternalInvincibility(float visualDuration)
    {
        BeginExternalInvincibility();
        if (visualDuration > 0f)
            invincibilityFlashFeedback?.Play(visualDuration);
    }

    public void EndExternalInvincibility()
    {
        if (_externalInvincibilityCount <= 0)
        {
            _externalInvincibilityCount = 0;
            return;
        }

        _externalInvincibilityCount--;
    }

    // ══════════════════════════════════════════════════════════════
    //  MP 관리
    // ══════════════════════════════════════════════════════════════

    private HitFlashFeedback ResolveHitFlashFeedback()
    {
        return GetComponentInChildren<HitFlashFeedback>(true);
    }

    private PlayerInvincibilityFlashFeedback ResolveInvincibilityFlashFeedback()
    {
        return GetComponentInChildren<PlayerInvincibilityFlashFeedback>(true);
    }

    private void SpendMp(int amount)
    {
        _resource.SpendMp(amount);
        combatChannel?.RaisePlayerMpChanged(CurrentMp, maxMp);
    }

    public void RestoreMp(int amount)
    {
        if (IsDead) return;

        _resource.RestoreMp(amount, maxMp);
        combatChannel?.RaisePlayerMpChanged(CurrentMp, maxMp);
    }

    public void RestoreHp(int amount)
    {
        if (IsDead) return;

        _resource.RestoreHp(amount, maxHp);
        combatChannel?.RaisePlayerHpChanged(CurrentHp, maxHp);
    }

    // ── 스킬 쿨다운 조회 (UI 표시용) ────────────────────────────────
    public float GetSkillCooldownRemaining(int slotIndex)
    {
        EnsureSkillSlotsBound();
        SkillSlotRuntime slot = GetSkillSlot(slotIndex);
        return slot != null && slot.CooldownRemaining > 0f ? slot.CooldownRemaining : 0f;
    }

    public float GetSkillCooldownMax(int slotIndex)
    {
        SkillData skill = GetSkillData(slotIndex);
        return skill != null ? skill.cooldown : 0f;
    }

    public float GetSkillCooldownNormalized(int slotIndex)
    {
        float max = GetSkillCooldownMax(slotIndex);
        return max > 0f ? GetSkillCooldownRemaining(slotIndex) / max : 0f;
    }

    public SkillData GetSkillData(int slotIndex)
    {
        EnsureSkillSlotsBound();
        return GetSkillSlot(slotIndex)?.Data;
    }

    public bool IsSkillReady(int slotIndex)
    {
        EnsureSkillSlotsBound();
        return GetSkillSlot(slotIndex)?.IsCooldownReady ?? false;
    }

    public bool CanUseSkill(int slotIndex)
    {
        EnsureSkillSlotsBound();
        return !IsDead && !IsSkillBusy && (GetSkillSlot(slotIndex)?.CanUse(CurrentMp) ?? false);
    }

    private static SkillSlotRuntime[] CreateSkillSlots()
    {
        var slots = new SkillSlotRuntime[SkillSlotCount];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = new SkillSlotRuntime();
        return slots;
    }

    private SkillSlotRuntime GetSkillSlot(int slotIndex)
    {
        return (uint)slotIndex < (uint)_skillSlots.Length ? _skillSlots[slotIndex] : null;
    }

    private void EnsureSkillSlotsBound()
    {
        if (_boundSkillWeapon != currentWeapon)
            BindSkillSlots(currentWeapon);
    }

    private void BindSkillSlots(WeaponData weapon)
    {
        _boundSkillWeapon = weapon;
        SkillData[] skills = weapon != null ? weapon.skills : null;

        for (int i = 0; i < _skillSlots.Length; i++)
        {
            SkillData skill = skills != null && i < skills.Length ? skills[i] : null;
            _skillSlots[i].Bind(skill);
        }
    }

    private void TickSkillSlots(float deltaTime)
    {
        for (int i = 0; i < _skillSlots.Length; i++)
            _skillSlots[i].TickCooldown(deltaTime);
    }
}
