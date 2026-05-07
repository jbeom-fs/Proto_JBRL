using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Executes skill effects.
/// This first version preserves the existing immediate area-damage behavior;
/// later versions can route to projectile, dash, area, buff, or channel handlers.
/// </summary>
public sealed class SkillExecutor
{
    private readonly AttackExecutor _attackExecutor;
    private readonly SkillTargetResolver _targetResolver;
    private readonly ProjectileFireService _projectileFireService;
    private readonly HashSet<SkillExecutionType> _reportedUnsupportedTypes = new();
    private readonly HashSet<SkillData> _reportedMissingProjectilePrefabs = new();

    public SkillExecutor(AttackExecutor attackExecutor)
    {
        _attackExecutor = attackExecutor;
        _targetResolver = new SkillTargetResolver();
        _projectileFireService = new ProjectileFireService();
    }

    public bool Execute(SkillExecutionContext context)
    {
        if (context == null) return false;
        if (context.Skill == null) return false;
        if (context.CasterTransform == null) return false;
        if (_attackExecutor == null) return false;

        switch (context.Skill.executionType)
        {
            case SkillExecutionType.InstantArea:
                return ExecuteInstantArea(context);

            case SkillExecutionType.Projectile:
                return ExecuteProjectile(context);

            case SkillExecutionType.Dash:
                return ExecuteDash(context);

            case SkillExecutionType.AreaOverTime:
            case SkillExecutionType.Buff:
            default:
                ReportUnsupportedExecutionType(context.Skill.executionType);
                return false;
        }
    }

    private bool ExecuteInstantArea(SkillExecutionContext context)
    {
        List<Vector2Int> targets = _targetResolver.ResolveTargets(context);
        _attackExecutor.BeginAttackActivation();
        _attackExecutor.ExecuteAttack(
            targets,
            context.TotalAttack + context.Skill.damage,
            context.Skill.canPenetrateWalls,
            context.Skill.isMultiTarget,
            context.Skill.knockbackForce,
            context.Skill.knockbackDuration,
            context.Skill.slowPercentage,
            context.Skill.slowDuration,
            context.HitRadius);

        return true;
    }

    private bool ExecuteProjectile(SkillExecutionContext context)
    {
        SkillData skill = context.Skill;
        if (skill.projectilePrefab == null)
        {
            ReportMissingProjectilePrefab(skill);
            return false;
        }

        Vector2 direction = ResolveExecutionDirection(context);
        return _projectileFireService.Fire(CreateProjectileFireRequest(context, direction));
    }

    private static bool ExecuteDash(SkillExecutionContext context)
    {
        PlayerDashController dashController = ResolveDashController(context);
        if (dashController == null) return false;

        SkillData skill = context.Skill;
        Vector2 direction = ResolveExecutionDirection(context);
        return dashController.TryStartDash(
            context.CasterCombat,
            direction,
            skill.dashDistance,
            skill.dashDuration,
            skill.dashStopOnWall,
            skill.dashInvincibleDuringDash);
    }

    private static ProjectileFireRequest CreateProjectileFireRequest(SkillExecutionContext context, Vector2 direction)
    {
        SkillData skill = context.Skill;
        return new ProjectileFireRequest
        {
            ProjectilePrefab = skill.projectilePrefab,
            OriginTransform = context.CasterTransform,
            CoroutineRunner = context.CasterCombat,
            Caster = context.CasterCombat,
            Owner = context.CasterCombat,
            Direction = direction,
            Damage = context.TotalAttack + skill.damage,
            Speed = skill.projectileSpeed,
            Lifetime = skill.projectileLifetime,
            ProjectileCount = skill.projectileCount,
            SpreadAngle = skill.projectileSpreadAngle,
            FirePattern = skill.projectileFirePattern,
            WallHitMode = skill.projectileWallHitMode,
            TargetHitMode = skill.projectileTargetHitMode,
            TargetMode = ProjectileController.TargetMode.Enemy,
            MaxBounceCount = skill.projectileMaxBounceCount,
            SpawnOffset = Mathf.Max(0f, skill.projectileSpawnOffset),
            BurstInterval = skill.projectileBurstInterval,
            KnockbackForce = skill.knockbackForce,
            KnockbackDuration = skill.knockbackDuration,
            SlowPercentage = skill.slowPercentage,
            SlowDuration = skill.slowDuration
        };
    }

    private static Vector2 ResolveExecutionDirection(SkillExecutionContext context)
    {
        Vector2 direction = context.AimDirection;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            Vector2Int grid = context.GridAimDirection;
            direction = new Vector2(grid.x, grid.y);
        }

        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector2.down;

        return direction.normalized;
    }

    private static PlayerDashController ResolveDashController(SkillExecutionContext context)
    {
        if (context.CasterCombat == null) return null;

        PlayerDashController dashController = context.CasterCombat.GetComponent<PlayerDashController>();
        if (dashController == null)
            dashController = context.CasterCombat.gameObject.AddComponent<PlayerDashController>();

        return dashController;
    }

    private void ReportUnsupportedExecutionType(SkillExecutionType executionType)
    {
#if UNITY_EDITOR
        if (_reportedUnsupportedTypes.Add(executionType))
            Debug.LogWarning($"[SkillExecutor] Skill execution type is not implemented yet: {executionType}");
#endif
    }

    private void ReportMissingProjectilePrefab(SkillData skill)
    {
#if UNITY_EDITOR
        if (skill != null && _reportedMissingProjectilePrefabs.Add(skill))
            Debug.LogWarning($"[SkillExecutor] Projectile skill is missing projectilePrefab: {skill.skillName}");
#endif
    }
}
