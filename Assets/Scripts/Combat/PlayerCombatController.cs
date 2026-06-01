// ═══════════════════════════════════════════════════════════════════
//  PlayerCombatController.cs
//  Application Layer — 플레이어 전투 (공격·스킬·피해 수신)
//
//  책임:
//    • WeaponData 기반 기본 공격 (Space)
//    • SkillData 기반 스킬 사용 (1~4)
//    • HP / skill resource 관리 및 이벤트 발행
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
public class PlayerCombatController : MonoBehaviour, IDamageable, ISkillResourceLedger
{
    private const int SkillSlotCount = 4;
    private const float DefaultPlayerHitRadius = 0.5f;

    public static PlayerCombatController Active { get; private set; }

    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("Dependencies")]
    public CombatEventChannel combatChannel;
    [SerializeField] private DungeonEventChannel dungeonChannel;
    public PlayerController  playerMovement;

    [Header("기본 스탯")]
    [SerializeField] private int maxHp      = 20;
    [SerializeField] private int baseAttack  = 3;
    [SerializeField] private int baseDefense = 1;

    [Header("무기 (런타임에 EquipWeapon()으로 교체 가능)")]
    public WeaponData currentWeapon;

    [Header("Animation")]
    [SerializeField] private SkillData basicAttackSkillData;

    [Header("피해 감지")]
    [Tooltip("공격 판정 반경 (월드 단위). 타일 크기의 약 40% 권장.")]
    [SerializeField] private float hitRadius = 0.3f;

    [Header("Damage Invincibility")]
    [SerializeField, Min(0f)] private float damageInvincibleDuration = 0.5f;

    [Header("Skill Resources")]
    [SerializeField, Min(0)] private int maxParryStack = 4;
    [SerializeField, Min(0f)] private float parryStackGraceDuration = 3f;
    [SerializeField, Min(0.01f)] private float parryStackDecayInterval = 1f;
    [SerializeField, Min(1)] private int parryStackDecayAmount = 1;

    [Header("Parry Basic Attack")]
    [SerializeField, Min(0f)] private float parryStartupDelay = 0.08f;
    [SerializeField, Min(0f)] private float parryInvincibleDuration = 0.2f;
    [SerializeField, Min(0f)] private float parryRecoveryDelay = 0.18f;
    [SerializeField] private bool blockMovementDuringParry = true;

    // ── 런타임 상태 ─────────────────────────────────────────────────

    private readonly PlayerResource _resource = new();
    private readonly SkillCooldownController _cooldownController = new();
    private readonly SkillSlotRuntime[] _skillSlots = CreateSkillSlots();
    private readonly DaggerMarkerRegistry _daggerMarkers = DaggerMarkerRegistry.Instance;
    private AttackExecutor _attackExecutor;
    private SkillExecutor _skillExecutor;
    private readonly List<Vector3> _basicAttackWorldTargets = new();
    private PlayerInputReader _inputReader;
    private PlayerDashController _dashController;
    private PlayerFormController _formController;
    private HitFlashFeedback _hitFlash;
    [SerializeField] private PlayerInvincibilityFlashFeedback invincibilityFlashFeedback;
    private WeaponData _boundSkillWeapon;
    private float _damageInvincibleTimer;
    private int _externalInvincibilityCount;
    private int _currentBullet;
    private int _parryStack;
    private float _parryStackGraceTimer;
    private float _parryStackDecayTimer;
    private Transform _cachedTransform;
    private Collider2D _cachedHitCollider;
    private float _cachedHitRadius = DefaultPlayerHitRadius;
    private Vector2Int _lastAimDirection = Vector2Int.down;
    private bool _isSkillCasting;
    private float _skillRecoveryTimer;
    private Coroutine _skillCastRoutine;
    private Coroutine _parryRoutine;
    private Coroutine _reloadRoutine;
    private Coroutine _enemyKnockbackRoutine;
    private readonly List<PlayerSlowEffect> _enemySlows = new();
    private float _enemySlowMultiplier = 1f;
    private float _stunTimer;
    private float _slowTotalDurationForUi;
    private float _stunTotalDurationForUi;
    private bool _isParrySequenceActive;
    private bool _isParryStartupActive;
    private bool _isParryInvincibleWindowActive;
    private bool _parryIntercepted;
    private bool _parryCancelled;
    private bool _isReloading;
    private bool _isDungeonChannelSubscribed;
    private int maxBullet;
    private SkillData _activeDaggerDashSkill;
    private int _activeDaggerDashSlotIndex = -1;
    private bool _daggerDashCooldownResetThisDash;
    private int _pendingDaggerCooldownResetSlot = -1;
    private float _daggerBasicAttackMarkerBuffTimer;
    private float _daggerBasicAttackMarkerDuration;
    private Action<EnemyController> _daggerDashEnemyHitCallback;
    private Action<EnemyController, ProjectileController> _daggerProjectileEnemyHitCallback;

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
    public int CurrentBullet => _currentBullet;
    public int MaxBullet => maxBullet;
    public int CurrentParryStack => _parryStack;
    public int MaxParryStack => maxParryStack;
    public bool IsReloading => _isReloading;
    public PlayerBasicAttackMode CurrentBasicAttackMode => GetCurrentBasicAttackMode();
    public bool IsDamageInvincible => _damageInvincibleTimer > 0f || HasExternalInvincibility;
    public bool HasExternalInvincibility => _externalInvincibilityCount > 0;
    public bool IsDashing => _dashController != null && _dashController.IsDashing;
    public bool IsParryBusy => _isParrySequenceActive;
    public bool IsSkillBusy => _isSkillCasting || _skillRecoveryTimer > 0f || _isParrySequenceActive || _isReloading;
    public bool IsSlowed => _enemySlows.Count > 0;
    public float SlowRemainingTime => GetSlowRemainingTime();
    public float SlowTotalDurationForUi => _slowTotalDurationForUi;
    public float SlowRemainingRatio =>
        _slowTotalDurationForUi > 0f ? Mathf.Clamp01(SlowRemainingTime / _slowTotalDurationForUi) : 0f;
    public bool IsStunned => _stunTimer > 0f;
    public float StunRemainingTime => _stunTimer;
    public float StunTotalDurationForUi => _stunTotalDurationForUi;
    public float StunRemainingRatio =>
        _stunTotalDurationForUi > 0f ? Mathf.Clamp01(_stunTimer / _stunTotalDurationForUi) : 0f;
    public bool BlocksPlayerMovement => _isSkillCasting || _skillRecoveryTimer > 0f || (blockMovementDuringParry && _isParrySequenceActive);
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
    public event Action<PlayerStatusEffectType> OnStatusEffectApplied;
    public event Action<PlayerStatusEffectType> OnStatusEffectEnded;

    // ══════════════════════════════════════════════════════════════
    //  초기화
    // ══════════════════════════════════════════════════════════════

    private void Awake()
    {
        _resource.Initialize(maxHp);
        CachePlayerHitInfo();
        RegisterAsActive();
        _attackExecutor = new AttackExecutor(transform, this, CombatLayers.EnemyFilter);
        _skillExecutor = new SkillExecutor(_attackExecutor);
        _daggerDashEnemyHitCallback = HandleDaggerDashEnemyHit;
        _daggerProjectileEnemyHitCallback = HandleDaggerProjectileEnemyHit;
        BindSkillSlots(currentWeapon);
        _inputReader = GetComponent<PlayerInputReader>();
        _dashController = GetComponent<PlayerDashController>();
        _formController = GetComponent<PlayerFormController>();
        _hitFlash = ResolveHitFlashFeedback();
        if (invincibilityFlashFeedback == null)
            invincibilityFlashFeedback = ResolveInvincibilityFlashFeedback();
        SubscribeDungeonChannel();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (combatChannel == null)
            Debug.LogWarning("[PlayerCombatController] CombatEventChannel 없음 — HP/스킬 UI 이벤트가 발행되지 않습니다.", this);
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
        UnsubscribeDungeonChannel();
        if (ReferenceEquals(Active, this))
            Active = null;
    }

    private void OnDisable()
    {
        ClearSkillTimingState();
        ClearParryState();
        ClearReloadState();
        ClearEnemyImpactState();
        ClearDaggerRuntimeState();
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
        ClearReloadState();
        currentWeapon   = weapon;
        ApplyWeaponMagazine(weapon);
        _cooldownController.ResetAll();
        BindSkillSlots(weapon);
#if UNITY_EDITOR
        Debug.Log($"[Combat] 무기 장착: {weapon?.weaponName ?? "없음"}");
#endif
    }

    // ══════════════════════════════════════════════════════════════
    //  매 프레임 처리
    // ══════════════════════════════════════════════════════════════

    private void ApplyWeaponMagazine(WeaponData weapon)
    {
        if (weapon != null && weapon.usesMagazine)
        {
            maxBullet = Mathf.Max(0, weapon.magazineSize);
            _currentBullet = maxBullet;
            return;
        }

        maxBullet = 0;
        _currentBullet = 0;
    }

    private void Update()
    {
        if (IsDead)
            return;

        if (_damageInvincibleTimer > 0f)
            _damageInvincibleTimer -= Time.deltaTime;

        TickEnemyStun(Time.deltaTime);
        TickEnemySlowEffects(Time.deltaTime);
        TickSkillResources(Time.deltaTime);
        EnsureSkillSlotsBound();
        _cooldownController.Tick(Time.deltaTime);
        TickSkillSlots(Time.deltaTime);
        TickSkillRecovery(Time.deltaTime);
        TickDaggerState(Time.deltaTime);

        if (_inputReader == null) return;

        if (DungeonManager.Instance != null && DungeonManager.Instance.IsTransitioning) return;
        if (IsCombatBlockedByLocation()) return;
        if (IsDashing) return;
        if (IsStunned) return;

        RefreshAimDirection();

        if (_inputReader.WasReloadPressed && IsCurrentFormBulletMode())
            TryStartReload();
        if (IsSkillBusy) return;

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

        if (IsCurrentFormParryMode())
        {
            _cooldownController.SetAttackCooldown(currentWeapon.attackCooldown);
            BeginParryBasicAttack();
            return;
        }

        if (IsCurrentFormBulletMode())
        {
            TryBulletBasicAttack();
            return;
        }

        _cooldownController.SetAttackCooldown(currentWeapon.attackCooldown);

        _attackExecutor.BeginAttackActivation();
        if (basicAttackSkillData != null)
            _formController?.PlaySkillAnimation(basicAttackSkillData, CurrentAimDirection);

        SkillTargetResolver.FillWorldTargets(
            currentWeapon.attackPattern,
            transform.position,
            CurrentAimDirection,
            GetGridAimDirection(),
            currentWeapon.patternRange,
            45f,
            _basicAttackWorldTargets);

        _attackExecutor.ExecuteAttackWorld(
            _basicAttackWorldTargets,
            TotalAttack + currentWeapon.damage,
            currentWeapon.canPenetrateWalls,
            currentWeapon.basicAttackMultiTarget,
            currentWeapon.knockbackForce,
            currentWeapon.knockbackDuration,
            currentWeapon.slowPercentage,
            currentWeapon.slowDuration,
            hitRadius);

        ApplyDaggerMarkersFromBasicAttack();
    }

    private bool IsCurrentFormParryMode()
    {
        return GetCurrentBasicAttackMode() == PlayerBasicAttackMode.Parry;
    }

    private bool IsCurrentFormBulletMode()
    {
        return GetCurrentBasicAttackMode() == PlayerBasicAttackMode.Bullet;
    }

    private PlayerBasicAttackMode GetCurrentBasicAttackMode()
    {
        PlayerFormData form = _formController != null ? _formController.CurrentForm : null;
        return form != null ? form.BasicAttackMode : PlayerBasicAttackMode.Damage;
    }

    private void TryBulletBasicAttack()
    {
        if (!CanUseMagazine())
            return;

        if (_currentBullet <= 0)
        {
            TryStartReload();
            return;
        }

        if (basicAttackSkillData == null || basicAttackSkillData.executionType != SkillExecutionType.Projectile)
            return;

        SkillExecutionContext context = CreateSkillExecutionContext(basicAttackSkillData, -1);
        if (!_skillExecutor.ExecuteBasicProjectile(context, 1))
            return;

        _cooldownController.SetAttackCooldown(currentWeapon.attackCooldown);
        Spend(SkillResourceType.Bullet, 1);
        TryStartAutoReloadIfEmpty();
    }

    private void BeginParryBasicAttack()
    {
        if (_parryRoutine != null)
            StopCoroutine(_parryRoutine);

        if (basicAttackSkillData != null)
            _formController?.PlaySkillAnimation(basicAttackSkillData, CurrentAimDirection);

        _parryRoutine = StartCoroutine(ParryBasicAttackRoutine());
    }

    private IEnumerator ParryBasicAttackRoutine()
    {
        _isParrySequenceActive = true;
        _isParryStartupActive = false;
        _isParryInvincibleWindowActive = false;
        _parryIntercepted = false;
        _parryCancelled = false;

        float startup = Mathf.Max(0f, parryStartupDelay);
        _isParryStartupActive = startup > 0f;
        while (startup > 0f && !_parryCancelled)
        {
            if (IsDead || !isActiveAndEnabled)
            {
                ClearParryState();
                yield break;
            }

            startup -= Time.deltaTime;
            yield return null;
        }

        _isParryStartupActive = false;
        if (_parryCancelled)
        {
            ClearParryState();
            yield break;
        }

        _isParryInvincibleWindowActive = true;
        float active = Mathf.Max(0f, parryInvincibleDuration);
        invincibilityFlashFeedback?.Play(active);
        while (active > 0f && !_parryIntercepted)
        {
            if (IsDead || !isActiveAndEnabled)
            {
                ClearParryState();
                yield break;
            }

            active -= Time.deltaTime;
            yield return null;
        }

        _isParryInvincibleWindowActive = false;
        invincibilityFlashFeedback?.StopAndReset();

        float recovery = Mathf.Max(0f, parryRecoveryDelay);
        while (recovery > 0f)
        {
            if (IsDead || !isActiveAndEnabled)
            {
                ClearParryState();
                yield break;
            }

            recovery -= Time.deltaTime;
            yield return null;
        }

        ClearParryState();
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
        if (!slot.CanUse(this)) return;

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
        if (IsCombatBlockedByLocation()) return false;

        EnsureSkillSlotsBound();
        SkillSlotRuntime slot = GetSkillSlot(slotIndex);
        if (slot == null) return false;
        if (!ReferenceEquals(slot.Data, expectedSkill)) return false;
        if (!slot.CanUse(this)) return false;

        SkillData skill = slot.Data;
        SkillExecutionContext context = CreateSkillExecutionContext(skill, slotIndex);
        SkillExecutionResult result = _skillExecutor.Execute(context);
        if (!result.Success) return false;

        Spend(skill.resourceType, result.ResourceConsumed);
        ApplySkillReload(skill);
        TryStartAutoReloadIfEmpty();
        slot.StartCooldown();
        ConsumePendingDaggerCooldownReset(slotIndex);
        StartSkillRecovery(skill.recoveryDelay);
        combatChannel?.RaiseSkillUsed(skill);
#if UNITY_EDITOR
        Debug.Log($"[Combat] Skill [{slotIndex + 1}] {skill.skillName} used");
#endif

        return true;
    }

    public void ResetSkillCooldown(int slotIndex)
    {
        EnsureSkillSlotsBound();
        GetSkillSlot(slotIndex)?.ResetRuntimeState();
    }

    public Action<EnemyController> PrepareDaggerDashHitCallback(SkillData skill, int slotIndex)
    {
        _activeDaggerDashSkill = skill;
        _activeDaggerDashSlotIndex = slotIndex;
        _daggerDashCooldownResetThisDash = false;
        return _daggerDashEnemyHitCallback;
    }

    public Action<EnemyController, ProjectileController> DaggerProjectileEnemyHitCallback => _daggerProjectileEnemyHitCallback;

    public void BeginDaggerBasicAttackMarkerBuff(SkillData skill)
    {
        if (skill == null)
            return;

        float duration = skill.markerDuration > 0f ? skill.markerDuration : 5f;
        _daggerBasicAttackMarkerBuffTimer = duration;
        _daggerBasicAttackMarkerDuration = duration;
    }

    private void HandleDaggerProjectileEnemyHit(EnemyController enemy, ProjectileController projectile)
    {
        if (enemy == null || projectile == null)
            return;

        _daggerMarkers.Apply(enemy, projectile.DaggerMarkerDuration);
    }

    private void HandleDaggerDashEnemyHit(EnemyController enemy)
    {
        SkillData skill = _activeDaggerDashSkill;
        if (enemy == null || skill == null || !skill.detonatesDaggerMarker)
            return;

        if (!_daggerMarkers.Detonate(enemy))
            return;

        int detonationDamage = skill.markerDetonationDamage > 0
            ? skill.markerDetonationDamage
            : skill.damage;
        if (detonationDamage > 0 && enemy.IsAlive)
        {
            enemy.ApplyCombatImpact(
                detonationDamage,
                transform.position,
                0f,
                0f,
                0f,
                0f);
        }

        if (skill.resetCooldownOnMarkerDetonate && !_daggerDashCooldownResetThisDash)
        {
            if (GetSkillCooldownRemaining(_activeDaggerDashSlotIndex) > 0f)
                ResetSkillCooldown(_activeDaggerDashSlotIndex);
            else
                _pendingDaggerCooldownResetSlot = _activeDaggerDashSlotIndex;
            _daggerDashCooldownResetThisDash = true;
        }
    }

    private void ApplyDaggerMarkersFromBasicAttack()
    {
        if (_daggerBasicAttackMarkerBuffTimer <= 0f)
            return;

        for (int i = 0; i < _attackExecutor.HitEnemyCount; i++)
        {
            EnemyController enemy = _attackExecutor.GetHitEnemy(i);
            if (enemy != null && enemy.IsAlive)
                _daggerMarkers.Apply(enemy, _daggerBasicAttackMarkerDuration);
        }
    }

    private void TickDaggerState(float deltaTime)
    {
        _daggerMarkers.Tick(deltaTime);
        if (_daggerBasicAttackMarkerBuffTimer > 0f)
            _daggerBasicAttackMarkerBuffTimer = Mathf.Max(0f, _daggerBasicAttackMarkerBuffTimer - deltaTime);
    }

    private void ConsumePendingDaggerCooldownReset(int slotIndex)
    {
        if (_pendingDaggerCooldownResetSlot != slotIndex)
            return;

        ResetSkillCooldown(slotIndex);
        _pendingDaggerCooldownResetSlot = -1;
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

    private bool CanUseMagazine()
    {
        return currentWeapon != null && currentWeapon.usesMagazine && maxBullet > 0;
    }

    private bool TryStartReload()
    {
        if (!CanUseMagazine())
            return false;
        if (_isSkillCasting || _skillRecoveryTimer > 0f || _isParrySequenceActive)
            return false;
        if (_isReloading)
            return false;
        if (_currentBullet >= maxBullet)
            return false;

        _reloadRoutine = StartCoroutine(ReloadRoutine());
        return true;
    }

    private IEnumerator ReloadRoutine()
    {
        _isReloading = true;
        float remaining = currentWeapon != null ? Mathf.Max(0f, currentWeapon.reloadTime) : 0f;
        while (remaining > 0f)
        {
            if (IsDead || !isActiveAndEnabled || !CanUseMagazine())
            {
                FinishReloadState();
                yield break;
            }

            remaining -= Time.deltaTime;
            yield return null;
        }

        int amount = ResolveReloadAmount();
        _currentBullet = Mathf.Min(maxBullet, _currentBullet + amount);
        FinishReloadState();
    }

    private int ResolveReloadAmount()
    {
        if (currentWeapon == null)
            return maxBullet;

        return currentWeapon.reloadAmount > 0 ? currentWeapon.reloadAmount : maxBullet;
    }

    private void TryStartAutoReloadIfEmpty()
    {
        if (_currentBullet <= 0)
            TryStartReload();
    }

    private void ApplySkillReload(SkillData skill)
    {
        if (skill != null && skill.reloadAmount > 0)
            RestoreSkillResource(SkillResourceType.Bullet, skill.reloadAmount);
    }

    private void ClearReloadState()
    {
        if (_reloadRoutine != null)
        {
            StopCoroutine(_reloadRoutine);
            _reloadRoutine = null;
        }

        _isReloading = false;
    }

    private void FinishReloadState()
    {
        _reloadRoutine = null;
        _isReloading = false;
    }

    private void ClearParryState()
    {
        if (_parryRoutine != null)
        {
            StopCoroutine(_parryRoutine);
            _parryRoutine = null;
        }

        _isParrySequenceActive = false;
        _isParryStartupActive = false;
        _isParryInvincibleWindowActive = false;
        _parryIntercepted = false;
        _parryCancelled = false;
        invincibilityFlashFeedback?.StopAndReset();
    }

    private void ClearEnemyImpactState()
    {
        bool wasSlowed = IsSlowed;
        bool wasStunned = IsStunned;

        if (_enemyKnockbackRoutine != null)
        {
            StopCoroutine(_enemyKnockbackRoutine);
            _enemyKnockbackRoutine = null;
        }

        _enemySlows.Clear();
        _enemySlowMultiplier = 1f;
        _slowTotalDurationForUi = 0f;
        _stunTimer = 0f;
        _stunTotalDurationForUi = 0f;

        if (wasSlowed)
            OnStatusEffectEnded?.Invoke(PlayerStatusEffectType.Slow);
        if (wasStunned)
            OnStatusEffectEnded?.Invoke(PlayerStatusEffectType.Stun);
    }

    private void ClearDaggerRuntimeState()
    {
        _activeDaggerDashSkill = null;
        _activeDaggerDashSlotIndex = -1;
        _daggerDashCooldownResetThisDash = false;
        _pendingDaggerCooldownResetSlot = -1;
        _daggerBasicAttackMarkerBuffTimer = 0f;
        _daggerBasicAttackMarkerDuration = 0f;
    }

    private SkillExecutionContext CreateSkillExecutionContext(SkillData skill, int slotIndex)
    {
        Vector2Int gridFacing = GetGridAimDirection();
        Vector2 aimDirection = RefreshAimDirection();

        return new SkillExecutionContext(
            this,
            _dashController,
            _formController,
            transform,
            skill,
            slotIndex,
            aimDirection,
            gridFacing,
            TotalAttack,
            hitRadius);
    }

    private Vector2Int GetGridAimDirection()
    {
        Vector2Int screenFacing = playerMovement != null ? playerMovement.FacingDirection : Vector2Int.down;
        return SkillTargetResolver.ToGridAimDirection(screenFacing);
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
        if (IsDead)
            return true;

        ApplyEnemyKnockback(hitDirection, knockbackForce, knockbackDuration);
        ApplyEnemySlow(slowMultiplier, slowDuration);
        ApplyEnemyStun(stunDuration);
        return true;
    }

    private bool TryApplyDamage(int incomingDamage)
    {
        if (IsDead || !IsAlive) return false;
        if (_isParryInvincibleWindowActive)
        {
            CompleteParryIntercept();
            return false;
        }
        if (IsDamageInvincible) return false;

        int actual = Mathf.Max(1, incomingDamage - TotalDefense);
        int hpBefore = CurrentHp;
        _resource.TakeDamage(actual);
        if (CurrentHp >= hpBefore)
            return false;

        if (_isParryStartupActive)
            _parryCancelled = true;

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

    private void CompleteParryIntercept()
    {
        _parryIntercepted = true;
        _isParryInvincibleWindowActive = false;
        AddParryStack(1);
        invincibilityFlashFeedback?.StopAndReset();
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

        _slowTotalDurationForUi = Mathf.Max(_slowTotalDurationForUi, duration);
        _enemySlows.Add(new PlayerSlowEffect
        {
            Multiplier = Mathf.Clamp01(multiplier),
            Timer = duration
        });
        RecalculateEnemySlowMultiplier();
        OnStatusEffectApplied?.Invoke(PlayerStatusEffectType.Slow);
    }

    private void ApplyEnemyStun(float duration)
    {
        if (duration <= 0f)
            return;

        _stunTimer = Mathf.Max(_stunTimer, duration);
        _stunTotalDurationForUi = Mathf.Max(_stunTotalDurationForUi, duration);
        OnStatusEffectApplied?.Invoke(PlayerStatusEffectType.Stun);
    }

    private void TickEnemyStun(float deltaTime)
    {
        if (_stunTimer <= 0f)
            return;

        _stunTimer = Mathf.Max(0f, _stunTimer - deltaTime);
        if (_stunTimer <= 0f)
        {
            _stunTotalDurationForUi = 0f;
            OnStatusEffectEnded?.Invoke(PlayerStatusEffectType.Stun);
        }
    }

    private void TickEnemySlowEffects(float deltaTime)
    {
        if (_enemySlows.Count == 0)
            return;

        bool changed = false;
        bool wasSlowed = _enemySlows.Count > 0;
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

        if (wasSlowed && _enemySlows.Count == 0)
        {
            _slowTotalDurationForUi = 0f;
            OnStatusEffectEnded?.Invoke(PlayerStatusEffectType.Slow);
        }
    }

    private void RecalculateEnemySlowMultiplier()
    {
        float multiplier = 1f;
        for (int i = 0; i < _enemySlows.Count; i++)
            multiplier = Mathf.Min(multiplier, _enemySlows[i].Multiplier);

        _enemySlowMultiplier = multiplier;
    }

    private float GetSlowRemainingTime()
    {
        float remaining = 0f;
        for (int i = 0; i < _enemySlows.Count; i++)
            remaining = Mathf.Max(remaining, _enemySlows[i].Timer);

        return remaining;
    }

    private void Die()
    {
        if (IsDead)
            return;

        IsDead = true;
        _damageInvincibleTimer = 0f;
        _externalInvincibilityCount = 0;
        ClearSkillTimingState();
        ClearParryState();
        ClearReloadState();
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
    //  Skill resource 관리
    // ══════════════════════════════════════════════════════════════

    private HitFlashFeedback ResolveHitFlashFeedback()
    {
        return GetComponentInChildren<HitFlashFeedback>(true);
    }

    private PlayerInvincibilityFlashFeedback ResolveInvincibilityFlashFeedback()
    {
        return GetComponentInChildren<PlayerInvincibilityFlashFeedback>(true);
    }

    public void RestoreHp(int amount)
    {
        if (IsDead) return;

        _resource.RestoreHp(amount, maxHp);
        combatChannel?.RaisePlayerHpChanged(CurrentHp, maxHp);
    }

    private void TickSkillResources(float deltaTime)
    {
        if (_parryStack <= 0)
            return;

        if (_parryStackGraceTimer > 0f)
        {
            _parryStackGraceTimer = Mathf.Max(0f, _parryStackGraceTimer - deltaTime);
            if (_parryStackGraceTimer > 0f)
                return;
        }

        _parryStackDecayTimer -= deltaTime;
        if (_parryStackDecayTimer > 0f)
            return;

        int decay = Mathf.Max(1, parryStackDecayAmount);
        _parryStack = Mathf.Max(0, _parryStack - decay);
        _parryStackDecayTimer = Mathf.Max(0.01f, parryStackDecayInterval);
        if (_parryStack == 0)
            ResetParryStackTimers();
    }

    private void AddParryStack(int amount)
    {
        if (amount <= 0)
            return;

        _parryStack = Mathf.Min(maxParryStack, _parryStack + amount);
        if (_parryStack > 0)
        {
            _parryStackGraceTimer = Mathf.Max(0f, parryStackGraceDuration);
            _parryStackDecayTimer = Mathf.Max(0.01f, parryStackDecayInterval);
        }
    }

    private void ResetParryStack()
    {
        _parryStack = 0;
        ResetParryStackTimers();
    }

    private void ResetParryStackTimers()
    {
        _parryStackGraceTimer = 0f;
        _parryStackDecayTimer = 0f;
    }

    public bool Has(SkillResourceType type, int requiredAmount)
    {
        if (type == SkillResourceType.None)
            return true;

        int required = Mathf.Max(0, requiredAmount);
        return GetAmount(type) >= required;
    }

    public bool Spend(SkillResourceType type, int consumeAmount)
    {
        if (type == SkillResourceType.None)
            return true;

        int amount = Mathf.Max(0, consumeAmount);
        if (amount == 0)
            return true;

        if (!Has(type, amount))
            return false;

        switch (type)
        {
            case SkillResourceType.Bullet:
                _currentBullet = Mathf.Max(0, _currentBullet - amount);
                return true;

            case SkillResourceType.ParryStack:
                _parryStack = Mathf.Max(0, _parryStack - amount);
                if (_parryStack == 0)
                    ResetParryStackTimers();
                return true;

            case SkillResourceType.None:
            default:
                return true;
        }
    }

    public int GetAmount(SkillResourceType type)
    {
        switch (type)
        {
            case SkillResourceType.Bullet:
                return _currentBullet;

            case SkillResourceType.ParryStack:
                return _parryStack;

            case SkillResourceType.None:
            default:
                return 0;
        }
    }

    public void RestoreSkillResource(SkillResourceType type, int amount)
    {
        if (IsDead || amount <= 0)
            return;

        switch (type)
        {
            case SkillResourceType.Bullet:
                _currentBullet = Mathf.Min(maxBullet, _currentBullet + amount);
                break;

            case SkillResourceType.ParryStack:
                AddParryStack(amount);
                break;
        }
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
        return !IsDead && !IsSkillBusy && !IsCombatBlockedByLocation() && (GetSkillSlot(slotIndex)?.CanUse(this) ?? false);
    }

    private void SubscribeDungeonChannel()
    {
        if (_isDungeonChannelSubscribed)
            return;

        DungeonEventChannel channel = dungeonChannel;
        if (channel == null && DungeonManager.Instance != null)
            channel = DungeonManager.Instance.eventChannel;
        if (channel == null)
            return;

        dungeonChannel = channel;
        dungeonChannel.OnRoomEntered += HandleRoomEntered;
        dungeonChannel.OnRoomDoorsOpened += HandleRoomDoorsOpened;
        dungeonChannel.OnFloorChanged += HandleFloorChanged;
        _isDungeonChannelSubscribed = true;
    }

    private void UnsubscribeDungeonChannel()
    {
        if (!_isDungeonChannelSubscribed || dungeonChannel == null)
            return;

        dungeonChannel.OnRoomEntered -= HandleRoomEntered;
        dungeonChannel.OnRoomDoorsOpened -= HandleRoomDoorsOpened;
        dungeonChannel.OnFloorChanged -= HandleFloorChanged;
        _isDungeonChannelSubscribed = false;
    }

    private void HandleRoomEntered(RoomEnteredEventArgs args)
    {
        ResetParryStack();
    }

    private void HandleRoomDoorsOpened(RoomInfo room)
    {
        ResetParryStack();
    }

    private void HandleFloorChanged(int previousFloor, int newFloor)
    {
        ResetParryStack();
    }

    private static bool IsCombatBlockedByLocation()
    {
        LocationTransitionManager locationManager = LocationTransitionManager.Active;
        return locationManager != null && locationManager.ShouldBlockCombat;
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
