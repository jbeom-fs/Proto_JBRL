using UnityEngine;

[CreateAssetMenu(fileName = "NewEliteJumpPattern", menuName = "JBRogLike/Enemy/Elite Jump Pattern")]
public sealed class EliteJumpPatternData : ElitePatternData
{
    [Header("Timing")]
    [SerializeField] private float windup = 0.45f;
    [SerializeField] private float jumpDuration = 0.55f;

    [Header("Jump")]
    [SerializeField] private float maxDistance = 5f;
    [SerializeField] private int impactDamage = 3;
    [SerializeField] private float impactRadius = 1.25f;
    [SerializeField] private bool stayInRoom = true;
    [SerializeField] private bool lockFacingDuringJump = true;

    [Header("Animation")]
    [SerializeField] private EnemyAnimationKey windupAnimation = EnemyAnimationKey.Jump;
    [SerializeField] private EnemyAnimationKey jumpAnimation = EnemyAnimationKey.Jump;

    public float Windup => Mathf.Max(0f, windup);
    public float JumpDuration => Mathf.Max(0f, jumpDuration);
    public float MaxDistance => Mathf.Max(0.01f, maxDistance);
    public int ImpactDamage => Mathf.Max(0, impactDamage);
    public float ImpactRadius => Mathf.Max(0.01f, impactRadius);
    public bool StayInRoom => stayInRoom;
    public bool LockFacingDuringJump => lockFacingDuringJump;
    public EnemyAnimationKey WindupAnimation => windupAnimation;
    public EnemyAnimationKey JumpAnimation => jumpAnimation;

    public override ElitePatternRuntime CreateRuntime()
    {
        return new EliteJumpPatternRuntime(this);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    protected override void OnValidate()
    {
        base.OnValidate();

        if (windup < 0f)
            Debug.LogWarning($"[EliteJumpPatternData] {name}: windup is negative.", this);
        if (jumpDuration < 0f)
            Debug.LogWarning($"[EliteJumpPatternData] {name}: jumpDuration is negative.", this);
        if (maxDistance <= 0f)
            Debug.LogWarning($"[EliteJumpPatternData] {name}: maxDistance must be greater than 0.", this);
        if (impactDamage < 0)
            Debug.LogWarning($"[EliteJumpPatternData] {name}: impactDamage is negative.", this);
        if (impactRadius <= 0f)
            Debug.LogWarning($"[EliteJumpPatternData] {name}: impactRadius must be greater than 0.", this);
    }
#endif
}
