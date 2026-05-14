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

public enum RangedMovementType
{
    Chase,
    Random,
    Kiting
}

public enum EnemySpecialAttackType
{
    None,
    Rush,
    Jump
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

    [Tooltip("Delay before this enemy is disabled after reaching 0 HP.")]
    [Min(0f)]
    public float deathDelay = 0.5f;

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

    [Header("Movement Flags")]
    [Tooltip("If true, AI movement, ranged movement, and separation movement are disabled while targeting and attacks still run.")]
    public bool isStationary = false;

    [Tooltip("If true, damage and status effects still apply, but knockback position movement is ignored.")]
    public bool immuneToKnockback = false;

    [Tooltip("If true, this enemy blocks Player walking and dash movement. This does not affect AI movement or knockback by itself.")]
    public bool blocksMovement = false;

    [Header("Attack")]
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

    [Header("Contact Special Attack")]
    [Tooltip("Optional Contact-only special attack pattern.")]
    public EnemySpecialAttackType specialAttackType = EnemySpecialAttackType.None;

    [Tooltip("Contact special attack trigger distance in world units.")]
    [Min(0f)]
    public float specialAttackRange = 3f;

    [Tooltip("Contact special attack cooldown after the special action starts.")]
    [Min(0f)]
    public float specialAttackCooldown = 2f;

    [Tooltip("Contact special attack windup. Enemy stops and stores the target direction or landing position.")]
    [Min(0f)]
    public float specialAttackWindup = 0.35f;

    [Tooltip("Contact special attack recovery. Movement and contact damage remain disabled during recovery.")]
    [Min(0f)]
    public float specialAttackRecovery = 0.3f;

    [Header("Rush Special Attack")]
    [Min(0.01f)]
    public float rushSpeed = 7f;

    [Min(0.01f)]
    public float rushDuration = 0.45f;

    [Tooltip("Rush damage. If 0, this enemy's attack value is used.")]
    [Min(0)]
    public int rushDamage = 0;

    [Min(0.01f)]
    public float rushHitRadius = 0.45f;

    [Header("Jump Special Attack")]
    [Min(0.01f)]
    public float jumpDuration = 0.35f;

    [Tooltip("Landing impact damage. If 0, this enemy's attack value is used.")]
    [Min(0)]
    public int jumpDamage = 0;

    [Min(0.01f)]
    public float jumpImpactRadius = 1.1f;

    [Min(0.01f)]
    public float jumpMaxDistance = 4f;

    [Tooltip("If true, jump landing candidates are restricted to the enemy's current room.")]
    public bool jumpStayInRoom = true;

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

    [Header("Status Resistance")]
    [Range(0f, 1f)]
    public float knockbackResistance = 0f;

    [Header("Ranged Movement")]
    [Tooltip("Ranged 이동 스타일. Chase는 기존 추격 동작과 동일합니다.")]
    public RangedMovementType rangedMovementType = RangedMovementType.Chase;

    [Tooltip("Kiting: 이 거리보다 멀어지면 Player에게 접근합니다. Random에서는 사용하지 않습니다.")]
    [Min(0f)]
    public float preferredRange = 5f;

    [Tooltip("Kiting: 이 거리보다 가까워지면 Player 반대 방향으로 후퇴합니다.")]
    [Min(0f)]
    public float kiteRetreatRange = 3f;

    [Tooltip("Random: 새 목적지를 다시 고르는 최소 간격(초).")]
    [Min(0f)]
    public float randomMoveIntervalMin = 0.8f;

    [Tooltip("Random: 새 목적지를 다시 고르는 최대 간격(초).")]
    [Min(0f)]
    public float randomMoveIntervalMax = 1.8f;

    [Tooltip("Random: 현재 위치 기준 새 목적지를 고르는 반경(월드 단위).")]
    [Min(0f)]
    public float randomMoveRadius = 3f;
}
