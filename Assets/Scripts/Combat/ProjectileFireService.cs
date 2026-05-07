using System.Collections;
using UnityEngine;

public sealed class ProjectileFireService
{
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
        ProjectileController projectile = ProjectilePool.Instance.Get(
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
            request.SlowDuration);
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
}
