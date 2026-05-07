using UnityEngine;

public sealed class ProjectileFireRequest
{
    public GameObject ProjectilePrefab;
    public Transform OriginTransform;
    public MonoBehaviour CoroutineRunner;
    public IDamageable Caster;
    public Object Owner;
    public Vector2 Direction;
    public int Damage;
    public float Speed;
    public float Lifetime;
    public int ProjectileCount = 1;
    public float SpreadAngle;
    public ProjectileFirePattern FirePattern = ProjectileFirePattern.Single;
    public ProjectileWallHitMode WallHitMode = ProjectileWallHitMode.Destroy;
    public ProjectileTargetHitMode TargetHitMode = ProjectileTargetHitMode.DestroyOnHit;
    public ProjectileController.TargetMode TargetMode = ProjectileController.TargetMode.Player;
    public int MaxBounceCount;
    public float SpawnOffset;
    public float BurstInterval;
    public float KnockbackForce;
    public float KnockbackDuration;
    public float SlowPercentage;
    public float SlowDuration;
}
