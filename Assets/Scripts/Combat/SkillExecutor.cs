using System.Collections;
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
    private readonly HashSet<SkillExecutionType> _reportedUnsupportedTypes = new();
    private readonly HashSet<SkillData> _reportedMissingProjectilePrefabs = new();

    private sealed class ProjectileFireRequest
    {
        public GameObject Prefab;
        public Transform CasterTransform;
        public PlayerCombatController CasterCombat;
        public Vector2 Direction;
        public int Damage;
        public float Speed;
        public float Lifetime;
        public ProjectileWallHitMode WallHitMode;
        public ProjectileTargetHitMode TargetHitMode;
        public int MaxBounceCount;
        public float SpawnOffset;
        public float KnockbackForce;
        public float KnockbackDuration;
        public float SlowPercentage;
        public float SlowDuration;
    }

    public SkillExecutor(AttackExecutor attackExecutor)
    {
        _attackExecutor = attackExecutor;
        _targetResolver = new SkillTargetResolver();
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

        Vector2 direction = ResolveProjectileDirection(context);
        int count = GetProjectileRequestCount(skill);
        switch (skill.projectileFirePattern)
        {
            case ProjectileFirePattern.Single:
                return SpawnProjectile(context, direction);

            case ProjectileFirePattern.Spread:
                return FireSpread(context, direction, count);

            case ProjectileFirePattern.Circle:
                return FireCircle(context, direction, count);

            case ProjectileFirePattern.Burst:
                return StartBurst(context, direction, count);

            default:
                return SpawnProjectile(context, direction);
        }
    }

    private bool FireSpread(SkillExecutionContext context, Vector2 direction, int count)
    {
        if (count <= 1)
            return SpawnProjectile(context, direction);

        bool spawnedAny = false;
        float spread = Mathf.Max(0f, context.Skill.projectileSpreadAngle);
        float startAngle = -spread * 0.5f;
        float step = spread / (count - 1);
        for (int i = 0; i < count; i++)
            spawnedAny |= SpawnProjectile(context, Rotate(direction, startAngle + step * i));
        return spawnedAny;
    }

    private bool FireCircle(SkillExecutionContext context, Vector2 direction, int count)
    {
        if (count <= 1)
            return SpawnProjectile(context, direction);

        bool spawnedAny = false;
        float step = 360f / count;
        for (int i = 0; i < count; i++)
            spawnedAny |= SpawnProjectile(context, Rotate(direction, step * i));
        return spawnedAny;
    }

    private bool StartBurst(SkillExecutionContext context, Vector2 direction, int count)
    {
        ProjectileFireRequest request = CreateProjectileFireRequest(context, direction);
        if (!SpawnProjectile(request))
            return false;

        int remainingCount = Mathf.Max(1, count) - 1;
        if (remainingCount > 0)
            context.CasterCombat.StartCoroutine(FireBurstRoutine(request, remainingCount, context.Skill.projectileBurstInterval));

        return true;
    }

    private bool SpawnProjectile(SkillExecutionContext context, Vector2 direction)
    {
        return SpawnProjectile(context, direction, Vector2.zero);
    }

    private bool SpawnProjectile(SkillExecutionContext context, Vector2 direction, Vector2 lateralOffset)
    {
        return SpawnProjectile(CreateProjectileFireRequest(context, direction), lateralOffset);
    }

    private bool SpawnProjectile(ProjectileFireRequest request)
    {
        return SpawnProjectile(request, Vector2.zero);
    }

    private bool SpawnProjectile(ProjectileFireRequest request, Vector2 lateralOffset)
    {
        if (!CanContinueProjectileFire(request))
            return false;

        Vector3 casterPosition = request.CasterTransform.position;
        Vector3 spawnPosition = casterPosition
            + (Vector3)(request.Direction * request.SpawnOffset)
            + (Vector3)lateralOffset;
        ProjectileController projectile = ProjectilePool.Instance.Get(
            request.Prefab,
            spawnPosition,
            Quaternion.identity);
        if (projectile == null) return false;

        projectile.Initialize(
            request.Direction,
            request.Damage,
            request.Speed,
            request.Lifetime,
            request.WallHitMode,
            request.MaxBounceCount,
            request.CasterCombat,
            ProjectileController.TargetMode.Enemy,
            request.TargetHitMode,
            request.KnockbackForce,
            request.KnockbackDuration,
            request.SlowPercentage,
            request.SlowDuration);
        return true;
    }

    private static ProjectileFireRequest CreateProjectileFireRequest(SkillExecutionContext context, Vector2 direction)
    {
        SkillData skill = context.Skill;
        return new ProjectileFireRequest
        {
            Prefab = skill.projectilePrefab,
            CasterTransform = context.CasterTransform,
            CasterCombat = context.CasterCombat,
            Direction = direction,
            Damage = context.TotalAttack + skill.damage,
            Speed = skill.projectileSpeed,
            Lifetime = skill.projectileLifetime,
            WallHitMode = skill.projectileWallHitMode,
            TargetHitMode = skill.projectileTargetHitMode,
            MaxBounceCount = skill.projectileMaxBounceCount,
            SpawnOffset = Mathf.Max(0f, skill.projectileSpawnOffset),
            KnockbackForce = skill.knockbackForce,
            KnockbackDuration = skill.knockbackDuration,
            SlowPercentage = skill.slowPercentage,
            SlowDuration = skill.slowDuration
        };
    }

    private static IEnumerator FireBurstRoutine(ProjectileFireRequest request, int remainingCount, float interval)
    {
        WaitForSeconds wait = interval > 0f ? new WaitForSeconds(interval) : null;
        for (int i = 0; i < remainingCount; i++)
        {
            if (wait != null)
                yield return wait;
            else
                yield return null;

            if (!CanContinueProjectileFire(request))
                yield break;

            SpawnProjectileStatic(request);
        }
    }

    private static bool SpawnProjectileStatic(ProjectileFireRequest request)
    {
        if (!CanContinueProjectileFire(request))
            return false;

        Vector3 spawnPosition = request.CasterTransform.position
            + (Vector3)(request.Direction * request.SpawnOffset);
        ProjectileController projectile = ProjectilePool.Instance.Get(
            request.Prefab,
            spawnPosition,
            Quaternion.identity);
        if (projectile == null) return false;

        projectile.Initialize(
            request.Direction,
            request.Damage,
            request.Speed,
            request.Lifetime,
            request.WallHitMode,
            request.MaxBounceCount,
            request.CasterCombat,
            ProjectileController.TargetMode.Enemy,
            request.TargetHitMode,
            request.KnockbackForce,
            request.KnockbackDuration,
            request.SlowPercentage,
            request.SlowDuration);
        return true;
    }

    private static bool CanContinueProjectileFire(ProjectileFireRequest request)
    {
        return request != null
            && request.Prefab != null
            && request.CasterTransform != null
            && request.CasterTransform.gameObject.activeInHierarchy
            && request.CasterCombat != null
            && request.CasterCombat.isActiveAndEnabled
            && !request.CasterCombat.IsDead;
    }

    private static int GetProjectileRequestCount(SkillData skill)
    {
        switch (skill.projectileFirePattern)
        {
            case ProjectileFirePattern.Burst:
            case ProjectileFirePattern.Spread:
            case ProjectileFirePattern.Circle:
                return Mathf.Max(1, skill.projectileCount);

            default:
                return 1;
        }
    }

    private static Vector2 ResolveProjectileDirection(SkillExecutionContext context)
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

    private static Vector2 Rotate(Vector2 direction, float degrees)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector2.down;

        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(
            direction.x * cos - direction.y * sin,
            direction.x * sin + direction.y * cos).normalized;
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
