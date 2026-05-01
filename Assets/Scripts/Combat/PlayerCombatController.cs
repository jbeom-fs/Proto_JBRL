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

using System.Collections.Generic;
using UnityEngine;

public class PlayerCombatController : MonoBehaviour, IDamageable
{
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

    // ── 런타임 상태 ─────────────────────────────────────────────────

    private readonly PlayerResource _resource = new();
    private readonly SkillCooldownController _cooldownController = new();
    private AttackExecutor _attackExecutor;
    private PlayerInputReader _inputReader;

    // ── 공개 프로퍼티 ────────────────────────────────────────────────

    public bool IsAlive     => _resource.IsAlive;
    public int  CurrentHp   => _resource.CurrentHp;
    public int  MaxHp       => maxHp;
    public int  CurrentMp   => _resource.CurrentMp;
    public int  MaxMp       => maxMp;

    /// <summary>무기 보정치가 합산된 최종 공격력.</summary>
    public int TotalAttack  => baseAttack  + (currentWeapon?.bonusAttack  ?? 0);

    /// <summary>무기 보정치가 합산된 최종 방어력.</summary>
    public int TotalDefense => baseDefense + (currentWeapon?.bonusDefense ?? 0);

    // ══════════════════════════════════════════════════════════════
    //  초기화
    // ══════════════════════════════════════════════════════════════

    private void Awake()
    {
        _resource.Initialize(maxHp, maxMp);
        _attackExecutor = new AttackExecutor(transform, this);
        if (combatChannel == null)
            Debug.LogWarning("[PlayerCombatController] CombatEventChannel 없음 — HP/MP/스킬 UI 이벤트가 발행되지 않습니다.");
        if (playerMovement == null)
            Debug.LogWarning("[PlayerCombatController] PlayerController 없음 — 공격 방향이 기본 방향을 사용합니다.");
        _inputReader = GetComponent<PlayerInputReader>();
        if (_inputReader == null && playerMovement != null)
            _inputReader = playerMovement.GetComponent<PlayerInputReader>();
        if (_inputReader == null)
            Debug.LogWarning("[PlayerCombatController] PlayerInputReader 없음 — 전투 입력 불가");
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
#if UNITY_EDITOR
        Debug.Log($"[Combat] 무기 장착: {weapon?.weaponName ?? "없음"}");
#endif
    }

    // ══════════════════════════════════════════════════════════════
    //  매 프레임 처리
    // ══════════════════════════════════════════════════════════════

    private void Update()
    {
        _cooldownController.Tick(Time.deltaTime);

        if (DungeonManager.Instance != null && DungeonManager.Instance.IsTransitioning) return;

        if (_inputReader == null) return;

        if (_inputReader.WasBasicAttackPressed)  TryBasicAttack();
        if (_inputReader.WasSkillPressed(0)) TryUseSkill(0);
        if (_inputReader.WasSkillPressed(1)) TryUseSkill(1);
        if (_inputReader.WasSkillPressed(2)) TryUseSkill(2);
        if (_inputReader.WasSkillPressed(3)) TryUseSkill(3);
    }

    // ══════════════════════════════════════════════════════════════
    //  기본 공격
    // ══════════════════════════════════════════════════════════════

    private void TryBasicAttack()
    {
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
        if (currentWeapon == null) return;
        if (currentWeapon.skills == null) return;
        if ((uint)slotIndex >= (uint)currentWeapon.skills.Length) return;

        SkillData skill = currentWeapon.skills[slotIndex];
        if (skill == null)                       return;
        if (!_cooldownController.IsSkillReady(slotIndex)) return;
        if (CurrentMp < skill.mpCost)            return;

        _cooldownController.SetSkillCooldown(slotIndex, skill.cooldown);
        SpendMp(skill.mpCost);
        _attackExecutor.BeginAttackActivation();

        var targets = ResolveTargets(skill.attackPattern, skill.patternRange, skill.coneHalfAngle);
        _attackExecutor.ExecuteAttack(
            targets,
            TotalAttack + skill.damage,
            skill.canPenetrateWalls,
            skill.isMultiTarget,
            skill.knockbackForce,
            skill.knockbackDuration,
            skill.slowPercentage,
            skill.slowDuration,
            hitRadius);

        combatChannel?.RaiseSkillUsed(skill);
#if UNITY_EDITOR
        Debug.Log($"[Combat] 스킬 [{slotIndex + 1}] {skill.skillName} 사용");
#endif
    }

    // ══════════════════════════════════════════════════════════════
    //  공통 헬퍼
    // ══════════════════════════════════════════════════════════════

    private List<Vector2Int> ResolveTargets(AttackPatternType pattern, int range, float coneHalfAngle = 45f)
    {
        var dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null) return new List<Vector2Int>();

        var origin = dungeonManager.WorldToGrid(transform.position);

        // FacingDirection은 화면 공간 (Up키 → y=+1).
        // 그리드 좌표계는 GridToWorld에서 Y가 반전(tilemap y = -row)되므로
        // 화면 +Y = 그리드 -Y 로 변환해야 실제 방향과 일치한다.
        var screenFacing = playerMovement != null ? playerMovement.FacingDirection : Vector2Int.down;
        var gridFacing   = new Vector2Int(screenFacing.x, -screenFacing.y);

        return AttackPattern.GetTargets(pattern, origin, gridFacing, range, coneHalfAngle);
    }

    // ══════════════════════════════════════════════════════════════
    //  피해 수신 (IDamageable)
    // ══════════════════════════════════════════════════════════════

    public void TakeDamage(int incomingDamage)
    {
        if (!IsAlive) return;

        int actual = Mathf.Max(1, incomingDamage - TotalDefense);
        _resource.TakeDamage(actual);
        combatChannel?.RaisePlayerHpChanged(CurrentHp, maxHp);
#if UNITY_EDITOR
        Debug.Log($"[Combat] 플레이어 -{actual} HP → {CurrentHp}/{maxHp}");
#endif
        if (CurrentHp == 0)
            OnPlayerDied();
    }

    private void OnPlayerDied()
    {
#if UNITY_EDITOR
        Debug.Log("[Combat] 플레이어 사망");
#endif
    }

    // ══════════════════════════════════════════════════════════════
    //  MP 관리
    // ══════════════════════════════════════════════════════════════

    private void SpendMp(int amount)
    {
        _resource.SpendMp(amount);
        combatChannel?.RaisePlayerMpChanged(CurrentMp, maxMp);
    }

    public void RestoreMp(int amount)
    {
        _resource.RestoreMp(amount, maxMp);
        combatChannel?.RaisePlayerMpChanged(CurrentMp, maxMp);
    }

    public void RestoreHp(int amount)
    {
        _resource.RestoreHp(amount, maxHp);
        combatChannel?.RaisePlayerHpChanged(CurrentHp, maxHp);
    }

    // ── 스킬 쿨다운 조회 (UI 표시용) ────────────────────────────────
    public float GetSkillCooldownRemaining(int slotIndex) =>
        (uint)slotIndex < 4u ? _cooldownController.GetSkillRemaining(slotIndex) : 0f;

    public float GetSkillCooldownMax(int slotIndex)
    {
        if (currentWeapon == null || currentWeapon.skills == null) return 0f;
        if ((uint)slotIndex >= (uint)currentWeapon.skills.Length) return 0f;
        return currentWeapon.skills[slotIndex]?.cooldown ?? 0f;
    }
}
