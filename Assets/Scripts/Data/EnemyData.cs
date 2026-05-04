using UnityEngine;

public enum EnemyBehaviorType
{
    Contact,
    Ranged
}

public enum ProjectileFirePattern
{
    Single,
    Burst,
    Spread,
    Circle
}

public enum ProjectileWallHitMode
{
    Destroy,
    PassThrough,
    Bounce
}

[CreateAssetMenu(fileName = "NewEnemy", menuName = "JBLogLike/Enemy")]
public class EnemyData : ScriptableObject
{
    [Header("Basic")]
    public string enemyName = "Slime";

    [Header("Combat Stats")]
    public int maxHp = 5;
    public int attack = 2;
    public int defense = 0;
    public int expReward = 10;

    [Header("Spawn Budget")]
    [Min(1)]
    public int spawnCost = 1;
    public SpawnRegion allowedRegions = SpawnRegion.Dungeon;

    [Header("AI Behavior")]
    public EnemyBehaviorType behaviorType = EnemyBehaviorType.Contact;

    [Tooltip("Player detection distance in world units.")]
    public float detectRange = 5f;

    [Tooltip("Ranged behavior attack distance in world units. Contact behavior does not use this.")]
    public float attackRange = 1.2f;

    [Tooltip("Movement speed in world units per second.")]
    public float moveSpeed = 2f;

    [Tooltip("Ranged behavior cooldown between attacks.")]
    public float attackCooldown = 1.5f;

    [Tooltip("Ranged behavior windup before applying an attack.")]
    public float attackWindup = 0.4f;

    [Tooltip("Ranged behavior recovery after firing. Movement is locked while this is greater than 0.")]
    public float attackRecovery = 0f;

    [Header("Contact Behavior")]
    [Tooltip("Fallback contact damage distance used when collider contact cannot be checked.")]
    public float contactDamageRadius = 0.45f;

    [Tooltip("Contact damage is applied when collider distance is less than or equal to this value.")]
    public float contactDamageSkin = 0.05f;

    [Header("Ranged Projectile")]
    public GameObject projectilePrefab;

    [Tooltip("Projectile damage. If 0, this enemy's attack value is used.")]
    [Min(0)]
    public int projectileDamage = 0;

    [Min(0.01f)]
    public float projectileSpeed = 6f;

    [Min(0.01f)]
    public float projectileLifetime = 3f;

    public ProjectileFirePattern firePattern = ProjectileFirePattern.Single;

    [Min(1)]
    public int projectileCount = 1;

    [Min(0f)]
    public float spreadAngle = 30f;

    [Min(0f)]
    public float burstInterval = 0.12f;

    public ProjectileWallHitMode projectileWallHitMode = ProjectileWallHitMode.Destroy;

    [Tooltip("Maximum wall bounces for Bounce mode. 0 means the projectile is released on the first wall hit.")]
    [Min(0)]
    public int projectileMaxBounceCount = 1;

    [Tooltip("Minimum projectile instances to prepare before this ranged enemy attacks.")]
    [Min(0)]
    public int projectilePrewarmCount = 8;

    [Header("Status Resistance")]
    [Range(0f, 1f)]
    public float knockbackResistance = 0f;
}
