using System;
using System.Collections.Generic;
using UnityEngine;

public enum SkillAnimationType
{
    None = 0,
    Attack = 1,
    Spin = 2,
    Dash = 3,
    CustomTrigger = 4
}

public enum SkillResourceType
{
    None = 0,
    Bullet = 1,
    ParryStack = 2
}

public enum BulletShortageMode
{
    RequireFullCost = 0,
    AllowPartialUse = 1
}

[Serializable]
public class HitStep
{
    public float delay;
    public int damagePct = 100;
    public List<Vector2Int> overrideCells = new();
}

/// <summary>
/// Skill data assigned from WeaponData.skills[].
/// Create from Assets > Create > JBRogLike > Combat > Skill.
/// </summary>
[CreateAssetMenu(fileName = "NewSkill", menuName = "JBRogLike/Combat/Skill")]
public class SkillData : ScriptableObject
{
    [Header("Basic Info")]
    [Tooltip("Display name shown for this skill.")]
    public string skillName = "새 스킬";

    [Tooltip("Icon used by skill UI.")]
    public Sprite icon;

    [Tooltip("Designer-facing description for UI and balancing notes.")]
    [TextArea(2, 4)]
    public string description;

    [Tooltip("Execution route used by SkillExecutor.")]
    public SkillExecutionType executionType = SkillExecutionType.InstantArea;

    [Header("Resource")]
    [Tooltip("Resource consumed when the skill succeeds. None ignores resource checks.")]
    public SkillResourceType resourceType = SkillResourceType.None;

    [Tooltip("Minimum resource required to cast this skill.")]
    [Min(0)]
    public int requiredAmount = 0;

    [Tooltip("Resource consumed after successful execution.")]
    [Min(0)]
    public int consumeAmount = 0;

    [Tooltip("Bullet skills may require the full cost or fire as many shots as remaining bullets allow.")]
    public BulletShortageMode bulletShortageMode = BulletShortageMode.RequireFullCost;

    [Tooltip("Bullet amount restored after this skill succeeds.")]
    [Min(0)]
    public int reloadAmount = 0;

    [Tooltip("Cooldown in seconds after the skill is cast.")]
    [Min(0f)]
    public float cooldown = 2f;

    [Tooltip("Delay in seconds before the skill effect executes after input.")]
    [Min(0f)]
    public float castDelay = 0f;

    [Tooltip("Delay in seconds after a successful skill execution before attacks or skills can be used again.")]
    [Min(0f)]
    public float recoveryDelay = 0f;

    [Tooltip("Root skill only. Follow-up skills triggered by repeated input on the same slot; the first recast uses element 0.")]
    public List<SkillData> recastStages = new();

    [Tooltip("Root skill only. Time after the root or a successful recast stage during which the next stage can be triggered.")]
    [Min(0f)]
    public float recastWindow = 1.5f;

    [Space(8)]
    [Header("Animation")]
    [SerializeField] private SkillAnimationType animationType = SkillAnimationType.None;
    [SerializeField] private string customAnimationTrigger;
    [SerializeField] private bool rotateAnimationByDirection;
    [SerializeField] private float animationBaseAngle;

    [Tooltip("Base damage shared by InstantArea, Projectile, Dash, and AreaOverTime where applicable.")]
    [Min(0)]
    public int damage = 10;

    [Space(8)]
    [Header("Target / InstantArea")]
    [Tooltip("Target shape used by InstantArea and some preview/target calculations.")]
    public AttackPatternType attackPattern = AttackPatternType.Cross;

    [Tooltip("Target range in tiles. Used by InstantArea and some preview/target calculations.")]
    [Min(1)]
    public int patternRange = 1;

    [Tooltip("Half angle in degrees for Cone targeting. Used by InstantArea and SkillRangePreviewer.")]
    [Range(1f, 179f)]
    public float coneHalfAngle = 45f;

    [Tooltip("Custom 패턴 전용. 시전자 기준 상대 셀(위=전방). 실행 시 조준 방향으로 회전.")]
    public List<Vector2Int> customCells = new List<Vector2Int>();

    [Tooltip("Follow-up hits after the base hit. Empty means a single base hit. Each step delay is measured from the previous hit.")]
    public List<HitStep> hitSteps = new();

    [Tooltip("이 스킬의 후딜(recovery) 및 진행 중 멀티히트 후속타를 다른 스킬의 성공 시전이 끊을 수 있게 함. 남은 후속타는 폐기되며 코스트·쿨다운은 환불되지 않음. false면 후딜 커밋(끊기 불가).")]
    public bool cancelable = false;

    [Tooltip("Allows InstantArea targeting to affect multiple resolved targets.")]
    public bool isMultiTarget = false;

    [Tooltip("If true, target and preview checks may ignore wall blocking where supported.")]
    public bool canPenetrateWalls = false;

    [Tooltip("Knockback force applied by InstantArea hit effects.")]
    [Min(0f)]
    public float knockbackForce = 0f;

    [Tooltip("Knockback duration in seconds for InstantArea hit effects.")]
    [Min(0f)]
    public float knockbackDuration = 0f;

    [Tooltip("Slow ratio applied by InstantArea hit effects. 0 means no slow, 1 means fully slowed.")]
    [Range(0f, 1f)]
    public float slowPercentage = 0f;

    [Tooltip("Slow duration in seconds for InstantArea hit effects.")]
    [Min(0f)]
    public float slowDuration = 0f;

    [Tooltip("DoT applications applied on hit. Empty means no ailment.")]
    public AilmentApplication[] ailments;

    [Space(8)]
    [Header("Zone")]
    [Tooltip("Sprite displayed by AreaOverTime zones. Null creates an invisible logical zone.")]
    public Sprite zoneSprite;

    [Tooltip("World-space radius used by AreaOverTime zone detection and visual sizing.")]
    [Min(0f)]
    public float zoneRadius = 1f;

    [Tooltip("Seconds between AreaOverTime zone ticks.")]
    [Min(0.01f)]
    public float zoneTickInterval = 0.5f;

    [Tooltip("AreaOverTime zone lifetime in seconds.")]
    [Min(0f)]
    public float zoneDuration = 3f;

    [Space(8)]
    [Header("Projectile")]
    [Tooltip("Projectile prefab spawned when executionType is Projectile.")]
    public GameObject projectilePrefab;

    [Tooltip("Projectile movement speed used when executionType is Projectile.")]
    [Min(0.01f)]
    public float projectileSpeed = 8f;

    [Tooltip("Projectile lifetime in seconds used when executionType is Projectile.")]
    [Min(0.01f)]
    public float projectileLifetime = 3f;

    [Tooltip("Number of projectiles to fire. Values above 1 are meaningful for Burst, Spread, or Circle patterns.")]
    [Min(1)]
    public int projectileCount = 1;

    [Tooltip("Angle between projectiles for Spread patterns when executionType is Projectile.")]
    [Min(0f)]
    public float projectileSpreadAngle = 15f;

    [Tooltip("Fire pattern used when executionType is Projectile.")]
    public ProjectileFirePattern projectileFirePattern = ProjectileFirePattern.Single;

    [Tooltip("Wall collision behavior used when executionType is Projectile.")]
    public ProjectileWallHitMode projectileWallHitMode = ProjectileWallHitMode.Destroy;

    [Tooltip("Target collision behavior used when executionType is Projectile.")]
    public ProjectileTargetHitMode projectileTargetHitMode = ProjectileTargetHitMode.DestroyOnHit;

    [Tooltip("Maximum bounce count when projectileWallHitMode is Bounce.")]
    [Min(0)]
    public int projectileMaxBounceCount = 1;

    [Tooltip("Distance from the caster where the projectile spawns when executionType is Projectile.")]
    [Min(0f)]
    public float projectileSpawnOffset = 0.35f;

    [Tooltip("Delay between shots when projectileFirePattern is Burst and projectileCount is greater than 1.")]
    [Min(0f)]
    public float projectileBurstInterval = 0.1f;

    [Tooltip("Forward spacing between burst projectiles when projectileFirePattern is Burst and projectileCount is greater than 1.")]
    [Min(0f)]
    public float projectileBurstSpacing = 0.12f;

    [Space(8)]
    [Header("Dash")]
    [Tooltip("World-space distance the caster tries to dash when executionType is Dash.")]
    [Min(0f)]
    public float dashDistance = 3f;

    [Tooltip("Seconds used to interpolate the dash movement when executionType is Dash.")]
    [Min(0f)]
    public float dashDuration = 0.12f;

    [Tooltip("If true, Dash stops at the last walkable point before a blocked tile. If false, blocked paths fail.")]
    public bool dashStopOnWall = true;

    [Tooltip("Damage enemies along the Dash path. First-pass implementation shares the same detection path as dashDamageOnContact; it can be split later.")]
    public bool dashDamageOnPath = false;

    [Tooltip("Damage enemies touched by Dash contact. First-pass implementation shares the same detection path as dashDamageOnPath; it can be split later.")]
    public bool dashDamageOnContact = false;

    [Tooltip("Ignore incoming damage while executionType is Dash.")]
    public bool dashInvincibleDuringDash = false;

    [Space(8)]
    [Header("Dagger Marker")]
    [Tooltip("Projectile, Blink, or buffed basic-attack hits apply a Dagger marker.")]
    public bool appliesDaggerMarker = false;

    [Tooltip("Dash hits detonate Dagger markers.")]
    public bool detonatesDaggerMarker = false;

    [Tooltip("Extra damage dealt when a Dagger marker detonates. 0 reuses damage.")]
    [Min(0)]
    public int markerDetonationDamage = 0;

    [Tooltip("Reset this skill cooldown once when this cast detonates at least one marker.")]
    public bool resetCooldownOnMarkerDetonate = false;

    [Tooltip("Marker lifetime in seconds. 0 uses registry default.")]
    [Min(0f)]
    public float markerDuration = 5f;

    [Space(8)]
    [Header("Blink")]
    [Tooltip("World-space offset from the target, away from caster, for Blink execution.")]
    [Min(0f)]
    public float blinkBehindOffset = 0.75f;

    public SkillAnimationType AnimationType => animationType;
    public string CustomAnimationTrigger => customAnimationTrigger;
    public bool RotateAnimationByDirection => rotateAnimationByDirection;
    public float AnimationBaseAngle => animationBaseAngle;
}
