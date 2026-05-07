using UnityEngine;

/// <summary>
/// 스킬 데이터 — WeaponData.skills[] 슬롯에 할당합니다.
/// Assets > Create > JBLogLike > Combat > Skill 로 생성합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewSkill", menuName = "JBLogLike/Combat/Skill")]
public class SkillData : ScriptableObject
{
    [Header("기본 정보")]
    public string skillName = "새 스킬";
    [TextArea(2, 4)]
    public string description;
    public Sprite icon;

    [Header("Execution")]
    [Tooltip("How this skill is executed. Existing skills should use InstantArea.")]
    public SkillExecutionType executionType = SkillExecutionType.InstantArea;

    [Header("Projectile")]
    public GameObject projectilePrefab;
    [Min(0.01f)] public float projectileSpeed = 8f;
    [Min(0.01f)] public float projectileLifetime = 3f;
    [Min(1)] public int projectileCount = 1;
    [Min(0f)] public float projectileSpreadAngle = 15f;
    public ProjectileFirePattern projectileFirePattern = ProjectileFirePattern.Single;
    public ProjectileWallHitMode projectileWallHitMode = ProjectileWallHitMode.Destroy;
    public ProjectileTargetHitMode projectileTargetHitMode = ProjectileTargetHitMode.DestroyOnHit;
    [Min(0)] public int projectileMaxBounceCount = 1;
    [Min(0f)] public float projectileSpawnOffset = 0.35f;
    [Min(0f)] public float projectileBurstInterval = 0.1f;
    [Min(0f)] public float projectileBurstSpacing = 0.12f;

    [Header("Dash")]
    [Tooltip("World-space distance the caster tries to dash.")]
    [Min(0f)] public float dashDistance = 3f;
    [Tooltip("Seconds used to interpolate the dash movement.")]
    [Min(0f)] public float dashDuration = 0.12f;
    [Tooltip("If true, dash stops at the last walkable point before a blocked tile. If false, blocked paths fail.")]
    public bool dashStopOnWall = true;
    [Tooltip("Reserved for a later phase: damage enemies along the dash path.")]
    public bool dashDamageOnPath = false;
    [Tooltip("Reserved for a later phase: damage enemies touched by the dash.")]
    public bool dashDamageOnContact = false;
    [Tooltip("Reserved for a later phase: ignore incoming damage during the dash.")]
    public bool dashInvincibleDuringDash = false;

    [Header("전투")]
    public int               damage        = 10;
    public AttackPatternType attackPattern = AttackPatternType.Cross;
    [Tooltip("모든 패턴의 사정거리(칸). Line=직선N칸, Cross/Diagonal=각방향N칸, Circle=체비쇼프N, Cone=부채꼴N칸, Single=N칸 거리 1타겟")]
    public int               patternRange  = 1;
    public bool              isMultiTarget = false;

    [Header("Hit Effects")]
    public float knockbackForce = 0f;
    public float knockbackDuration = 0f;
    [Range(0f, 1f)]
    public float slowPercentage = 0f;
    public float slowDuration = 0f;

    [Header("비용 및 쿨다운")]
    public int   mpCost   = 3;
    public float cooldown = 2f;

    [Header("벽 관통")]
    [Tooltip("false: 벽에 막힘 / true: 벽을 무시하고 유닛에게 피해")]
    public bool canPenetrateWalls = false;

    [Header("시각화 (SkillRangePreviewer)")]
    [Tooltip("Cone 패턴의 반각도 (°). 기본 45 → 전체 90° 부채꼴.\n현재 Cone 패턴은 정면+좌우45° 3칸이므로 45가 정확합니다.")]
    [Range(1f, 179f)]
    public float coneHalfAngle = 45f;
}
