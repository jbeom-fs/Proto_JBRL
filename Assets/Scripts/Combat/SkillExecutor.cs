using System;
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

public delegate void SkillInstantAreaHitHandler(
    IReadOnlyList<Vector3> worldTargets,
    CustomShapeMatcher? customShape,
    float cellSize);

/// <summary>
/// Executes skill effects.
/// This first version preserves the existing immediate area-damage behavior;
/// later versions can route to projectile, dash, area, buff, or channel handlers.
/// </summary>
public sealed class SkillExecutor
{
    private const int BlinkEnemyBufferSize = 64;
    private const float BlinkCurrentPositionEpsilon = 0.5f;
    private static readonly float[] s_BlinkLandingAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };

    private readonly AttackExecutor _attackExecutor;
    private readonly SkillTargetResolver _targetResolver;
    private readonly ProjectileFireService _projectileFireService;
    private readonly Action<SkillExecutionContext, HitStep> _executeMultiHitStep;
    private readonly MultiHitSkillRunner _multiHitRunner;
    private readonly SkillInstantAreaHitHandler _onInstantAreaHit;
    private readonly HashSet<SkillExecutionType> _reportedUnsupportedTypes = new();
    private readonly HashSet<SkillData> _reportedMissingProjectilePrefabs = new();
    private readonly HashSet<PlayerCombatController> _reportedMissingDashControllers = new();
    private readonly List<EnemyController> _instantAreaDaggerTargets = new();
    private readonly Collider2D[] _blinkEnemyBuffer = new Collider2D[BlinkEnemyBufferSize];
    private bool _reportedProcSequenceLimit;
    private readonly HashSet<SkillData> _reportedProcRecoilSkills = new();

    public SkillExecutor(AttackExecutor attackExecutor)
        : this(attackExecutor, null, null)
    {
    }

    public SkillExecutor(
        AttackExecutor attackExecutor,
        Func<SkillExecutionContext, bool> canContinueMultiHit)
        : this(attackExecutor, canContinueMultiHit, null)
    {
    }

    public SkillExecutor(
        AttackExecutor attackExecutor,
        Func<SkillExecutionContext, bool> canContinueMultiHit,
        SkillInstantAreaHitHandler onInstantAreaHit)
    {
        _attackExecutor = attackExecutor;
        _targetResolver = new SkillTargetResolver();
        _projectileFireService = new ProjectileFireService();
        _executeMultiHitStep = ExecuteMultiHitStep;
        _multiHitRunner = new MultiHitSkillRunner(_executeMultiHitStep, canContinueMultiHit);
        _onInstantAreaHit = onInstantAreaHit;
    }

    public bool IsMultiHitActive => _multiHitRunner.IsActive;
    public SkillData ActiveMultiHitSkill => _multiHitRunner.ActiveSkill;

    public void TickMultiHit(float deltaTime)
    {
        _multiHitRunner.Tick(deltaTime);
    }

    public void CancelMultiHit()
    {
        _multiHitRunner.Cancel();
    }

    public void ClearAllProcSequences()
    {
        _multiHitRunner.ClearAllProcSequences();
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

            case SkillExecutionType.Blink:
                return ExecuteBlink(context);

            case SkillExecutionType.Buff:
                return ExecuteBuff(context);

            case SkillExecutionType.AreaOverTime:
                return ExecuteAreaOverTime(context);

            default:
                ReportUnsupportedExecutionType(context.Skill.executionType);
                return SkillExecutionResult.Failure;
        }
    }

    private SkillExecutionResult ExecuteInstantArea(SkillExecutionContext context)
    {
        List<Vector3> targets = _targetResolver.ResolveWorldTargets(context);
        CustomShapeMatcher? customShape = null;
        if (SkillTargetResolver.TryCreateCustomShapeMatcher(
                context.Skill,
                context.CasterPosition,
                context.AimDirection,
                context.GridAimDirection,
                out CustomShapeMatcher matcher))
        {
            customShape = matcher;
        }

        bool didCrit = false;
        int skillDamage = ResolveSkillDamage(context);
        int scaledDamage = ApplyParryStackRampSnapshot(
            context,
            context.TotalAttack + skillDamage);
        int damage = context.CasterCombat != null
            ? context.CasterCombat.RollCritDamage(scaledDamage, out didCrit)
            : scaledDamage;
        _attackExecutor.BeginAttackActivation();
        _attackExecutor.ExecuteAttackWorld(
            targets,
            damage,
            context.Skill.canPenetrateWalls,
            context.Skill.isMultiTarget,
            context.Skill.knockbackForce,
            context.Skill.knockbackDuration,
            context.Skill.slowPercentage,
            context.Skill.slowDuration,
            ResolveAttackAilments(context),
            ResolveCombatEffectContext(context),
            context.HitRadius,
            customShape);
        int damageDealtThisAttack = _attackExecutor.DamageDealtThisAttack;
        CaptureInstantAreaDaggerTargets(context);
        DetonateDaggerMarkersFromInstantArea(context);
        ApplyDaggerMarkersFromInstantArea(context);
        NotifyInstantAreaHit(targets, customShape, context.CasterPosition);
        context.CasterCombat?.ReportLifestealDamage(damageDealtThisAttack);
        if (damageDealtThisAttack > 0)
        {
            if (!context.IsProcCast)
                context.CasterCombat?.RegisterComboHit();
            context.CasterCombat?.LogDamageDealt(damageDealtThisAttack, didCrit);
        }

        Vector2 direction = ResolveExecutionDirection(context);
        PlayConfiguredAnimation(context, context.Skill, direction);
        ApplyRecoil(context, direction);
        if (context.Skill.hitSteps != null && context.Skill.hitSteps.Count > 0)
        {
            if (context.IsProcCast)
            {
                if (!_multiHitRunner.TryStartProcSequence(context))
                    ReportProcSequenceLimitOnce();
            }
            else
            {
                _multiHitRunner.Start(context);
            }
        }

        return SkillExecutionResult.SuccessWithCost(context.Skill.consumeAmount);
    }

    private void ExecuteInstantAreaHit(SkillExecutionContext context, HitStep hitStep)
    {
        _attackExecutor.BeginAttackActivation();

        Vector3 origin = context.IsProcCast
            ? context.CasterPosition
            : context.CasterTransform.position;
        IReadOnlyList<Vector2Int> overrideCells = hitStep.overrideCells;
        bool usesOverride = overrideCells != null && overrideCells.Count > 0;
        List<Vector3> targets = _targetResolver.ResolveWorldTargets(context, origin, overrideCells);

        CustomShapeMatcher? customShape = null;
        CustomShapeMatcher matcher;
        bool hasCustomShape = usesOverride
            ? SkillTargetResolver.TryCreateCustomShapeMatcher(
                overrideCells,
                origin,
                context.AimDirection,
                context.GridAimDirection,
                out matcher)
            : SkillTargetResolver.TryCreateCustomShapeMatcher(
                context.Skill,
                origin,
                context.AimDirection,
                context.GridAimDirection,
                out matcher);
        if (hasCustomShape)
            customShape = matcher;

        int scaledDamage = Mathf.RoundToInt(
            (context.TotalAttack + ResolveSkillDamage(context)) *
            hitStep.damagePct / 100f);
        scaledDamage = ApplyParryStackRampSnapshot(context, scaledDamage);
        bool didCrit = false;
        int damage = context.CasterCombat != null
            ? context.CasterCombat.RollCritDamage(scaledDamage, out didCrit)
            : scaledDamage;

        _attackExecutor.ExecuteAttackWorld(
            targets,
            damage,
            context.Skill.canPenetrateWalls,
            context.Skill.isMultiTarget,
            context.Skill.knockbackForce,
            context.Skill.knockbackDuration,
            context.Skill.slowPercentage,
            context.Skill.slowDuration,
            ResolveAttackAilments(context),
            ResolveCombatEffectContext(context),
            context.HitRadius,
            customShape);
        int damageDealtThisAttack = _attackExecutor.DamageDealtThisAttack;
        CaptureInstantAreaDaggerTargets(context);
        DetonateDaggerMarkersFromInstantArea(context);
        ApplyDaggerMarkersFromInstantArea(context);
        NotifyInstantAreaHit(targets, customShape, origin);
        context.CasterCombat?.ReportLifestealDamage(damageDealtThisAttack);
        if (damageDealtThisAttack > 0)
        {
            if (!context.IsProcCast)
                context.CasterCombat?.RegisterComboHit();
            context.CasterCombat?.LogDamageDealt(damageDealtThisAttack, didCrit);
        }

        Vector2 direction = ResolveExecutionDirection(context);
        PlayConfiguredAnimation(context, context.Skill, direction);
        ApplyRecoil(context, direction);
    }

    private void CaptureInstantAreaDaggerTargets(SkillExecutionContext context)
    {
        if (context.IsProcCast)
            return;

        SkillData skill = context.Skill;
        if (!skill.appliesDaggerMarker && !skill.detonatesDaggerMarker)
            return;

        _instantAreaDaggerTargets.Clear();
        for (int i = 0; i < _attackExecutor.HitEnemyCount; i++)
            _instantAreaDaggerTargets.Add(_attackExecutor.GetHitEnemy(i));
    }

    private void DetonateDaggerMarkersFromInstantArea(SkillExecutionContext context)
    {
        SkillData skill = context.Skill;
        if (context.IsProcCast ||
            !skill.detonatesDaggerMarker ||
            context.CasterCombat == null)
        {
            return;
        }

        for (int i = 0; i < _instantAreaDaggerTargets.Count; i++)
        {
            EnemyController enemy = _instantAreaDaggerTargets[i];
            if (enemy != null && enemy.IsAlive)
                context.CasterCombat.TryDetonateDaggerMarker(enemy, skill);
        }
    }

    private void ApplyDaggerMarkersFromInstantArea(SkillExecutionContext context)
    {
        SkillData skill = context.Skill;
        if (context.IsProcCast || !skill.appliesDaggerMarker)
            return;

        for (int i = 0; i < _instantAreaDaggerTargets.Count; i++)
        {
            EnemyController enemy = _instantAreaDaggerTargets[i];
            if (enemy != null && enemy.IsAlive)
                DaggerMarkerRegistry.Instance.Apply(enemy, skill.markerDuration);
        }
    }

    private void ExecuteMultiHitStep(SkillExecutionContext context, HitStep hitStep)
    {
        if (context.Skill.executionType == SkillExecutionType.Projectile)
        {
            ExecuteProjectileHit(context, hitStep);
            return;
        }

        ExecuteInstantAreaHit(context, hitStep);
    }

    private void ReportProcSequenceLimitOnce()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_reportedProcSequenceLimit)
            return;

        _reportedProcSequenceLimit = true;
        Debug.LogWarning(
            "[SkillExecutor] Proc multi-hit sequence rejected: concurrent limit " +
            MultiHitSkillRunner.MaxConcurrentProcSequences + " reached.");
#endif
    }

    private void ReportProcRecoilOnce(SkillData skill)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (skill != null && _reportedProcRecoilSkills.Add(skill))
        {
            string skillName = string.IsNullOrWhiteSpace(skill.skillName) ? skill.name : skill.skillName;
            Debug.LogWarning($"[SkillExecutor] Proc skill '{skillName}' applies recoil.");
        }
#endif
    }

    private void NotifyInstantAreaHit(
        IReadOnlyList<Vector3> worldTargets,
        CustomShapeMatcher? customShape,
        Vector3 origin)
    {
        if (_onInstantAreaHit == null)
            return;

        float cellSize = customShape.HasValue
            ? customShape.Value.CellSize
            : WorldEnvironmentQuery.GetCellSize(origin);

        try
        {
            _onInstantAreaHit(worldTargets, customShape, cellSize);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
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
        context.AmmoRampSnapshot = context.CasterCombat != null
            ? context.CasterCombat.GetAmmoRampMultiplier(resourceConsumed)
            : 1f;
        if (!_projectileFireService.Fire(CreateProjectileFireRequest(context, direction, projectileCount)))
            return SkillExecutionResult.Failure;

        PlayConfiguredAnimation(context, skill, direction);
        ApplyRecoil(context, direction);
        if (!context.IsBasicAttack && skill.hitSteps != null && skill.hitSteps.Count > 0)
        {
            if (context.IsProcCast)
            {
                if (!_multiHitRunner.TryStartProcSequence(context))
                    ReportProcSequenceLimitOnce();
            }
            else
            {
                _multiHitRunner.Start(context);
            }
        }

        return SkillExecutionResult.SuccessWithCost(resourceConsumed);
    }

    private void ExecuteProjectileHit(SkillExecutionContext context, HitStep hitStep)
    {
        SkillData skill = context.Skill;
        int scaledDamage = Mathf.RoundToInt(
            (context.TotalAttack + ResolveSkillDamage(context)) *
            hitStep.damagePct / 100f);
        scaledDamage = Mathf.RoundToInt(
            scaledDamage * context.AmmoRampSnapshot);
        scaledDamage = ApplyParryStackRampSnapshot(context, scaledDamage);
        bool didCrit = false;
        int damage = context.CasterCombat != null
            ? context.CasterCombat.RollCritDamage(scaledDamage, out didCrit)
            : scaledDamage;
        Vector2 direction = ResolveExecutionDirection(context);
        int projectileCount = hitStep.projectileCount > 0
            ? hitStep.projectileCount
            : SkillProjectileUtility.GetEffectiveProjectileCount(skill);
        float spreadAngle = hitStep.spreadAngle > 0f
            ? hitStep.spreadAngle
            : skill.projectileSpreadAngle;

        _projectileFireService.Fire(CreateProjectileFireRequest(
            context,
            direction,
            projectileCount,
            spreadAngle,
            scaledDamage,
            damage,
            didCrit));
        PlayConfiguredAnimation(context, skill, direction);
        ApplyRecoil(context, direction);
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

    private SkillExecutionResult ExecuteAreaOverTime(SkillExecutionContext context)
    {
        if (DamageZoneSpawner.Instance == null)
            return SkillExecutionResult.Failure;

        SkillData skill = context.Skill;
        int skillDamage = ResolveSkillDamage(context);
        int tickDamage = skillDamage > 0 ? context.TotalAttack + skillDamage : 0;
        ZonePayload payload = new ZonePayload(
            tickDamage,
            skill.zoneDuration,
            skill.slowPercentage,
            skill.slowDuration,
            ResolveAttackAilments(context),
            ResolveCombatEffectContext(context),
            skill.zoneRadius,
            skill.zoneTickInterval);
        if (!DamageZoneSpawner.Instance.SpawnZone(
                context.CasterPosition,
                skill.zoneSprite,
                skill.zoneAnimation,
                in payload))
            return SkillExecutionResult.Failure;

        PlayConfiguredAnimation(context, skill, ResolveExecutionDirection(context));
        return SkillExecutionResult.SuccessWithCost(skill.consumeAmount);
    }

    private SkillExecutionResult ExecuteBlink(SkillExecutionContext context)
    {
        SkillData skill = context.Skill;
        if (!TryFindNearestEnemy(context, out EnemyController target))
            return SkillExecutionResult.Failure;

        Vector3 start = context.CasterTransform.position;
        Vector3 targetPosition = target.transform.position;
        Vector2 awayFromCaster = targetPosition - start;
        if (awayFromCaster.sqrMagnitude <= 0.0001f)
            awayFromCaster = ResolveExecutionDirection(context);
        awayFromCaster.Normalize();

        PlayerController playerController = context.CasterTransform.GetComponent<PlayerController>();
        float radius = context.CasterCombat != null ? context.CasterCombat.CachedHitRadius : Mathf.Max(0.01f, context.HitRadius);
        if (!TryFindBlinkLandingPosition(
                start,
                targetPosition,
                awayFromCaster,
                Mathf.Max(0f, skill.blinkBehindOffset),
                playerController,
                radius,
                out Vector3 blinkPosition))
        {
            return SkillExecutionResult.Failure;
        }

        if (playerController != null)
            playerController.TeleportTo(blinkPosition);
        else
            context.CasterTransform.position = blinkPosition;

        if (!context.IsProcCast && skill.appliesDaggerMarker)
            DaggerMarkerRegistry.Instance.Apply(target, skill.markerDuration);
        CombatEffectContext effectContext =
            ResolveCombatEffectContext(context);
        target.ApplyAilments(skill.ailments, in effectContext);

        PlayConfiguredAnimation(context, skill, (targetPosition - start).normalized);
        return SkillExecutionResult.SuccessWithCost(skill.consumeAmount);
    }

    private static bool TryFindBlinkLandingPosition(
        Vector3 currentPosition,
        Vector3 targetPosition,
        Vector2 behindDirection,
        float offset,
        PlayerController playerController,
        float footprintRadius,
        out Vector3 landingPosition)
    {
        float currentPositionEpsilonSqr = BlinkCurrentPositionEpsilon * BlinkCurrentPositionEpsilon;
        for (int i = 0; i < s_BlinkLandingAngles.Length; i++)
        {
            float radians = s_BlinkLandingAngles[i] * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            Vector2 direction = new Vector2(
                behindDirection.x * cosine - behindDirection.y * sine,
                behindDirection.x * sine + behindDirection.y * cosine);
            Vector3 candidate = targetPosition + (Vector3)(direction * offset);
            // A landing within 0.5f is effectively no reposition, so Blink must not consume a cast.
            if (((Vector2)(candidate - currentPosition)).sqrMagnitude <= currentPositionEpsilonSqr)
                continue;

            bool canOccupy = playerController != null
                ? playerController.CanOccupyPosition(candidate)
                : WorldEnvironmentQuery.IsFootprintWalkable(candidate, footprintRadius);
            if (!canOccupy)
                continue;

            landingPosition = candidate;
            return true;
        }

        landingPosition = default;
        return false;
    }

    private SkillExecutionResult ExecuteBuff(SkillExecutionContext context)
    {
        if (context.CasterCombat == null)
            return SkillExecutionResult.Failure;

        SkillData skill = context.Skill;
        if (!context.IsProcCast && skill.appliesDaggerMarker)
            context.CasterCombat.BeginDaggerBasicAttackMarkerBuff(skill);

        PlayConfiguredAnimation(context, skill, ResolveExecutionDirection(context));
        return SkillExecutionResult.SuccessWithCost(skill.consumeAmount);
    }

    private static void PlayConfiguredAnimation(SkillExecutionContext context, SkillData skill, Vector2 direction)
    {
        if (context.IsProcCast)
            return;

        if (context.CasterForm == null || skill == null)
            return;

        context.CasterForm.PlaySkillAnimation(skill, direction);
    }

    private void ApplyRecoil(SkillExecutionContext context, Vector2 direction)
    {
        if (context.Skill.recoilDistance <= 0f)
            return;
        if (context.CasterCombat == null || context.CasterCombat.playerMovement == null)
            return;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        if (context.IsProcCast)
            ReportProcRecoilOnce(context.Skill);

        context.CasterCombat.AddRecoil(
            -direction.normalized * context.Skill.recoilDistance,
            context.Skill.recoilDuration);
    }

    private static DashDamageRequest CreateDashDamageRequest(SkillExecutionContext context)
    {
        SkillData skill = context.Skill;
        bool didCrit = false;
        bool hasDashDamage = skill.dashDamageOnPath || skill.dashDamageOnContact;
        int damage = 0;
        if (hasDashDamage)
        {
            int skillDamage = ResolveSkillDamage(context);
            int scaledDamage = ApplyParryStackRampSnapshot(
                context,
                context.TotalAttack + skillDamage);
            damage = context.CasterCombat != null
                ? context.CasterCombat.RollCritDamage(
                    scaledDamage,
                    out didCrit)
                : scaledDamage;
        }

        return new DashDamageRequest
        {
            DamageOnPath = skill.dashDamageOnPath,
            DamageOnContact = skill.dashDamageOnContact,
            CasterCombat = context.CasterCombat,
            Damage = damage,
            IsCrit = didCrit,
            HitRadius = context.HitRadius,
            KnockbackForce = skill.knockbackForce,
            KnockbackDuration = skill.knockbackDuration,
            SlowPercentage = skill.slowPercentage,
            SlowDuration = skill.slowDuration,
            Ailments = hasDashDamage ? ResolveAttackAilments(context) : skill.ailments,
            EffectContext = ResolveCombatEffectContext(context),
            OnEnemyHit = !context.IsProcCast &&
                         skill.detonatesDaggerMarker &&
                         context.CasterCombat != null
                ? context.CasterCombat.PrepareDaggerDashHitCallback(skill, context.SlotIndex)
                : null
        };
    }

    private static ProjectileFireRequest CreateProjectileFireRequest(SkillExecutionContext context, Vector2 direction, int projectileCount)
    {
        bool didCrit = false;
        int skillDamage = ResolveSkillDamage(context);
        int baseDamage = context.TotalAttack + skillDamage;
        int amplifiedDamage = context.CasterCombat != null
            ? Mathf.RoundToInt(
                baseDamage * context.AmmoRampSnapshot)
            : baseDamage;
        amplifiedDamage = ApplyParryStackRampSnapshot(
            context,
            amplifiedDamage);
        int damage = context.CasterCombat != null
            ? context.CasterCombat.RollCritDamage(amplifiedDamage, out didCrit)
            : amplifiedDamage;
        return CreateProjectileFireRequest(
            context,
            direction,
            projectileCount,
            context.Skill.projectileSpreadAngle,
            amplifiedDamage,
            damage,
            didCrit);
    }

    private static ProjectileFireRequest CreateProjectileFireRequest(
        SkillExecutionContext context,
        Vector2 direction,
        int projectileCount,
        float spreadAngle,
        int baseDamage,
        int damage,
        bool didCrit)
    {
        SkillData skill = context.Skill;
        return new ProjectileFireRequest
        {
            ProjectilePrefab = skill.projectilePrefab,
            OriginTransform = context.CasterTransform,
            OriginPositionOverride = context.IsProcCast ? context.CasterPosition : (Vector3?)null,
            CoroutineRunner = context.CasterCombat,
            Caster = context.CasterCombat,
            Owner = context.CasterCombat,
            Direction = direction,
            BaseDamage = baseDamage,
            Damage = damage,
            IsCrit = didCrit,
            IsProcCast = context.IsProcCast,
            Speed = skill.projectileSpeed,
            Lifetime = skill.projectileLifetime,
            ProjectileCount = Mathf.Max(1, projectileCount),
            SpreadAngle = spreadAngle,
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
            SlowDuration = skill.slowDuration,
            Ailments = ResolveAttackAilments(context),
            EffectContext = ResolveCombatEffectContext(context),
            OnEnemyHit = context.CasterCombat != null
                ? context.CasterCombat.ResolveProjectileHitCallback(
                    skill,
                    context.IsProcCast)
                : null,
            DaggerMarkerDuration = skill.markerDuration,
            SplitDepth = context.CasterCombat != null
                ? context.CasterCombat.SplitShotDepth
                : 0
        };
    }

    private static CombatEffectContext ResolveCombatEffectContext(
        SkillExecutionContext context)
    {
        return context.CasterCombat != null
            ? context.CasterCombat.CurrentCombatEffectContext
            : CombatEffectContext.Default;
    }

    private static int ResolveSkillDamage(SkillExecutionContext context)
    {
        return context.SkillDamageOverride ?? context.Skill.damage;
    }

    private static int ApplyParryStackRampSnapshot(
        SkillExecutionContext context,
        int damage)
    {
        float multiplier = context != null
            ? Mathf.Max(0f, context.ParryStackRampSnapshot)
            : 1f;
        if (Mathf.Approximately(multiplier, 1f))
            return damage;

        return Mathf.RoundToInt(damage * multiplier);
    }

    private static AilmentApplication[] ResolveAttackAilments(SkillExecutionContext context)
    {
        AilmentApplication[] authored = context.Skill.ailments;
        IReadOnlyList<AilmentApplication> bonuses = context.CasterCombat != null
            ? context.CasterCombat.BonusAttackAilments
            : null;
        if (bonuses == null || bonuses.Count == 0)
            return authored;

        int authoredCount = authored != null ? authored.Length : 0;
        AilmentApplication[] merged = new AilmentApplication[authoredCount + bonuses.Count];
        if (authoredCount > 0)
            Array.Copy(authored, merged, authoredCount);

        for (int i = 0; i < bonuses.Count; i++)
            merged[authoredCount + i] = bonuses[i];

        return merged;
    }

    private bool TryFindNearestEnemy(SkillExecutionContext context, out EnemyController nearest)
    {
        nearest = null;
        float range = Mathf.Max(0.01f, context.Skill.patternRange * WorldEnvironmentQuery.GetCellSize(context.CasterPosition));
        int count = Physics2D.OverlapCircle(context.CasterPosition, range, CombatLayers.EnemyFilter, _blinkEnemyBuffer);
        float bestSqrDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider2D hit = _blinkEnemyBuffer[i];
            if (hit == null) continue;
            if (!hit.TryGetComponent(out EnemyController enemy) || !enemy.IsAlive) continue;

            float sqrDistance = ((Vector2)enemy.transform.position - (Vector2)context.CasterPosition).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance) continue;

            bestSqrDistance = sqrDistance;
            nearest = enemy;
        }

        return nearest != null;
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
