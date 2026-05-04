using UnityEngine;

public enum EnemyBehaviorType
{
    Contact,
    Ranged
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

    [Header("Contact Behavior")]
    [Tooltip("Fallback contact damage distance used when collider contact cannot be checked.")]
    public float contactDamageRadius = 0.45f;

    [Tooltip("Contact damage is applied when collider distance is less than or equal to this value.")]
    public float contactDamageSkin = 0.05f;

    [Header("Status Resistance")]
    [Range(0f, 1f)]
    public float knockbackResistance = 0f;
}
