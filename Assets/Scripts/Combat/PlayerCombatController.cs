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
    private const float MouseAimEpsilonSqr = 0.0001f;

    private struct RecastChainEntry
    {
        public SkillData RootSkill;
        public int StageIndex;
        public float WindowTimer;
        public float RecoveryHold;
        public float RecoveryTotal;
    }

    public static PlayerCombatController Active { get; private set; }

    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("Dependencies")]
    public CombatEventChannel combatChannel;
    [SerializeField] private DungeonEventChannel dungeonChannel;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerSoulEnhancements soulEnhancements;
    [SerializeField] private EngravingLoadout engravingLoadout;
    [SerializeField] private SoulEnhancementTable soulEnhancementTable;
    [SerializeField] private ComboTierConfig comboTierConfig;
    public PlayerController  playerMovement;

    [Header("기본 스탯")]
    [SerializeField] private int maxHp      = 20;
    [SerializeField] private int baseAttack  = 3;
    [SerializeField] private int baseDefense = 1;

    [Header("무기 (런타임에 EquipWeapon()으로 교체 가능)")]
    public WeaponData currentWeapon;

    [Header("Animation")]
    [SerializeField] private SkillData basicAttackSkillData;

    [Header("Dodge")]
    [SerializeField] private SkillData dodgeSkill;

    [Header("Skill Hit Flash")]
    [SerializeField] private bool enableSkillHitFlash = true;
    [SerializeField] private Color skillHitFlashColor = new Color(0.9f, 0.1f, 0.1f, 0.55f);
    [SerializeField, Min(0f)] private float skillHitFlashDuration = 0.2f;

    [Header("피해 감지")]
    [Tooltip("공격 판정 반경 (월드 단위). 타일 크기의 약 40% 권장.")]
    [SerializeField] private float hitRadius = 0.3f;

    [Header("Damage Invincibility")]
    [SerializeField, Min(0f)] private float damageInvincibleDuration = 0.5f;
    [SerializeField] private Color shieldFlashColor = new Color(1f, 0.82f, 0.2f, 1f);

    [Header("Critical")]
    [SerializeField, Min(1f)] private float critDamageMultiplier = 2f;

    [Header("Skill Resources")]
    [SerializeField, Min(0)] private int maxParryStack = 4;
    [SerializeField, Min(0f)] private float parryStackGraceDuration = 3f;
    [SerializeField, Min(0.01f)] private float parryStackDecayInterval = 1f;
    [SerializeField, Min(1)] private int parryStackDecayAmount = 1;

    [Header("Combo")]
    // Legacy fields retained only to preserve existing serialized paths. ComboTierConfig drives runtime values.
#pragma warning disable 0414
    [SerializeField, HideInInspector] private float comboWindow = 2f;
    [SerializeField, HideInInspector] private int comboMaxStack = 20;
#pragma warning restore 0414

    [Header("Parry Basic Attack")]
    [SerializeField, Min(0f)] private float parryStartupDelay = 0.08f;
    [SerializeField, Min(0f)] private float parryInvincibleDuration = 0.2f;
    [SerializeField, Min(0f)] private float parryRecoveryDelay = 0.18f;
    [SerializeField] private bool blockMovementDuringParry = true;

    // ── 런타임 상태 ─────────────────────────────────────────────────

    private readonly PlayerResource _resource = new();
    private readonly PlayerShield _shield = new();
    private readonly PlayerAttackBuff _attackBuff = new();
    private readonly PlayerItemStats _itemStats = new();
    private readonly SoulStatBonus _soulBonus = new SoulStatBonus();
    private readonly SkillCooldownController _cooldownController = new();
    private readonly SkillSlotRuntime[] _skillSlots = CreateSkillSlots();
    private readonly DaggerMarkerRegistry _daggerMarkers = DaggerMarkerRegistry.Instance;
    private AttackExecutor _attackExecutor;
    private SkillExecutor _skillExecutor;
    private SkillHitFlashRenderer _skillHitFlashRenderer;
    private readonly List<Vector3> _basicAttackWorldTargets = new();
    private PlayerInputReader _inputReader;
    private PlayerDashController _dashController;
    private PlayerFormController _formController;
    private PlayerBehaviors _relicBehaviors;
    private HitFlashFeedback _hitFlash;
    [SerializeField] private PlayerInvincibilityFlashFeedback invincibilityFlashFeedback;
    private WeaponData _boundSkillWeapon;
    private float _damageInvincibleTimer;
    private int _externalInvincibilityCount;
    private int _currentBullet;
    private ParryStackResource _parryStack;
    private ComboMeter _combo;
    private PlayerFormId _previousFormId;
    private Transform _cachedTransform;
    private Collider2D _cachedHitCollider;
    private float _cachedHitRadius = DefaultPlayerHitRadius;
    private Vector2Int _lastAimDirection = Vector2Int.down;
    private Vector2 _aimDirectionContinuous = Vector2.down;
    private bool _isSkillCasting;
    private float _skillRecoveryTimer;
    private SkillData _recoveryCancelableSkill;
    private float _dodgeCooldownTimer;
    private Coroutine _skillCastRoutine;
    private readonly RecastChainEntry[] _recastChains = new RecastChainEntry[SkillSlotCount];
    private Coroutine _parryRoutine;
    private Coroutine _reloadRoutine;
    private PlayerStatusEffects _status;
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
    private float _lifestealPool;
    private float _lifestealShieldPool;
    private Action<EnemyController> _daggerDashEnemyHitCallback;
    private Action<EnemyController, ProjectileController> _daggerProjectileEnemyHitCallback;
    private bool _isInventorySubscribed;
    private bool _isSoulEnhancementsSubscribed;
    private readonly HashSet<SkillExecutionType> _warnedRejectedProcTypes = new HashSet<SkillExecutionType>();

    // ── 공개 프로퍼티 ────────────────────────────────────────────────

    public bool IsAlive     => _resource.IsAlive && !IsDead;
    public bool IsDead { get; private set; }
    public int  CurrentHp   => _resource.CurrentHp;
    public int  MaxHp       => Mathf.Max(1, maxHp + _itemStats.MaxHpBonus);
    public int CurrentShield => _shield.CurrentAmount;
    public int CurrentAttackBuff => _attackBuff.CurrentAmount;
    public int CurrentBullet => _currentBullet;
    public int MaxBullet => maxBullet;
    public int CurrentParryStack => _parryStack != null ? _parryStack.Current : 0;
    public int MaxParryStack => maxParryStack;
    public int CurrentComboStack => _combo != null ? _combo.TotalStacks : 0;
    public int CurrentComboTier => _combo != null ? _combo.Tier : 0;
    public int CurrentComboProgress => _combo != null ? _combo.Progress : 0;
    public int MaxComboTier => comboTierConfig != null ? comboTierConfig.MaxTier : 0;
    public float ComboWindowRemaining => _combo != null ? _combo.WindowRemaining : 0f;
    public float ComboWindowRemainingNormalized => _combo != null ? _combo.WindowRemainingNormalized : 0f;
    public float CurrentComboDamageMultiplier => GetComboDamageMultiplier();
    public bool IsComboActive => _formController?.CurrentForm?.UsesCombo == true;
    public PlayerFormId CurrentFormId =>
        _formController != null && _formController.CurrentForm != null
            ? _formController.CurrentForm.FormId
            : PlayerFormId.Normal;
    public bool IsReloading => _isReloading;
    public PlayerBasicAttackMode CurrentBasicAttackMode => GetCurrentBasicAttackMode();
    public bool IsDamageInvincible => _damageInvincibleTimer > 0f || HasExternalInvincibility;
    public bool HasExternalInvincibility => _externalInvincibilityCount > 0;
    public bool IsDashing => _dashController != null && _dashController.IsDashing;
    public bool IsParryBusy => _isParrySequenceActive;
    public bool IsSkillBusy =>
        _isSkillCasting ||
        _skillRecoveryTimer > 0f ||
        _isParrySequenceActive ||
        _isReloading ||
        (_skillExecutor != null && _skillExecutor.IsMultiHitActive);
    public bool IsSlowed => _status != null && _status.IsSlowed;
    public float SlowRemainingTime => _status != null ? _status.SlowRemainingTime : 0f;
    public float SlowTotalDurationForUi => _status != null ? _status.SlowTotalDurationForUi : 0f;
    public float SlowRemainingRatio => _status != null ? _status.SlowRemainingRatio : 0f;
    public bool IsStunned => _status != null && _status.IsStunned;
    public float StunRemainingTime => _status != null ? _status.StunRemainingTime : 0f;
    public float StunTotalDurationForUi => _status != null ? _status.StunTotalDurationForUi : 0f;
    public float StunRemainingRatio => _status != null ? _status.StunRemainingRatio : 0f;
    public bool BlocksPlayerMovement =>
        _isSkillCasting ||
        _skillRecoveryTimer > 0f ||
        (blockMovementDuringParry && _isParrySequenceActive) ||
        (_skillExecutor != null && _skillExecutor.IsMultiHitActive);
    public float MoveSpeedMultiplier =>
        (_status != null ? _status.MoveSpeedMultiplier : 1f) *
        (1f + _itemStats.MoveSpeedBonusPercent / 100f);

    public Transform   CachedPlayerTransform => _cachedTransform;
    public Collider2D  CachedHitCollider     => _cachedHitCollider;
    public float       CachedHitRadius       => _cachedHitRadius;
    private SkillData ActiveBasicAttack =>
        currentWeapon != null && currentWeapon.basicAttackSkillData != null
            ? currentWeapon.basicAttackSkillData
            : basicAttackSkillData;
    public Vector2     CurrentAimDirection   =>
        _inputReader != null && _inputReader.HasMouseAim
            ? _aimDirectionContinuous
            : AimDirectionUtility.ToNormalizedDirection(_lastAimDirection);
    public Vector2Int  CurrentAimRawDirection => _lastAimDirection;

    /// <summary>무기 보정치가 합산된 최종 공격력.</summary>
    public int TotalAttack  => baseAttack
                               + (currentWeapon?.bonusAttack ?? 0)
                               + _itemStats.AttackBonus
                               + _attackBuff.CurrentAmount;

    /// <summary>무기 보정치가 합산된 최종 방어력.</summary>
    public int TotalDefense => baseDefense + (currentWeapon?.bonusDefense ?? 0) + _itemStats.DefenseBonus;

    public float EffectiveCritDamageMultiplier => Mathf.Max(1f, critDamageMultiplier);

    public event Action<PlayerCombatController> OnDied;
    public event Action<PlayerStatusEffectType> OnStatusEffectApplied;
    public event Action<PlayerStatusEffectType> OnStatusEffectEnded;

    // ══════════════════════════════════════════════════════════════
    //  초기화
    // ══════════════════════════════════════════════════════════════

    private void Awake()
    {
        _resource.Initialize(maxHp);
        _shield.OnChanged += HandleShieldChanged;
        CachePlayerHitInfo();
        RegisterAsActive();
        _attackExecutor = new AttackExecutor(transform, this, CombatLayers.EnemyFilter);
        _skillHitFlashRenderer = new SkillHitFlashRenderer(
            SortingLayer.NameToID("FloorFX"),
            30);
        _skillExecutor = new SkillExecutor(
            _attackExecutor,
            CanContinueMultiHit,
            HandleInstantAreaHit);
        _daggerDashEnemyHitCallback = HandleDaggerDashEnemyHit;
        _daggerProjectileEnemyHitCallback = HandleDaggerProjectileEnemyHit;
        _status = new PlayerStatusEffects(v => playerMovement?.TryApplyExternalDisplacement(v));
        _status.OnApplied += HandleStatusEffectApplied;
        _status.OnEnded += HandleStatusEffectEnded;
        _parryStack = new ParryStackResource(
            maxParryStack,
            parryStackGraceDuration,
            parryStackDecayInterval,
            parryStackDecayAmount);
        if (comboTierConfig != null)
        {
            _combo = new ComboMeter(
                comboTierConfig.StacksPerTier,
                comboTierConfig.MaxTier,
                comboTierConfig.Window,
                comboTierConfig.GainPerHit);
        }
        _inputReader = GetComponent<PlayerInputReader>();
        _dashController = GetComponent<PlayerDashController>();
        _formController = GetComponent<PlayerFormController>();
        _previousFormId = CurrentFormId;
        _relicBehaviors = GetComponent<PlayerBehaviors>();
        if (engravingLoadout == null)
            engravingLoadout = GetComponent<EngravingLoadout>();
        if (engravingLoadout != null)
            engravingLoadout.OnChanged += HandleEngravingLoadoutChanged;
        BindSkillSlots(currentWeapon);
        if (soulEnhancements == null)
            soulEnhancements = GetComponent<PlayerSoulEnhancements>();
        SubscribeSoulEnhancements();
        RecalculateSoulBonus();
        ResolveInventory();
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
        if (comboTierConfig == null)
            Debug.LogError("[PlayerCombatController] ComboTierConfig가 연결되지 않아 콤보가 비활성화됩니다.", this);
        if (_hitFlash == null)
            Debug.LogWarning("[PlayerCombatController] HitFlashFeedback을 찾지 못했습니다 — 자식에 추가하거나 SerializeField로 연결하세요.", this);
        if (invincibilityFlashFeedback == null)
            Debug.LogWarning("[PlayerCombatController] PlayerInvincibilityFlashFeedback을 찾지 못했습니다 — 자식에 추가하거나 SerializeField로 연결하세요.", this);
#endif
    }

    private void OnEnable()
    {
        SubscribeInventory();
        SubscribeSoulEnhancements();
        RecalculateItemStats();
        RecalculateSoulBonus();
    }

    private void OnDestroy()
    {
        _shield.OnChanged -= HandleShieldChanged;
        if (engravingLoadout != null)
            engravingLoadout.OnChanged -= HandleEngravingLoadoutChanged;
        UnsubscribeSoulEnhancements();
        UnsubscribeInventory();
        UnsubscribeDungeonChannel();
        _skillHitFlashRenderer?.Dispose();
        if (ReferenceEquals(Active, this))
            Active = null;
    }

    private void OnDisable()
    {
        UnsubscribeSoulEnhancements();
        UnsubscribeInventory();
        _skillHitFlashRenderer?.Clear();
        ClearSkillTimingState();
        ClearParryState();
        ClearReloadState();
        _status?.ClearAll();
        _shield.Clear();
        _attackBuff.Clear();
        ClearDaggerRuntimeState();
    }

    private void HandleStatusEffectApplied(PlayerStatusEffectType type)
    {
        OnStatusEffectApplied?.Invoke(type);
    }

    private void HandleStatusEffectEnded(PlayerStatusEffectType type)
    {
        OnStatusEffectEnded?.Invoke(type);
    }

    private void ResolveInventory()
    {
        if (inventory == null)
            inventory = GetComponent<PlayerInventory>();
    }

    private void SubscribeInventory()
    {
        ResolveInventory();
        if (inventory == null || _isInventorySubscribed)
            return;

        inventory.OnInventoryChanged += HandleInventoryChanged;
        _isInventorySubscribed = true;
    }

    private void UnsubscribeInventory()
    {
        if (inventory != null && _isInventorySubscribed)
            inventory.OnInventoryChanged -= HandleInventoryChanged;

        _isInventorySubscribed = false;
    }

    private void HandleInventoryChanged()
    {
        RecalculateItemStats();
    }

    private void SubscribeSoulEnhancements()
    {
        if (soulEnhancements == null || _isSoulEnhancementsSubscribed)
            return;

        soulEnhancements.OnChanged += HandleSoulEnhancementsChanged;
        _isSoulEnhancementsSubscribed = true;
    }

    private void UnsubscribeSoulEnhancements()
    {
        if (soulEnhancements != null && _isSoulEnhancementsSubscribed)
            soulEnhancements.OnChanged -= HandleSoulEnhancementsChanged;

        _isSoulEnhancementsSubscribed = false;
    }

    private void HandleSoulEnhancementsChanged()
    {
        RecalculateSoulBonus();
        ApplyWeaponMagazine(currentWeapon);
    }

    private void RecalculateSoulBonus()
    {
        PlayerFormId form = (_formController != null && _formController.CurrentForm != null)
            ? _formController.CurrentForm.FormId
            : PlayerFormId.Normal;

        _soulBonus.Recalculate(form, soulEnhancements, soulEnhancementTable);
        _parryStack?.SetMax(maxParryStack + Mathf.RoundToInt(_soulBonus.Get(SoulStatType.ParryStackMax)));
        _parryStack?.SetGraceDuration(parryStackGraceDuration + _soulBonus.Get(SoulStatType.ParryGrace));
    }

    private void RecalculateItemStats()
    {
        int previousMaxHpBonus = _itemStats.MaxHpBonus;
        int previousAttackBonus = _itemStats.AttackBonus;
        int previousDefenseBonus = _itemStats.DefenseBonus;
        int previousMoveSpeedBonusPercent = _itemStats.MoveSpeedBonusPercent;

        _itemStats.Recalculate(inventory != null ? inventory.Items : null);

        bool changed =
            previousMaxHpBonus != _itemStats.MaxHpBonus ||
            previousAttackBonus != _itemStats.AttackBonus ||
            previousDefenseBonus != _itemStats.DefenseBonus ||
            previousMoveSpeedBonusPercent != _itemStats.MoveSpeedBonusPercent;
        if (!changed)
            return;

        int maxHpDelta = _itemStats.MaxHpBonus - previousMaxHpBonus;
        if (maxHpDelta != 0 && IsAlive)
        {
            _resource.AdjustHp(maxHpDelta, MaxHp);
            combatChannel?.RaisePlayerHpChanged(CurrentHp, MaxHp);
            combatChannel?.RaisePlayerShieldChanged(CurrentShield, MaxHp);
        }

        combatChannel?.RaiseLoadoutChanged();
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
        RecalculateSoulBonus();
        ApplyWeaponMagazine(weapon);
        _cooldownController.ResetAll();
        BindSkillSlots(weapon);
        combatChannel?.RaiseLoadoutChanged();
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
            int magBonus = Mathf.RoundToInt(_soulBonus.Get(SoulStatType.MagazineSize));
            maxBullet = Mathf.Max(0, weapon.magazineSize + magBonus);
            _currentBullet = maxBullet;
            return;
        }

        maxBullet = 0;
        _currentBullet = 0;
    }

    private float EffectiveAttackCooldown()
    {
        if (currentWeapon == null)
            return 0f;

        float pct = _soulBonus.Get(SoulStatType.AttackSpeed);
        return currentWeapon.attackCooldown * Mathf.Max(0f, 1f - pct / 100f);
    }

    private float EffectiveSkillCooldownMultiplier()
    {
        return Mathf.Max(0f, 1f - _soulBonus.Get(SoulStatType.CooldownReduction) / 100f);
    }

    public float GetEffectiveCooldown(SkillData skill)
    {
        return skill != null
            ? Mathf.Max(0f, skill.cooldown) * EffectiveSkillCooldownMultiplier()
            : 0f;
    }

    public float AilmentDamageMultiplier =>
        1f + Mathf.Max(0f, _soulBonus.Get(SoulStatType.AilmentDamage)) / 100f;

    public IReadOnlyList<AilmentApplication> BonusAttackAilments =>
        _relicBehaviors != null
            ? _relicBehaviors.AttackAilments
            : Array.Empty<AilmentApplication>();

    public CombatEffectContext CreateCombatEffectContext(
        float ailmentDamageMultiplier)
    {
        AilmentOverloadSettings overload = default;
        _relicBehaviors?.TryGetAilmentOverloadSettings(out overload);
        ExecuteThresholdSettings executeThreshold = default;
        _relicBehaviors?.TryGetExecuteThresholdSettings(
            out executeThreshold);
        return new CombatEffectContext(
            ailmentDamageMultiplier,
            overload,
            executeThreshold);
    }

    public CombatEffectContext CurrentCombatEffectContext =>
        CreateCombatEffectContext(AilmentDamageMultiplier);

    private float EffectiveReloadTime()
    {
        if (currentWeapon == null)
            return 0f;

        float pct = _soulBonus.Get(SoulStatType.ReloadSpeed);
        return Mathf.Max(0f, currentWeapon.reloadTime) * Mathf.Max(0f, 1f - pct / 100f);
    }

    public int RollCritDamage(int baseDamage, out bool didCrit)
    {
        didCrit = false;
        if (baseDamage <= 0)
            return baseDamage;

        baseDamage = ApplyComboMultiplier(baseDamage);

        float chance = Mathf.Clamp01(_soulBonus.Get(SoulStatType.Crit));
        if (chance <= 0f || UnityEngine.Random.value >= chance)
            return baseDamage;

        didCrit = true;
        return Mathf.Max(1, Mathf.RoundToInt(baseDamage * EffectiveCritDamageMultiplier));
    }

    /// <summary>
    /// Scales outgoing damage by the current combo tier immediately before the critical roll.
    /// </summary>
    private int ApplyComboMultiplier(int baseDamage)
    {
        float multiplier = GetComboDamageMultiplier();
        if (multiplier == 1f)
            return baseDamage;

        return Mathf.Max(1, Mathf.RoundToInt(baseDamage * multiplier));
    }

    private float GetComboDamageMultiplier()
    {
        int tier = _combo != null ? _combo.Tier : 0;
        if (tier <= 0 || comboTierConfig == null)
            return 1f;

        float soulScale = _soulBonus.Get(SoulStatType.ComboDamage);
        if (soulScale <= 0f)
            return 1f;

        return 1f + comboTierConfig.GetTierBonusPct(tier) * soulScale / 100f;
    }

    /// <summary>Registers one landed attack action (one swing/projectile) toward the combo stack.</summary>
    public void RegisterComboHit()
    {
        if (!IsComboActive)
            return;

        _combo?.RegisterHit();
    }

    public bool AddComboStacks(int amount)
    {
        if (_combo == null || amount <= 0)
            return false;

        _combo.AddStacks(amount);
        return true;
    }

    public void ResetCombo()
    {
        _combo?.Reset();
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public void LogDamageDealt(int amount, bool isCrit)
    {
        if (amount <= 0)
            return;

        string combo = IsComboActive && CurrentComboStack > 0
            ? $" [combo x{CurrentComboStack}]"
            : string.Empty;
        Debug.Log((isCrit
            ? $"[Combat] 데미지 {amount} (CRITICAL)"
            : $"[Combat] 데미지 {amount}") + combo);
    }

    public void ReportLifestealDamage(int actualDamage)
    {
        if (actualDamage <= 0 || !IsAlive)
            return;

        float hpRatio = MaxHp > 0 ? CurrentHp / (float)MaxHp : 0f;
        float passiveBonusPct = _relicBehaviors != null
            ? _relicBehaviors.GetLifestealBonusPct(hpRatio)
            : 0f;
        float pct = Mathf.Clamp01(
            _soulBonus.Get(SoulStatType.Lifesteal) +
            passiveBonusPct / 100f);
        if (pct <= 0f)
            return;

        _lifestealPool += actualDamage * pct;
        int heal = Mathf.FloorToInt(_lifestealPool);
        if (heal <= 0)
            return;

        _lifestealPool -= heal;
        int room = Mathf.Max(0, MaxHp - CurrentHp);
        int actualHeal = Mathf.Min(heal, room);
        if (actualHeal > 0)
            RestoreHp(actualHeal);

        int overheal = heal - actualHeal;
        if (overheal <= 0 ||
            _relicBehaviors == null ||
            !_relicBehaviors.TryGetLifestealShieldParameters(
                out float conversionPct,
                out float capPct,
                out float shieldDuration))
        {
            return;
        }

        _lifestealShieldPool +=
            overheal * Mathf.Max(0f, conversionPct) / 100f;
        int convertedShield = Mathf.FloorToInt(_lifestealShieldPool);
        if (convertedShield <= 0)
            return;

        _lifestealShieldPool -= convertedShield;
        int shieldCap = Mathf.Max(
            0,
            Mathf.FloorToInt(
                MaxHp * Mathf.Max(0f, capPct) / 100f));
        int shieldRoom = Mathf.Max(0, shieldCap - CurrentShield);
        int shieldToAdd = Mathf.Min(convertedShield, shieldRoom);
        if (shieldToAdd > 0)
        {
            _shield.Add(
                ShieldSource.LifestealConversion,
                shieldToAdd,
                shieldDuration);
        }
    }

    private void Update()
    {
        _skillHitFlashRenderer?.Tick(Time.deltaTime);
        TickRecastChain(Time.deltaTime);

        if (IsDead)
            return;

        _shield.Tick(Time.deltaTime);
        _attackBuff.Tick(Time.deltaTime);

        if (_damageInvincibleTimer > 0f)
            _damageInvincibleTimer -= Time.deltaTime;

        _status?.Tick(Time.deltaTime);
        _parryStack?.Tick(Time.deltaTime);
        bool hasActiveEnemies = EnemyPoolManager.Instance != null && EnemyPoolManager.Instance.HasActiveEnemies;
        PlayerFormId currentFormId = CurrentFormId;
        if (currentFormId != _previousFormId)
        {
            _previousFormId = currentFormId;
            ResetCombo();
        }
        _combo?.Tick(Time.deltaTime, hasActiveEnemies);
        EnsureSkillSlotsBound();
        _cooldownController.Tick(Time.deltaTime);
        TickSkillSlots(Time.deltaTime);
        TickDodgeCooldown(Time.deltaTime);
        TickSkillRecovery(Time.deltaTime);
        TickDaggerState(Time.deltaTime);
        _skillExecutor?.TickMultiHit(Time.deltaTime);

        if (_inputReader == null) return;

        if (DungeonManager.Instance != null && DungeonManager.Instance.IsTransitioning) return;
        if (IsCombatBlockedByLocation()) return;
        if (IsDashing) return;
        if (IsStunned) return;

        RefreshAimDirection();

        if (_inputReader.WasReloadPressed && IsCurrentFormBulletMode())
            TryStartReload();
        if (IsSkillBusy && !CanCancelActiveSkill()) return;

        if (_inputReader.WasDodgePressed) TryUseDodge();
        if (_inputReader.WasBasicAttackPressed)  TryBasicAttack();
        if (_inputReader.WasSkillPressed(0)) TryUseSkill(0);
        if (_inputReader.WasSkillPressed(1)) TryUseSkill(1);
        if (_inputReader.WasSkillPressed(2)) TryUseSkill(2);
        if (_inputReader.WasSkillPressed(3)) TryUseSkill(3);
    }

    private void HandleInstantAreaHit(
        IReadOnlyList<Vector3> worldTargets,
        CustomShapeMatcher? customShape,
        float cellSize)
    {
        if (!enableSkillHitFlash)
            return;

        _skillHitFlashRenderer?.Flash(
            worldTargets,
            customShape,
            cellSize,
            skillHitFlashColor,
            skillHitFlashDuration);
    }

    public Vector2 RefreshAimDirection()
    {
        if (IsStunned)
            return CurrentAimDirection;

        if (_inputReader != null && _inputReader.HasMouseAim)
        {
            Vector2 aim = _inputReader.AimWorldPoint - (Vector2)transform.position;
            if (aim.sqrMagnitude > MouseAimEpsilonSqr)
            {
                _aimDirectionContinuous = aim.normalized;

                if (AimDirectionUtility.TryGetEightWayRaw(aim, out Vector2Int mouseRawDirection))
                    _lastAimDirection = mouseRawDirection;
            }

            _formController?.ApplyFacing(CurrentAimDirection);
            return CurrentAimDirection;
        }

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
            _cooldownController.SetAttackCooldown(EffectiveAttackCooldown());
            BeginParryBasicAttack();
            return;
        }

        if (IsCurrentFormBulletMode())
        {
            TryBulletBasicAttack();
            return;
        }

        _cooldownController.SetAttackCooldown(EffectiveAttackCooldown());

        SkillData basicAttack = ActiveBasicAttack;
        _attackExecutor.BeginAttackActivation();
        if (basicAttack != null)
            _formController?.PlaySkillAnimation(basicAttack, CurrentAimDirection);

        SkillTargetResolver.FillWorldTargets(
            currentWeapon.attackPattern,
            transform.position,
            CurrentAimDirection,
            GetGridAimDirection(),
            currentWeapon.patternRange,
            45f,
            null,
            _basicAttackWorldTargets);

        int basicDamage = RollCritDamage(TotalAttack + currentWeapon.damage, out bool didCrit);
        _attackExecutor.ExecuteAttackWorld(
            _basicAttackWorldTargets,
            basicDamage,
            currentWeapon.canPenetrateWalls,
            currentWeapon.basicAttackMultiTarget,
            currentWeapon.knockbackForce,
            currentWeapon.knockbackDuration,
            currentWeapon.slowPercentage,
            currentWeapon.slowDuration,
            ResolveBasicAttackAilments(),
            CurrentCombatEffectContext,
            hitRadius);

        ReportLifestealDamage(_attackExecutor.DamageDealtThisAttack);
        if (_attackExecutor.HitEnemyCount > 0)
        {
            RegisterComboHit();
            LogDamageDealt(_attackExecutor.DamageDealtThisAttack, didCrit);
        }
        ApplyDaggerMarkersFromBasicAttack();
    }

    private AilmentApplication[] ResolveBasicAttackAilments()
    {
        IReadOnlyList<AilmentApplication> bonuses = BonusAttackAilments;
        if (bonuses == null || bonuses.Count == 0)
            return null;

        AilmentApplication[] merged = new AilmentApplication[bonuses.Count];
        for (int i = 0; i < bonuses.Count; i++)
            merged[i] = bonuses[i];

        return merged;
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

        SkillData basicAttack = ActiveBasicAttack;
        if (basicAttack == null || basicAttack.executionType != SkillExecutionType.Projectile)
            return;

        SkillExecutionContext context = CreateSkillExecutionContext(basicAttack, -1);
        if (!_skillExecutor.ExecuteBasicProjectile(context, 1))
            return;

        _cooldownController.SetAttackCooldown(EffectiveAttackCooldown());
        Spend(SkillResourceType.Bullet, 1);
        TryStartAutoReloadIfEmpty();
    }

    private void BeginParryBasicAttack()
    {
        if (_parryRoutine != null)
            StopCoroutine(_parryRoutine);

        SkillData basicAttack = ActiveBasicAttack;
        if (basicAttack != null)
            _formController?.PlaySkillAnimation(basicAttack, CurrentAimDirection);

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

    private void TryUseDodge()
    {
        if (dodgeSkill == null) return;
        if (_dodgeCooldownTimer > 0f) return;
        if (IsDead) return;
        if (IsDashing) return;
        if (IsStunned) return;
        if (DungeonManager.Instance != null && DungeonManager.Instance.IsTransitioning) return;
        if (IsCombatBlockedByLocation()) return;

        bool cancelsActiveSkill = CanCancelActiveSkill();
        if (IsSkillBusy && !cancelsActiveSkill) return;

        if (cancelsActiveSkill)
            CancelActiveSkillFor(dodgeSkill);

        Vector2 dodgeDirection = ResolveDodgeDirection();
        SkillExecutionContext context = CreateSkillExecutionContext(dodgeSkill, -1, dodgeDirection);
        SkillExecutionResult result = _skillExecutor.Execute(context);
        if (!result.Success) return;

        _dodgeCooldownTimer = Mathf.Max(0f, dodgeSkill.cooldown) * EffectiveSkillCooldownMultiplier();
        StartSkillRecovery(dodgeSkill, dodgeSkill.recoveryDelay);
        combatChannel?.RaiseSkillUsed(dodgeSkill);
    }

    private Vector2 ResolveDodgeDirection()
    {
        if (_inputReader != null && _inputReader.HasMouseAim)
            return RefreshAimDirection();

        Vector2 moveDirection = _inputReader != null ? _inputReader.MoveInput : Vector2.zero;
        if (moveDirection.sqrMagnitude > 0.0001f)
            return moveDirection.normalized;

        Vector2Int facing = playerMovement != null ? playerMovement.FacingDirection : Vector2Int.down;
        return AimDirectionUtility.ToNormalizedDirection(facing);
    }

    private void TryUseSkill(int slotIndex)
    {
        if (IsDead) return;
        if (IsDashing) return;
        if (IsStunned) return;
        bool cancelsActiveSkill = CanCancelActiveSkill();
        if (IsSkillBusy && !cancelsActiveSkill) return;
        EnsureSkillSlotsBound();
        SkillSlotRuntime slot = GetSkillSlot(slotIndex);
        if (slot == null) return;

        if (TryHandleRecastInput(slotIndex, slot))
            return;

        if (!slot.CanUse(this)) return;

        SkillData skill = slot.Data;
        if (cancelsActiveSkill)
            CancelActiveSkillFor(skill);

        float castDelay = Mathf.Max(0f, skill.castDelay);
        if (castDelay > 0f)
        {
            BeginSkillCast(slotIndex, skill, castDelay);
            return;
        }

        ExecuteSkillIfReady(slotIndex, skill);
    }

    /// <summary>
    /// Executes a skill outside the normal cast pipeline without cost, cooldown,
    /// recovery, animation, recast, cancellation, or skill-used events.
    /// </summary>
    public bool ExecuteSkillProc(
        SkillData skill,
        Vector3 origin,
        Vector2 direction,
        int? skillDamageOverride = null)
    {
        if (skill == null || IsDead)
            return false;

        switch (skill.executionType)
        {
            case SkillExecutionType.AreaOverTime:
            case SkillExecutionType.InstantArea:
            case SkillExecutionType.Projectile:
            case SkillExecutionType.Buff:
                break;
            default:
                WarnRejectedProcTypeOnce(skill.executionType);
                return false;
        }

        if (direction.sqrMagnitude <= 0.0001f)
            direction = CurrentAimDirection;
        direction.Normalize();

        Vector2Int rawDirection = AimDirectionUtility.ResolveEightWayRaw(
            direction,
            playerMovement != null ? playerMovement.FacingDirection : Vector2Int.down);
        Vector2Int gridFacing = SkillTargetResolver.ToGridAimDirection(
            AimDirectionUtility.ToCardinalDirection(rawDirection));
        SkillExecutionContext context = new SkillExecutionContext(
            this,
            _dashController,
            _formController,
            transform,
            skill,
            -1,
            direction,
            gridFacing,
            TotalAttack,
            hitRadius,
            true,
            origin,
            skillDamageOverride);

        return _skillExecutor.Execute(context).Success;
    }

    public void ClearAllProcSkillSequences()
    {
        _skillExecutor?.ClearAllProcSequences();
    }

    private void WarnRejectedProcTypeOnce(SkillExecutionType executionType)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedRejectedProcTypes.Add(executionType))
            Debug.LogWarning("[PlayerCombatController] Proc rejected execution type: " + executionType + ".", this);
#endif
    }

    private bool CanCancelActiveSkill()
    {
        if (_isSkillCasting || _isParrySequenceActive || _isReloading)
            return false;

        if (_skillExecutor != null && _skillExecutor.IsMultiHitActive)
        {
            SkillData activeSkill = _skillExecutor.ActiveMultiHitSkill;
            if (activeSkill != null && activeSkill.cancelable)
                return true;
        }

        if (_skillRecoveryTimer > 0f && _recoveryCancelableSkill != null)
            return true;

        return false;
    }

    private bool TryHandleRecastInput(
        int slotIndex,
        SkillSlotRuntime slot)
    {
        if ((uint)slotIndex >= (uint)_recastChains.Length)
            return false;

        RecastChainEntry chain = _recastChains[slotIndex];
        if (chain.RootSkill == null)
            return false;

        List<SkillData> stages = chain.RootSkill.recastStages;
        if (chain.WindowTimer <= 0f ||
            !ReferenceEquals(slot.Data, chain.RootSkill) ||
            stages == null ||
            (uint)chain.StageIndex >= (uint)stages.Count)
        {
            return true;
        }

        if (IsSkillBusy)
            return true;

        SkillData stage = stages[chain.StageIndex];
        if (stage == null)
            return true;

        int requiredAmount = SkillSlotRuntime.ResolveRequiredAmount(stage);
        if (!Has(stage.resourceType, requiredAmount))
            return true;

        if (!IsPendingRecastStage(slotIndex, stage))
            return true;

        requiredAmount = SkillSlotRuntime.ResolveRequiredAmount(stage);
        if (!Has(stage.resourceType, requiredAmount))
            return true;

        SkillExecutionContext context = CreateSkillExecutionContext(stage, slotIndex);
        SkillExecutionResult result = _skillExecutor.Execute(context);
        if (!result.Success)
            return true;

        Spend(stage.resourceType, result.ResourceConsumed);
        ApplySkillReload(stage);
        StartSkillRecovery(stage, stage.recoveryDelay);
        AdvanceRecastChain(slotIndex);
        combatChannel?.RaiseSkillUsed(stage);
        return true;
    }

    private bool IsPendingRecastStage(int slotIndex, SkillData stage)
    {
        if ((uint)slotIndex >= (uint)_recastChains.Length)
            return false;

        RecastChainEntry chain = _recastChains[slotIndex];
        if (chain.RootSkill == null ||
            chain.WindowTimer <= 0f ||
            chain.RootSkill.recastStages == null ||
            (uint)chain.StageIndex >= (uint)chain.RootSkill.recastStages.Count)
        {
            return false;
        }

        return ReferenceEquals(chain.RootSkill.recastStages[chain.StageIndex], stage) &&
               ReferenceEquals(GetSkillSlot(slotIndex)?.Data, chain.RootSkill);
    }

    private void CancelActiveSkillFor(SkillData cancelingSkill)
    {
        SkillData canceledSkill = null;
        if (_skillExecutor != null && _skillExecutor.IsMultiHitActive)
        {
            canceledSkill = _skillExecutor.ActiveMultiHitSkill;
            _skillExecutor.CancelMultiHit();
        }
        else if (_skillRecoveryTimer > 0f && _recoveryCancelableSkill != null)
        {
            canceledSkill = _recoveryCancelableSkill;
        }

        _skillRecoveryTimer = 0f;
        _recoveryCancelableSkill = null;

        if (canceledSkill != null)
            combatChannel?.RaiseSkillCanceled(canceledSkill, cancelingSkill);
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
        slot.StartCooldown(EffectiveSkillCooldownMultiplier());
        ConsumePendingDaggerCooldownReset(slotIndex);
        StartSkillRecovery(skill, skill.recoveryDelay);
        OpenRecastChain(slotIndex, skill);
        combatChannel?.RaiseSkillUsed(skill);

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
        if (enemy == null || projectile == null || projectile.IsProcCast)
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

        Vector3 detonationPosition = enemy.transform.position;
        int detonationDamage = skill.markerDetonationDamage > 0
            ? skill.markerDetonationDamage
            : skill.damage;
        if (detonationDamage > 0 && enemy.IsAlive)
        {
            int amplifiedDamage = Mathf.Max(1, Mathf.RoundToInt(detonationDamage * AilmentDamageMultiplier));
            int actualDamage = enemy.ApplyCombatImpact(
                RollCritDamage(amplifiedDamage, out bool didCrit),
                transform.position,
                0f,
                0f,
                0f,
                0f,
                null,
                CurrentCombatEffectContext);
            ReportLifestealDamage(actualDamage);
            if (actualDamage > 0)
                RegisterComboHit();
            LogDamageDealt(actualDamage, didCrit);
        }

        // Resolve authored single-target damage first so a proc kill cannot suppress it.
        combatChannel?.RaiseMarkerDetonated(detonationPosition);

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
        {
            _skillRecoveryTimer -= deltaTime;
            if (_skillRecoveryTimer <= 0f)
                _recoveryCancelableSkill = null;
        }
    }

    private void TickDodgeCooldown(float deltaTime)
    {
        if (_dodgeCooldownTimer > 0f)
            _dodgeCooldownTimer = Mathf.Max(0f, _dodgeCooldownTimer - deltaTime);
    }

    private void StartSkillRecovery(SkillData skill, float recoveryDelay)
    {
        _skillRecoveryTimer = Mathf.Max(_skillRecoveryTimer, Mathf.Max(0f, recoveryDelay));
        _recoveryCancelableSkill = recoveryDelay > 0f && skill != null && skill.cancelable
            ? skill
            : null;
    }

    private void TickRecastChain(float deltaTime)
    {
        float elapsed = Mathf.Max(0f, deltaTime);
        for (int slotIndex = 0; slotIndex < _recastChains.Length; slotIndex++)
        {
            if (_recastChains[slotIndex].RootSkill == null)
                continue;

            if (_recastChains[slotIndex].RecoveryHold > 0f)
            {
                _recastChains[slotIndex].RecoveryHold -= elapsed;
                continue;
            }

            _recastChains[slotIndex].WindowTimer -= elapsed;
            if (_recastChains[slotIndex].WindowTimer <= 0f)
                ResetRecastChain(slotIndex);
        }
    }

    private void OpenRecastChain(int slotIndex, SkillData rootSkill)
    {
        if ((uint)slotIndex >= (uint)_recastChains.Length ||
            rootSkill == null ||
            rootSkill.recastStages == null ||
            rootSkill.recastStages.Count == 0)
        {
            return;
        }

        _recastChains[slotIndex] = new RecastChainEntry
        {
            RootSkill = rootSkill,
            StageIndex = 0,
            WindowTimer = Mathf.Max(0f, rootSkill.recastWindow),
            RecoveryHold = Mathf.Max(0f, rootSkill.recoveryDelay),
            RecoveryTotal = Mathf.Max(0f, rootSkill.recoveryDelay)
        };
    }

    private void AdvanceRecastChain(int slotIndex)
    {
        if ((uint)slotIndex >= (uint)_recastChains.Length)
            return;

        RecastChainEntry chain = _recastChains[slotIndex];
        if (chain.RootSkill == null || chain.RootSkill.recastStages == null)
            return;

        SkillData justExecuted =
            chain.StageIndex >= 0 && chain.StageIndex < chain.RootSkill.recastStages.Count
                ? chain.RootSkill.recastStages[chain.StageIndex]
                : null;

        chain.StageIndex++;
        if (chain.StageIndex >= chain.RootSkill.recastStages.Count)
        {
            ResetRecastChain(slotIndex);
            return;
        }

        chain.WindowTimer = Mathf.Max(0f, chain.RootSkill.recastWindow);
        chain.RecoveryHold = justExecuted != null ? Mathf.Max(0f, justExecuted.recoveryDelay) : 0f;
        chain.RecoveryTotal = chain.RecoveryHold;
        _recastChains[slotIndex] = chain;
    }

    private void ResetRecastChain(int slotIndex)
    {
        if ((uint)slotIndex < (uint)_recastChains.Length)
            _recastChains[slotIndex] = default;
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
        _recoveryCancelableSkill = null;
        _skillExecutor?.CancelMultiHit();
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
        float remaining = EffectiveReloadTime();
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
        Vector2 aimDirection = RefreshAimDirection();
        Vector2Int gridFacing = GetGridAimDirection();

        return CreateSkillExecutionContext(skill, slotIndex, aimDirection, gridFacing);
    }

    private SkillExecutionContext CreateSkillExecutionContext(
        SkillData skill,
        int slotIndex,
        Vector2 aimDirection)
    {
        Vector2Int rawDirection = AimDirectionUtility.ResolveEightWayRaw(
            aimDirection,
            playerMovement != null ? playerMovement.FacingDirection : Vector2Int.down);
        Vector2Int gridFacing = SkillTargetResolver.ToGridAimDirection(
            AimDirectionUtility.ToCardinalDirection(rawDirection));

        return CreateSkillExecutionContext(skill, slotIndex, aimDirection, gridFacing);
    }

    private SkillExecutionContext CreateSkillExecutionContext(
        SkillData skill,
        int slotIndex,
        Vector2 aimDirection,
        Vector2Int gridFacing)
    {

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

    private bool CanContinueMultiHit(SkillExecutionContext context)
    {
        if (context == null)
            return false;

        PlayerFormData currentForm = _formController != null ? _formController.CurrentForm : null;
        return !IsDead &&
               !IsStunned &&
               !IsDashing &&
               (DungeonManager.Instance == null || !DungeonManager.Instance.IsTransitioning) &&
               !IsCombatBlockedByLocation() &&
               ReferenceEquals(currentForm, context.CasterFormData);
    }

    private Vector2Int GetGridAimDirection()
    {
        if (_inputReader != null && _inputReader.HasMouseAim)
            return SkillTargetResolver.ToGridAimDirection(
                AimDirectionUtility.ToCardinalDirection(_lastAimDirection));

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

        if (playerMovement != null && knockbackForce > 0f && knockbackDuration > 0f)
            _status?.ApplyKnockback(ResolveEnemyImpactDirection(hitDirection), knockbackForce, knockbackDuration);
        _status?.ApplySlow(slowMultiplier, slowDuration);
        _status?.ApplyStun(stunDuration);
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
        int toHp = _shield.IsActive ? _shield.Absorb(actual) : actual;
        _damageInvincibleTimer = damageInvincibleDuration;

        if (_isParryStartupActive)
            _parryCancelled = true;

        if (toHp <= 0)
        {
            _hitFlash?.Play(shieldFlashColor);
            return true;
        }

        int hpBefore = CurrentHp;
        _resource.TakeDamage(toHp);
        if (CurrentHp >= hpBefore)
            return false;

        _hitFlash?.Play();
        combatChannel?.RaisePlayerHpChanged(CurrentHp, MaxHp);
#if UNITY_EDITOR
        Debug.Log($"[Combat] Player -{toHp} HP -> {CurrentHp}/{MaxHp}");
#endif
        if (CurrentHp == 0)
            Die();

        return true;
    }

    private void CompleteParryIntercept()
    {
        _parryIntercepted = true;
        _isParryInvincibleWindowActive = false;
        _parryStack?.Add(1);
        invincibilityFlashFeedback?.StopAndReset();
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

    private void Die()
    {
        if (IsDead)
            return;

        IsDead = true;
        _damageInvincibleTimer = 0f;
        _externalInvincibilityCount = 0;
        ResetCombo();
        ClearSkillTimingState();
        ClearAllProcSkillSequences();
        ClearParryState();
        ClearReloadState();
        _status?.ClearAll();
        _shield.Clear();
        _attackBuff.Clear();
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

        _resource.RestoreHp(amount, MaxHp);
        combatChannel?.RaisePlayerHpChanged(CurrentHp, MaxHp);
    }

    public void GrantShield(
        ShieldSource source,
        int amount,
        float duration)
    {
        if (IsDead)
            return;

        _shield.Grant(source, amount, duration);
    }

    public void GrantAttackBuff(int amount, float duration)
    {
        if (IsDead)
            return;

        _attackBuff.Grant(amount, duration);
    }

    public void ClearShield()
    {
        _shield.Clear();
    }

    private void HandleShieldChanged()
    {
        combatChannel?.RaisePlayerShieldChanged(CurrentShield, MaxHp);
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
                return _parryStack != null && _parryStack.Spend(amount);

            case SkillResourceType.Combo:
                return _combo != null && _combo.Spend(amount);

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
                return _parryStack != null ? _parryStack.Current : 0;

            case SkillResourceType.Combo:
                return CurrentComboStack;

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
                _parryStack?.Restore(amount);
                break;
        }
    }

    // ── 스킬 쿨다운 조회 (UI 표시용) ────────────────────────────────
    public bool TryGetRecastRecoveryState(
        int slotIndex,
        out float remaining,
        out float total,
        out SkillData nextStage)
    {
        remaining = 0f;
        total = 0f;
        nextStage = null;
        EnsureSkillSlotsBound();

        if ((uint)slotIndex >= (uint)_recastChains.Length)
            return false;

        RecastChainEntry chain = _recastChains[slotIndex];
        List<SkillData> stages = chain.RootSkill != null ? chain.RootSkill.recastStages : null;
        if (chain.RootSkill == null ||
            chain.RecoveryHold <= 0f ||
            stages == null ||
            (uint)chain.StageIndex >= (uint)stages.Count ||
            !ReferenceEquals(GetSkillSlot(slotIndex)?.Data, chain.RootSkill))
        {
            return false;
        }

        nextStage = stages[chain.StageIndex];
        if (nextStage == null)
            return false;

        total = Mathf.Max(0f, chain.RecoveryTotal);
        remaining = total > 0f ? Mathf.Clamp(chain.RecoveryHold, 0f, total) : 0f;
        return true;
    }

    public bool TryGetRecastState(
        int slotIndex,
        out float remaining,
        out float total,
        out SkillData nextStage)
    {
        remaining = 0f;
        total = 0f;
        nextStage = null;
        EnsureSkillSlotsBound();

        if ((uint)slotIndex >= (uint)_recastChains.Length)
            return false;

        RecastChainEntry chain = _recastChains[slotIndex];
        List<SkillData> stages = chain.RootSkill != null ? chain.RootSkill.recastStages : null;
        if (chain.RootSkill == null ||
            chain.WindowTimer <= 0f ||
            stages == null ||
            (uint)chain.StageIndex >= (uint)stages.Count ||
            !ReferenceEquals(GetSkillSlot(slotIndex)?.Data, chain.RootSkill))
        {
            return false;
        }

        nextStage = stages[chain.StageIndex];
        if (nextStage == null)
            return false;

        total = Mathf.Max(0f, chain.RootSkill.recastWindow);
        remaining = total > 0f ? Mathf.Clamp(chain.WindowTimer, 0f, total) : 0f;
        return true;
    }

    public float GetSkillCooldownRemaining(int slotIndex)
    {
        EnsureSkillSlotsBound();
        SkillSlotRuntime slot = GetSkillSlot(slotIndex);
        return slot != null && slot.CooldownRemaining > 0f ? slot.CooldownRemaining : 0f;
    }

    public float GetSkillCooldownMax(int slotIndex)
    {
        SkillData skill = GetSkillData(slotIndex);
        return GetEffectiveCooldown(skill);
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

    public bool HasResourceFor(SkillData skill)
    {
        if (skill == null || skill.resourceType == SkillResourceType.None)
            return true;

        return Has(skill.resourceType, SkillSlotRuntime.ResolveRequiredAmount(skill));
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
        _parryStack?.Reset();
    }

    private void HandleRoomDoorsOpened(RoomInfo room)
    {
        _parryStack?.Reset();
    }

    private void HandleFloorChanged(int previousFloor, int newFloor)
    {
        _parryStack?.Reset();
        ResetCombo();
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

    private void HandleEngravingLoadoutChanged()
    {
        BindSkillSlots(currentWeapon);
    }

    private void BindSkillSlots(WeaponData weapon)
    {
        _boundSkillWeapon = weapon;
        SkillData[] skills = weapon != null ? weapon.skills : null;
        PlayerFormId form = CurrentFormId;

        if (engravingLoadout != null)
        {
            engravingLoadout.EnsureSeeded(form, skills);
            engravingLoadout.EnsurePassiveSeeded(
                form,
                weapon != null ? weapon.defaultPassive : null);
        }

        for (int i = 0; i < _skillSlots.Length; i++)
        {
            SkillData token = engravingLoadout != null
                ? engravingLoadout.GetSlot(form, i)
                : (skills != null && i < skills.Length ? skills[i] : null);
            _skillSlots[i].Bind(token);
        }
    }

    private void TickSkillSlots(float deltaTime)
    {
        for (int i = 0; i < _skillSlots.Length; i++)
            _skillSlots[i].TickCooldown(deltaTime);
    }
}
