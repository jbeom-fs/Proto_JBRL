using System.Collections.Generic;
using UnityEngine;

public readonly struct SkillExecutionResult
{
    public SkillExecutionResult(bool success, int resourceConsumed)
    {
        Success = success;
        ResourceConsumed = Mathf.Max(0, resourceConsumed);
    }

    public bool Success { get; }
    public int ResourceConsumed { get; }

    public static SkillExecutionResult Failure => new(false, 0);
    public static SkillExecutionResult SuccessWithCost(int resourceConsumed) => new(true, resourceConsumed);
}

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
    private readonly HashSet<PlayerCombatController> _reportedMissingDashControllers = new();

    public SkillExecutor(AttackExecutor attackExecutor)
    {
        _attackExecutor = attackExecutor;
        _targetResolver = new SkillTargetResolver();
        _projectileFireService = new ProjectileFireService();
    }

    public SkillExecutionResult Execute(SkillExecutionContext context)
    {
        if (context == null) return SkillExecutionResult.Failure;
        if (context.Skill == null) return SkillExecutionResult.Failure;
        if (context.CasterTransform == null) return SkillExecutionResult.Failure;
        if (_attackExecutor == null) return SkillExecutionResult.Failure;

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
                return SkillExecutionResult.Failure;
        }
    }

    public bool ExecuteBasicProjectile(SkillExecutionContext context, int projectileCount)
    {
        return ExecuteProjectile(context, Mathf.Max(1, projectileCount), 0).Success;
    }

    private SkillExecutionResult ExecuteInstantArea(SkillExecutionContext context)
    {
        List<Vector3> targets = _targetResolver.ResolveWorldTargets(context);
        _attackExecutor.BeginAttackActivation();
        _attackExecutor.ExecuteAttackWorld(
            targets,
            context.TotalAttack + context.Skill.damage,
            context.Skill.canPenetrateWalls,
            context.Skill.isMultiTarget,
            context.Skill.knockbackForce,
            context.Skill.knockbackDuration,
            context.Skill.slowPercentage,
            context.Skill.slowDuration,
            context.HitRadius);

        PlayConfiguredAnimation(context, context.Skill, ResolveExecutionDirection(context));
        return SkillExecutionResult.SuccessWithCost(context.Skill.consumeAmount);
    }

    private SkillExecutionResult ExecuteProjectile(SkillExecutionContext context)
    {
        SkillData skill = context.Skill;
        int projectileCount = SkillProjectileUtility.GetEffectiveProjectileCount(skill);
        int resourceConsumed = Mathf.Max(0, skill.consumeAmount);

        if (SkillProjectileUtility.AllowsPartialBulletUse(skill))
        {
            int available = context.CasterCombat != null ? context.CasterCombat.CurrentBullet : 0;
            int required = Mathf.Max(0, skill.requiredAmount);
            if (available < required)
                return SkillExecutionResult.Failure;

            projectileCount = Mathf.Min(projectileCount, available);
            resourceConsumed = projectileCount;
            if (projectileCount <= 0)
                return SkillExecutionResult.Failure;
        }

        return ExecuteProjectile(context, projectileCount, resourceConsumed);
    }

    private SkillExecutionResult ExecuteProjectile(SkillExecutionContext context, int projectileCount, int resourceConsumed)
    {
        SkillData skill = context.Skill;
        if (skill.projectilePrefab == null)
        {
            ReportMissingProjectilePrefab(skill);
            return SkillExecutionResult.Failure;
        }

        Vector2 direction = ResolveExecutionDirection(context);
        if (!_projectileFireService.Fire(CreateProjectileFireRequest(context, direction, projectileCount)))
            return SkillExecutionResult.Failure;

        PlayConfiguredAnimation(context, skill, direction);
        return SkillExecutionResult.SuccessWithCost(resourceConsumed);
    }

    private SkillExecutionResult ExecuteDash(SkillExecutionContext context)
    {
        PlayerDashController dashController = context.CasterDash;
        if (dashController == null)
        {
            ReportMissingDashController(context.CasterCombat);
            return SkillExecutionResult.Failure;
        }

        SkillData skill = context.Skill;
        Vector2 direction = ResolveExecutionDirection(context);
        bool invincibleDuringDash = skill.dashInvincibleDuringDash;
        bool success = dashController.TryStartDash(
            context.CasterCombat,
            direction,
            skill.dashDistance,
            skill.dashDuration,
            skill.dashStopOnWall,
            invincibleDuringDash,
            CreateDashDamageRequest(context),
            skill);
        return success ? SkillExecutionResult.SuccessWithCost(skill.consumeAmount) : SkillExecutionResult.Failure;
    }

    private static void PlayConfiguredAnimation(SkillExecutionContext context, SkillData skill, Vector2 direction)
    {
        if (context.CasterForm == null || skill == null)
            return;

        context.CasterForm.PlaySkillAnimation(skill, direction);
    }

    private static DashDamageRequest CreateDashDamageRequest(SkillExecutionContext context)
    {
        SkillData skill = context.Skill;
        return new DashDamageRequest
        {
            DamageOnPath = skill.dashDamageOnPath,
            DamageOnContact = skill.dashDamageOnContact,
            Damage = context.TotalAttack + skill.damage,
            HitRadius = context.HitRadius,
            KnockbackForce = skill.knockbackForce,
            KnockbackDuration = skill.knockbackDuration,
            SlowPercentage = skill.slowPercentage,
            SlowDuration = skill.slowDuration
        };
    }

    private static ProjectileFireRequest CreateProjectileFireRequest(SkillExecutionContext context, Vector2 direction, int projectileCount)
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
            ProjectileCount = Mathf.Max(1, projectileCount),
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

    private void ReportMissingDashController(PlayerCombatController caster)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (caster != null && _reportedMissingDashControllers.Add(caster))
            Debug.LogWarning(
                $"[SkillExecutor] Dash 스킬을 실행할 PlayerDashController가 없습니다 (caster: {caster.name}).",
                caster);
#endif
    }
}
