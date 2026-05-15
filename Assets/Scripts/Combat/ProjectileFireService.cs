using System.Collections;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
#endif
using UnityEngine;

public sealed class ProjectileFireService
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static readonly HashSet<int> s_ReportedMissingBurstCoroutineRunnerCasters = new();
#endif

    public bool Fire(ProjectileFireRequest request)
    {
        if (!CanFire(request))
            return false;

        request.Direction = NormalizeDirection(request.Direction);
        request.ProjectileCount = GetProjectileRequestCount(request);

        switch (request.FirePattern)
        {
            case ProjectileFirePattern.Single:
                return SpawnProjectile(request);

            case ProjectileFirePattern.Spread:
                return FireSpread(request);

            case ProjectileFirePattern.Circle:
                return FireCircle(request);

            case ProjectileFirePattern.Burst:
                return StartBurst(request);

            default:
                return SpawnProjectile(request);
        }
    }

    public static int GetProjectileRequestCount(ProjectileFireRequest request)
    {
        if (request == null) return 0;

        switch (request.FirePattern)
        {
            case ProjectileFirePattern.Burst:
            case ProjectileFirePattern.Spread:
            case ProjectileFirePattern.Circle:
                return Mathf.Max(1, request.ProjectileCount);

            default:
                return 1;
        }
    }

    private static bool FireSpread(ProjectileFireRequest request)
    {
        int count = request.ProjectileCount;
        if (count <= 1)
            return SpawnProjectile(request);

        bool spawnedAny = false;
        float spread = Mathf.Max(0f, request.SpreadAngle);
        float startAngle = -spread * 0.5f;
        float step = spread / (count - 1);
        for (int i = 0; i < count; i++)
            spawnedAny |= SpawnProjectile(request, Rotate(request.Direction, startAngle + step * i));
        return spawnedAny;
    }

    private static bool FireCircle(ProjectileFireRequest request)
    {
        int count = request.ProjectileCount;
        if (count <= 1)
            return SpawnProjectile(request);

        bool spawnedAny = false;
        float step = 360f / count;
        for (int i = 0; i < count; i++)
            spawnedAny |= SpawnProjectile(request, Rotate(request.Direction, step * i));
        return spawnedAny;
    }

    private static bool StartBurst(ProjectileFireRequest request)
    {
        if (!SpawnProjectile(request))
            return false;

        int remainingCount = Mathf.Max(1, request.ProjectileCount) - 1;
        if (remainingCount > 0 && request.CoroutineRunner != null)
            request.CoroutineRunner.StartCoroutine(FireBurstRoutine(request, remainingCount));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        else if (remainingCount > 0)
            ReportMissingBurstCoroutineRunner(request, remainingCount);
#endif

        return true;
    }

    private static IEnumerator FireBurstRoutine(ProjectileFireRequest request, int remainingCount)
    {
        WaitForSeconds wait = request.BurstInterval > 0f ? new WaitForSeconds(request.BurstInterval) : null;
        for (int i = 0; i < remainingCount; i++)
        {
            if (wait != null)
                yield return wait;
            else
                yield return null;

            if (!CanFire(request))
                yield break;

            SpawnProjectile(request);
        }
    }

    private static bool SpawnProjectile(ProjectileFireRequest request)
    {
        return SpawnProjectile(request, request.Direction);
    }

    private static bool SpawnProjectile(ProjectileFireRequest request, Vector2 direction)
    {
        if (!CanFire(request))
            return false;

        direction = NormalizeDirection(direction);
        Vector3 spawnPosition = request.OriginTransform.position
            + (Vector3)(direction * Mathf.Max(0f, request.SpawnOffset));
        ProjectilePool pool = ProjectilePool.Instance;
        if (pool == null)
            return false;

        ProjectileController projectile = pool.Get(
            request.ProjectilePrefab,
            spawnPosition,
            Quaternion.identity);
        if (projectile == null) return false;

        projectile.Initialize(
            direction,
            request.Damage,
            request.Speed,
            request.Lifetime,
            request.WallHitMode,
            request.MaxBounceCount,
            request.Owner,
            request.TargetMode,
            request.TargetHitMode,
            request.KnockbackForce,
            request.KnockbackDuration,
            request.SlowPercentage,
            request.SlowDuration,
            request.StunDuration);
        return true;
    }

    private static bool CanFire(ProjectileFireRequest request)
    {
        return request != null
            && request.ProjectilePrefab != null
            && request.OriginTransform != null
            && request.OriginTransform.gameObject.activeInHierarchy
            && (request.Caster == null || request.Caster.IsAlive);
    }

    private static Vector2 NormalizeDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector2.down;
        return direction.normalized;
    }

    private static Vector2 Rotate(Vector2 direction, float degrees)
    {
        direction = NormalizeDirection(direction);
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(
            direction.x * cos - direction.y * sin,
            direction.x * sin + direction.y * cos).normalized;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static void ReportMissingBurstCoroutineRunner(ProjectileFireRequest request, int remainingCount)
    {
        int casterId = ResolveCasterWarningId(request);
        if (!s_ReportedMissingBurstCoroutineRunnerCasters.Add(casterId))
            return;

        string casterName = ResolveCasterWarningName(request);
        Debug.LogWarning(
            $"[ProjectileFireService] Burst requested {remainingCount + 1} projectiles for {casterName}, " +
            "but CoroutineRunner is null. The first projectile was fired and the remaining burst projectiles were skipped.");
    }

    private static int ResolveCasterWarningId(ProjectileFireRequest request)
    {
#pragma warning disable CS0618 // Requested per-caster warning key; editor/development-only diagnostic path.
        if (request?.Caster is Object casterObject && casterObject != null)
            return casterObject.GetInstanceID();

        if (request?.OriginTransform != null)
            return request.OriginTransform.GetInstanceID();
#pragma warning restore CS0618

        return 0;
    }

    private static string ResolveCasterWarningName(ProjectileFireRequest request)
    {
        if (request?.Caster is Object casterObject && casterObject != null)
            return casterObject.name;

        if (request?.OriginTransform != null)
            return request.OriginTransform.name;

        return "unknown caster";
    }
#endif
}
