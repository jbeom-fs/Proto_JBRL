using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemySkill", menuName = "JBRogLike/Enemy/Enemy Skill")]
public sealed class EnemySkillData : EnemyPatternData
{
    [Header("Execution")]
    [SerializeField] private EnemySkillExecutionType executionType = EnemySkillExecutionType.Jump;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float castDelay = 0.45f;

    [Header("Range")]
    [SerializeField] private PatternRangeData searchRange = new();
    [SerializeField] private PatternRangeData damageRange = new();

    [Header("Impact")]
    [SerializeField, Min(0)] private int damage = 3;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 8f;
    [SerializeField, Min(0f)] private float jumpVisualHeight = 1f;
    [SerializeField] private bool stayInRoom = true;
    [SerializeField] private bool lockFacingDuringExecute = true;

    [Header("Animation")]
    [SerializeField] private EnemyAnimationKey castAnimation = EnemyAnimationKey.Jump;
    [SerializeField] private string castAnimationTrigger;
    [SerializeField] private EnemyAnimationKey executeAnimation = EnemyAnimationKey.Jump;
    [SerializeField] private string executeAnimationTrigger;

    public EnemySkillExecutionType ExecutionType => executionType;
    public float CastDelay => Mathf.Max(0f, castDelay);
    public PatternRangeData SearchRange => searchRange;
    public PatternRangeData DamageRange => damageRange;
    public int Damage => Mathf.Max(0, damage);
    public float MoveSpeed => Mathf.Max(0f, moveSpeed);
    public float JumpVisualHeight => Mathf.Max(0f, jumpVisualHeight);
    public bool StayInRoom => stayInRoom;
    public bool LockFacingDuringExecute => lockFacingDuringExecute;
    public EnemyAnimationKey CastAnimation => castAnimation;
    public string CastAnimationTrigger => castAnimationTrigger;
    public EnemyAnimationKey ExecuteAnimation => executeAnimation;
    public string ExecuteAnimationTrigger => executeAnimationTrigger;

    public override EnemyPatternRuntime CreateRuntime() => new EnemySkillRuntime(this);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    protected override void OnValidate()
    {
        base.OnValidate();

        if (searchRange != null &&
            searchRange.PatternType == AttackPatternType.Custom &&
            (searchRange.CustomCells == null || searchRange.CustomCells.Count == 0))
        {
            Debug.LogWarning($"[EnemySkillData] {name}: Custom searchRange requires at least one custom cell.", this);
        }

        if (damageRange != null &&
            damageRange.PatternType == AttackPatternType.Custom &&
            (damageRange.CustomCells == null || damageRange.CustomCells.Count == 0))
        {
            Debug.LogWarning($"[EnemySkillData] {name}: Custom damageRange requires at least one custom cell.", this);
        }

        if (executionType == EnemySkillExecutionType.Jump && moveSpeed <= 0f)
            Debug.LogWarning($"[EnemySkillData] {name}: Jump moveSpeed must be greater than 0.", this);

        if (searchRange != null &&
            searchRange.PatternType != AttackPatternType.Custom &&
            searchRange.PatternRange == 0)
        {
            Debug.LogWarning($"[EnemySkillData] {name}: Non-custom searchRange must be greater than 0.", this);
        }

        if (searchRange != null && MaxRange > searchRange.PatternRange)
        {
            Debug.LogWarning(
                $"[EnemySkillData] {name}: maxRange({MaxRange})가 searchRange({searchRange.PatternRange})보다 큽니다. " +
                "도달할 수 없는 거리에서 패턴이 선택됩니다.",
                this);
        }
    }
#endif
}
