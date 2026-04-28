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
using UnityEngine.InputSystem;

public class PlayerCombatController : MonoBehaviour, IDamageable
{
    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("Dependencies")]
    public DungeonManager    dungeonManager;
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

    private int     _currentHp;
    private int     _currentMp;
    private float   _attackCooldown;
    private readonly float[] _skillCooldowns = new float[4];

    // ── 공개 프로퍼티 ────────────────────────────────────────────────

    public bool IsAlive     => _currentHp > 0;
    public int  CurrentHp   => _currentHp;
    public int  MaxHp       => maxHp;
    public int  CurrentMp   => _currentMp;
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
        _currentHp = maxHp;
        _currentMp = maxMp;
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
        _attackCooldown = 0f;
        System.Array.Clear(_skillCooldowns, 0, _skillCooldowns.Length);
#if UNITY_EDITOR
        Debug.Log($"[Combat] 무기 장착: {weapon?.weaponName ?? "없음"}");
#endif
    }

    // ══════════════════════════════════════════════════════════════
    //  매 프레임 처리
    // ══════════════════════════════════════════════════════════════

    private void Update()
    {
        TickCooldowns();

        if (dungeonManager != null && dungeonManager.IsTransitioning) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.spaceKey.wasPressedThisFrame)  TryBasicAttack();
        if (kb.digit1Key.wasPressedThisFrame) TryUseSkill(0);
        if (kb.digit2Key.wasPressedThisFrame) TryUseSkill(1);
        if (kb.digit3Key.wasPressedThisFrame) TryUseSkill(2);
        if (kb.digit4Key.wasPressedThisFrame) TryUseSkill(3);
    }

    private void TickCooldowns()
    {
        float dt = Time.deltaTime;
        _attackCooldown -= dt;
        for (int i = 0; i < _skillCooldowns.Length; i++)
            _skillCooldowns[i] -= dt;
    }

    // ══════════════════════════════════════════════════════════════
    //  기본 공격
    // ══════════════════════════════════════════════════════════════

    private void TryBasicAttack()
    {
        if (_attackCooldown > 0f || currentWeapon == null) return;

        _attackCooldown = currentWeapon.attackCooldown;

        var targets = ResolveTargets(
            currentWeapon.attackPattern,
            currentWeapon.patternRange);

        ExecuteAttack(targets, TotalAttack + currentWeapon.damage);
    }

    // ══════════════════════════════════════════════════════════════
    //  스킬 사용
    // ══════════════════════════════════════════════════════════════

    private void TryUseSkill(int slotIndex)
    {
        if (currentWeapon == null) return;
        if (slotIndex >= currentWeapon.skills.Length) return;

        SkillData skill = currentWeapon.skills[slotIndex];
        if (skill == null)                       return;
        if (_skillCooldowns[slotIndex] > 0f)     return;
        if (_currentMp < skill.mpCost)           return;

        _skillCooldowns[slotIndex] = skill.cooldown;
        SpendMp(skill.mpCost);

        var targets = ResolveTargets(skill.attackPattern, skill.patternRange);
        ExecuteAttack(targets, TotalAttack + skill.damage);

        combatChannel?.RaiseSkillUsed(skill);
#if UNITY_EDITOR
        Debug.Log($"[Combat] 스킬 [{slotIndex + 1}] {skill.skillName} 사용");
#endif
    }

    // ══════════════════════════════════════════════════════════════
    //  공통 헬퍼
    // ══════════════════════════════════════════════════════════════

    private List<Vector2Int> ResolveTargets(AttackPatternType pattern, int range)
    {
        var origin = dungeonManager.WorldToGrid(transform.position);
        var facing = playerMovement != null ? playerMovement.FacingDirection : Vector2Int.down;
        return AttackPattern.GetTargets(pattern, origin, facing, range);
    }

    private void ExecuteAttack(List<Vector2Int> gridPositions, int damage)
    {
        foreach (var gp in gridPositions)
        {
            Vector3 worldPos = dungeonManager.GridToWorld(gp);
            var hits = Physics2D.OverlapCircleAll(worldPos, hitRadius);
            foreach (var col in hits)
            {
                if (col.TryGetComponent<IDamageable>(out var target) &&
                    !ReferenceEquals(target, this))
                {
                    target.TakeDamage(damage);
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  피해 수신 (IDamageable)
    // ══════════════════════════════════════════════════════════════

    public void TakeDamage(int incomingDamage)
    {
        if (!IsAlive) return;

        int actual = Mathf.Max(1, incomingDamage - TotalDefense);
        _currentHp = Mathf.Max(0, _currentHp - actual);
        combatChannel?.RaisePlayerHpChanged(_currentHp, maxHp);
#if UNITY_EDITOR
        Debug.Log($"[Combat] 플레이어 -${actual} HP → {_currentHp}/{maxHp}");
#endif
        if (_currentHp == 0)
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
        _currentMp = Mathf.Max(0, _currentMp - amount);
        combatChannel?.RaisePlayerMpChanged(_currentMp, maxMp);
    }

    public void RestoreMp(int amount)
    {
        _currentMp = Mathf.Min(maxMp, _currentMp + amount);
        combatChannel?.RaisePlayerMpChanged(_currentMp, maxMp);
    }

    public void RestoreHp(int amount)
    {
        _currentHp = Mathf.Min(maxHp, _currentHp + amount);
        combatChannel?.RaisePlayerHpChanged(_currentHp, maxHp);
    }

    // ── 스킬 쿨다운 조회 (UI 표시용) ────────────────────────────────
    public float GetSkillCooldownRemaining(int slotIndex) =>
        slotIndex < _skillCooldowns.Length ? Mathf.Max(0f, _skillCooldowns[slotIndex]) : 0f;

    public float GetSkillCooldownMax(int slotIndex)
    {
        if (currentWeapon == null || slotIndex >= currentWeapon.skills.Length) return 0f;
        return currentWeapon.skills[slotIndex]?.cooldown ?? 0f;
    }
}
