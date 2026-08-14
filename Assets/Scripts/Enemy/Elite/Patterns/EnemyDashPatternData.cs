using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemyDashPattern", menuName = "JBRogLike/Enemy/Enemy Dash Pattern")]
public sealed class EnemyDashPatternData : EnemyPatternData
{
    [Header("Timing")]
    [SerializeField] private float windup = 0.35f;

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 8f;
    [SerializeField] private int damage = 2;
    [SerializeField] private float hitRadius = 0.45f;
    [SerializeField] private bool stopOnWall = true;
    [SerializeField] private bool lockFacingDuringDash = true;

    [Header("Animation")]
    [SerializeField] private EnemyAnimationKey windupAnimation = EnemyAnimationKey.Dash;
    [SerializeField] private EnemyAnimationKey dashAnimation = EnemyAnimationKey.Dash;

    public float Windup => Mathf.Max(0f, windup);
    public float DashSpeed => Mathf.Max(0f, dashSpeed);
    public int Damage => Mathf.Max(0, damage);
    public float HitRadius => Mathf.Max(0.01f, hitRadius);
    public bool StopOnWall => stopOnWall;
    public bool LockFacingDuringDash => lockFacingDuringDash;
    public EnemyAnimationKey WindupAnimation => windupAnimation;
    public EnemyAnimationKey DashAnimation => dashAnimation;

    public override EnemyPatternRuntime CreateRuntime()
    {
        return new EnemyDashPatternRuntime(this);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    protected override void OnValidate()
    {
        base.OnValidate();

        if (windup < 0f)
            Debug.LogWarning($"[EnemyDashPatternData] {name}: windup is negative.", this);
        if (dashSpeed <= 0f)
            Debug.LogWarning($"[EnemyDashPatternData] {name}: dashSpeed must be greater than 0.", this);
        if (damage < 0)
            Debug.LogWarning($"[EnemyDashPatternData] {name}: damage is negative.", this);
        if (hitRadius <= 0f)
            Debug.LogWarning($"[EnemyDashPatternData] {name}: hitRadius must be greater than 0.", this);
    }
#endif
}
