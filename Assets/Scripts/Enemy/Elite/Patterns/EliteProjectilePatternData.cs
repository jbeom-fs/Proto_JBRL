using UnityEngine;

[CreateAssetMenu(fileName = "NewEliteProjectilePattern", menuName = "JBRogLike/Enemy/Elite Projectile Pattern")]
public sealed class EliteProjectilePatternData : ElitePatternData
{
    [Header("Timing")]
    [SerializeField] private float windupDuration = 0.5f;

    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private int damage = 1;
    [SerializeField] private float projectileSpeed = 6f;
    [SerializeField] private float projectileLifetime = 3f;
    [SerializeField] private ProjectileFirePattern firePattern = ProjectileFirePattern.Single;
    [SerializeField] private int projectileCount = 1;
    [SerializeField] private float spreadAngle = 30f;
    [SerializeField] private float burstInterval = 0.1f;
    [SerializeField] private ProjectileWallHitMode wallHitMode = ProjectileWallHitMode.Destroy;
    [SerializeField] private int maxBounceCount = 1;
    [SerializeField] private EnemyAttackImpactData impact = default;

    [Header("Animation")]
    [SerializeField] private EnemyAnimationKey windupAnimation = EnemyAnimationKey.Charge;
    [SerializeField] private EnemyAnimationKey fireAnimation = EnemyAnimationKey.Projectile;
    [SerializeField] private EnemyAnimationKey recoveryAnimation = EnemyAnimationKey.None;

    public float WindupDuration => Mathf.Max(0f, windupDuration);
    public GameObject ProjectilePrefab => projectilePrefab;
    public int Damage => Mathf.Max(0, damage);
    public float ProjectileSpeed => Mathf.Max(0.01f, projectileSpeed);
    public float ProjectileLifetime => Mathf.Max(0.01f, projectileLifetime);
    public ProjectileFirePattern FirePattern => firePattern;
    public int ProjectileCount => Mathf.Max(1, projectileCount);
    public float SpreadAngle => Mathf.Max(0f, spreadAngle);
    public float BurstInterval => Mathf.Max(0f, burstInterval);
    public ProjectileWallHitMode WallHitMode => wallHitMode;
    public int MaxBounceCount => Mathf.Max(0, maxBounceCount);
    public EnemyAttackImpactData Impact => impact;
    public EnemyAnimationKey WindupAnimation => windupAnimation;
    public EnemyAnimationKey FireAnimation => fireAnimation;
    public EnemyAnimationKey RecoveryAnimation => recoveryAnimation;

    public override ElitePatternRuntime CreateRuntime()
    {
        return new EliteProjectilePatternRuntime(this);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    protected override void OnValidate()
    {
        base.OnValidate();

        if (windupDuration < 0f)
            Debug.LogWarning($"[EliteProjectilePatternData] {name}: windupDuration is negative.", this);
        if (damage < 0)
            Debug.LogWarning($"[EliteProjectilePatternData] {name}: damage is negative.", this);
        if (projectileSpeed <= 0f)
            Debug.LogWarning($"[EliteProjectilePatternData] {name}: projectileSpeed must be greater than 0.", this);
        if (projectileLifetime <= 0f)
            Debug.LogWarning($"[EliteProjectilePatternData] {name}: projectileLifetime must be greater than 0.", this);
        if (projectileCount <= 0)
            Debug.LogWarning($"[EliteProjectilePatternData] {name}: projectileCount must be greater than 0.", this);
        if (spreadAngle < 0f)
            Debug.LogWarning($"[EliteProjectilePatternData] {name}: spreadAngle is negative.", this);
        if (burstInterval < 0f)
            Debug.LogWarning($"[EliteProjectilePatternData] {name}: burstInterval is negative.", this);
        if (maxBounceCount < 0)
            Debug.LogWarning($"[EliteProjectilePatternData] {name}: maxBounceCount is negative.", this);
    }
#endif
}
