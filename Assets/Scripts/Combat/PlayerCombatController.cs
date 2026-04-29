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

    // NonAlloc 버퍼 — 광역 스킬 후보를 한 번에 담기 위해 넉넉하게 잡고 프레임 간 재사용합니다.
    private static readonly Collider2D[] s_HitBuffer = new Collider2D[128];

    // ── 런타임 상태 ─────────────────────────────────────────────────

    private int     _currentHp;
    private int     _currentMp;
    private float   _attackCooldown;
    private readonly float[] _skillCooldowns = new float[4];
    private readonly HashSet<IDamageable> _hitTargetsThisAttack = new();
    private readonly HashSet<Vector2Int> _targetGridSet = new();
    private readonly List<HitCandidate> _hitCandidates = new();
    private bool    _isAttackAlreadyProcessed;
    private PlayerInputReader _inputReader;

    private struct HitCandidate
    {
        public IDamageable Target;
        public float SqrDistance;
    }

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

        if (DungeonManager.Instance != null && DungeonManager.Instance.IsTransitioning) return;

        if (_inputReader == null) return;

        if (_inputReader.WasBasicAttackPressed)  TryBasicAttack();
        if (_inputReader.WasSkillPressed(0)) TryUseSkill(0);
        if (_inputReader.WasSkillPressed(1)) TryUseSkill(1);
        if (_inputReader.WasSkillPressed(2)) TryUseSkill(2);
        if (_inputReader.WasSkillPressed(3)) TryUseSkill(3);
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
        BeginAttackActivation();

        var targets = ResolveTargets(
            currentWeapon.attackPattern,
            currentWeapon.patternRange);

        ExecuteAttack(
            targets,
            TotalAttack + currentWeapon.damage,
            currentWeapon.canPenetrateWalls,
            false,
            currentWeapon.knockbackForce,
            currentWeapon.knockbackDuration,
            currentWeapon.slowPercentage,
            currentWeapon.slowDuration);
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
        BeginAttackActivation();

        var targets = ResolveTargets(skill.attackPattern, skill.patternRange, skill.coneHalfAngle);
        ExecuteAttack(
            targets,
            TotalAttack + skill.damage,
            skill.canPenetrateWalls,
            skill.isMultiTarget,
            skill.knockbackForce,
            skill.knockbackDuration,
            skill.slowPercentage,
            skill.slowDuration);

        combatChannel?.RaiseSkillUsed(skill);
#if UNITY_EDITOR
        Debug.Log($"[Combat] 스킬 [{slotIndex + 1}] {skill.skillName} 사용");
#endif
    }

    // ══════════════════════════════════════════════════════════════
    //  공통 헬퍼
    // ══════════════════════════════════════════════════════════════

    private void BeginAttackActivation()
    {
        // 공격 1회가 새로 시작될 때만 상태 가드를 초기화한다.
        // 같은 공격 판정이 물리/범위 루프에서 다시 들어와도 중복 피해를 막기 위한 준비 단계다.
        _isAttackAlreadyProcessed = false;
        _hitTargetsThisAttack.Clear();
        _targetGridSet.Clear();
        _hitCandidates.Clear();
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
        var gridFacing   = new Vector2Int(screenFacing.x, -screenFacing.y);

        return AttackPattern.GetTargets(pattern, origin, gridFacing, range, coneHalfAngle);
    }

    private void ExecuteAttack(
        List<Vector2Int> gridPositions,
        int damage,
        bool canPenetrateWalls,
        bool isMultiTarget,
        float knockbackForce,
        float knockbackDuration,
        float slowPercentage,
        float slowDuration)
    {
        // 이미 처리된 공격 활성화라면 즉시 종료한다.
        // 다음 기본 공격/스킬이 시작될 때 BeginAttackActivation()에서만 false로 되돌린다.
        if (_isAttackAlreadyProcessed) return;

        var dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null) return;

        // 벽 체크용 그리드 좌표 (DungeonData 조회에 사용)
        Vector2Int attackerGrid = dungeonManager.WorldToGrid(transform.position);
        _hitCandidates.Clear();
        _targetGridSet.Clear();

        float queryRadius = BuildTargetGridSetAndRadius(gridPositions, dungeonManager);
        if (_targetGridSet.Count == 0) return;

        // 기존처럼 타일마다 물리 쿼리를 반복하지 않고, 공격당 한 번만 넓게 후보를 수집한 뒤 그리드로 필터링합니다.
        int count = Physics2D.OverlapCircleNonAlloc(transform.position, queryRadius + hitRadius, s_HitBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = s_HitBuffer[i];
            if (!col.TryGetComponent<IDamageable>(out var target)) continue;
            if (ReferenceEquals(target, this)) continue;
            if (!target.IsAlive) continue;

            Vector2Int targetGrid = dungeonManager.WorldToGrid(col.bounds.center);
            if (!_targetGridSet.Contains(targetGrid)) continue;

            // 2단계: 벽 관통 여부에 따라 지형 체크
            if (!canPenetrateWalls)
            {
                // 적의 실제 위치를 그리드 좌표로 변환하여 DungeonData 로 경로 확인.
                // 하나의 Tilemap 에 벽·바닥이 혼재하므로 Physics2D.Linecast 대신
                // 배열 조회(O(1))를 사용한다. 별도 Wall 레이어 설정 불필요.
                if (HasWallBetween(attackerGrid, targetGrid)) continue;
            }
            // canPenetrateWalls = true: 지형 검사를 완전히 건너뜁니다.
            // 벽 너머 적도 포함, 범위 안의 모든 대상에게 피해를 줍니다.

            if (!_hitTargetsThisAttack.Add(target)) continue;

            _hitCandidates.Add(new HitCandidate
            {
                Target = target,
                SqrDistance = ((Vector2)col.bounds.center - (Vector2)transform.position).sqrMagnitude
            });
        }

        if (_hitCandidates.Count == 0) return;

        // 멀티타겟이면 감지된 모든 대상에게 1회씩 피해를 주고, 아니면 가장 가까운 대상 1명만 처리한다.
        if (isMultiTarget)
        {
            for (int i = 0; i < _hitCandidates.Count; i++)
                ApplyDamageAndStatus(_hitCandidates[i].Target, damage, knockbackForce, knockbackDuration, slowPercentage, slowDuration);
        }
        else
        {
            HitCandidate closest = _hitCandidates[0];
            for (int i = 1; i < _hitCandidates.Count; i++)
                if (_hitCandidates[i].SqrDistance < closest.SqrDistance)
                    closest = _hitCandidates[i];

            ApplyDamageAndStatus(closest.Target, damage, knockbackForce, knockbackDuration, slowPercentage, slowDuration);
        }

        // 이번 공격 활성화는 처리 완료입니다. 다음 공격 시작 시에만 BeginAttackActivation()에서 해제됩니다.
        _isAttackAlreadyProcessed = true;
    }

    private void ApplyDamageAndStatus(
        IDamageable target,
        int damage,
        float knockbackForce,
        float knockbackDuration,
        float slowPercentage,
        float slowDuration)
    {
        if (target == null || !target.IsAlive) return;

        // 적 컨트롤러라면 데미지와 함께 넉백/슬로우를 전달하고, 그 외 대상은 기존 데미지 처리만 유지합니다.
        if (target is EnemyController enemy)
        {
            enemy.ApplyCombatImpact(
                damage,
                transform.position,
                knockbackForce,
                knockbackDuration,
                slowPercentage,
                slowDuration);
            return;
        }

        target.TakeDamage(damage);
    }

    private float BuildTargetGridSetAndRadius(List<Vector2Int> gridPositions, DungeonManager dungeonManager)
    {
        float maxSqrDistance = 0f;
        Vector2 origin = transform.position;

        for (int i = 0; i < gridPositions.Count; i++)
        {
            Vector2Int grid = gridPositions[i];
            if (!_targetGridSet.Add(grid)) continue;

            Vector2 world = dungeonManager.GridToWorld(grid);
            float sqrDistance = (world - origin).sqrMagnitude;
            if (sqrDistance > maxSqrDistance)
                maxSqrDistance = sqrDistance;
        }

        return Mathf.Sqrt(maxSqrDistance);
    }

    /// <summary>
    /// 공격자 그리드 → 적 그리드 사이의 직선 경로에 벽 타일이 있는지 검사합니다.
    /// 그리드 칸 단위 체크이므로 Physics2D 레이어 설정 없이 동작합니다.
    /// </summary>
    private bool HasWallBetween(Vector2Int from, Vector2Int to)
    {
        DungeonData data = DungeonManager.Instance != null ? DungeonManager.Instance.Data : null;
        if (data == null) return false;

        int dx    = to.x - from.x;
        int dy    = to.y - from.y;
        int steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        if (steps == 0) return false;

        // i=1: 공격자 칸은 항상 걷기 가능 → 중간 칸부터 적 칸까지 검사
        for (int i = 1; i <= steps; i++)
        {
            float t   = (float)i / steps;
            int   col = Mathf.RoundToInt(from.x + dx * t);
            int   row = Mathf.RoundToInt(from.y + dy * t);
            if (!data.IsWalkable(col, row)) return true;  // 벽 발견
        }
        return false;
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
