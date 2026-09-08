using UnityEngine;

[System.Serializable]
public sealed class ProjectileSettings
{
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField, Min(0.01f)] private float projectileSpeed = 6f;
    [SerializeField, Min(0.01f)] private float projectileLifetime = 3f;
    [SerializeField] private ProjectileFirePattern firePattern = ProjectileFirePattern.Single;
    [SerializeField, Min(1)] private int projectileCount = 1;
    [SerializeField, Min(0f)] private float spreadAngle = 30f;
    [SerializeField, Min(0f)] private float burstInterval = 0.1f;
    [SerializeField] private ProjectileWallHitMode wallHitMode = ProjectileWallHitMode.Destroy;
    [SerializeField, Min(0)] private int maxBounceCount = 1;
    [SerializeField] private EnemyAttackImpactData impact = EnemyAttackImpactData.Default;

    public GameObject ProjectilePrefab => projectilePrefab;
    public float ProjectileSpeed => Mathf.Max(0.01f, projectileSpeed);
    public float ProjectileLifetime => Mathf.Max(0.01f, projectileLifetime);
    public ProjectileFirePattern FirePattern => firePattern;
    public int ProjectileCount => Mathf.Max(1, projectileCount);
    public float SpreadAngle => Mathf.Max(0f, spreadAngle);
    public float BurstInterval => Mathf.Max(0f, burstInterval);
    public ProjectileWallHitMode WallHitMode => wallHitMode;
    public int MaxBounceCount => Mathf.Max(0, maxBounceCount);
    public EnemyAttackImpactData Impact => impact;
}
