using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemySkill", menuName = "JBRogLike/Enemy/Enemy Skill")]
public sealed class EnemySkillData : EnemyPatternData
{
    [Header("Execution")]
    [SerializeField] private EnemySkillExecutionType executionType = EnemySkillExecutionType.Jump;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float castDelay = 0.45f;

    [Header("Range")]
    [SerializeField] private PatternShapeData searchShape = new();

    [Header("Impact")]
    [SerializeField] private PatternShapeData damageShape = new();
    [SerializeField, Min(0)] private int damageRange = 3;
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
    public PatternShapeData SearchShape => searchShape;
    public PatternShapeData DamageShape => damageShape;
    public int DamageRange => Mathf.Max(0, damageRange);
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

        if (searchShape != null &&
            searchShape.PatternType == AttackPatternType.Custom &&
            (searchShape.CustomCells == null || searchShape.CustomCells.Count == 0))
        {
            Debug.LogWarning($"[EnemySkillData] {name}: Custom searchShape requires at least one custom cell.", this);
        }

        if (damageShape != null &&
            damageShape.PatternType == AttackPatternType.Custom &&
            (damageShape.CustomCells == null || damageShape.CustomCells.Count == 0))
        {
            Debug.LogWarning($"[EnemySkillData] {name}: Custom damageShape requires at least one custom cell.", this);
        }

        if (executionType == EnemySkillExecutionType.Jump && moveSpeed <= 0f)
            Debug.LogWarning($"[EnemySkillData] {name}: Jump moveSpeed must be greater than 0.", this);

        if (executionType == EnemySkillExecutionType.Jump && MaxRange < 1f)
            Debug.LogWarning($"[EnemySkillData] {name}: maxRange가 1보다 작아 착지 후보가 생기지 않습니다.", this);
    }
#endif
}
