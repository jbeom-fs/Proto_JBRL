using UnityEngine;

public class EnemyAnimationController : MonoBehaviour
{
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int AttackTriggerHash = Animator.StringToHash("AttackTrigger");
    private static readonly int DeathTriggerHash = Animator.StringToHash("DeathTrigger");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int LastMoveXHash = Animator.StringToHash("LastMoveX");
    private static readonly int LastMoveYHash = Animator.StringToHash("LastMoveY");

    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float movementThreshold = 0.001f;

    private Vector3 _previousPosition;
    private bool _hasIsMoving;
    private bool _hasAttackTrigger;
    private bool _hasDeathTrigger;
    private bool _hasMoveX;
    private bool _hasMoveY;
    private bool _hasLastMoveX;
    private bool _hasLastMoveY;

    private void Awake()
    {
        ResolveDependencies();
        CacheAnimatorParameters();
        _previousPosition = transform.position;
    }

    private void OnEnable()
    {
        _previousPosition = transform.position;
    }

    private void LateUpdate()
    {
        if (animator == null)
            return;

        Vector3 currentPosition = transform.position;
        Vector2 delta = currentPosition - _previousPosition;
        bool isMoving = delta.sqrMagnitude > movementThreshold * movementThreshold;

        SetBool(IsMovingHash, _hasIsMoving, isMoving);

        if (isMoving)
        {
            Vector2 direction = delta.normalized;
            SetFloat(MoveXHash, _hasMoveX, direction.x);
            SetFloat(MoveYHash, _hasMoveY, direction.y);
            SetFloat(LastMoveXHash, _hasLastMoveX, direction.x);
            SetFloat(LastMoveYHash, _hasLastMoveY, direction.y);
        }

        _previousPosition = currentPosition;
    }

    public void ResetAnimationState()
    {
        ResolveDependencies();
        CacheAnimatorParameters();

        if (spriteRenderer != null)
            spriteRenderer.enabled = true;

        if (animator == null)
            return;

        animator.ResetTrigger(AttackTriggerHash);
        animator.ResetTrigger(DeathTriggerHash);
        SetBool(IsMovingHash, _hasIsMoving, false);
        SetFloat(MoveXHash, _hasMoveX, 0f);
        SetFloat(MoveYHash, _hasMoveY, 0f);
        SetFloat(LastMoveXHash, _hasLastMoveX, 0f);
        SetFloat(LastMoveYHash, _hasLastMoveY, -1f);
        animator.Rebind();
        animator.Update(0f);
        animator.Play("Idle", 0, 0f);
        _previousPosition = transform.position;
    }

    public void TriggerAttack()
    {
        if (animator == null || !_hasAttackTrigger)
            return;

        animator.ResetTrigger(AttackTriggerHash);
        animator.SetTrigger(AttackTriggerHash);
    }

    public void TriggerDeath()
    {
        if (animator == null || !_hasDeathTrigger)
            return;

        animator.ResetTrigger(AttackTriggerHash);
        animator.SetTrigger(DeathTriggerHash);
    }

    private void ResolveDependencies()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
    }

    private void CacheAnimatorParameters()
    {
        _hasIsMoving = false;
        _hasAttackTrigger = false;
        _hasDeathTrigger = false;
        _hasMoveX = false;
        _hasMoveY = false;
        _hasLastMoveX = false;
        _hasLastMoveY = false;

        if (animator == null)
            return;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == IsMovingHash) _hasIsMoving = true;
            if (parameter.nameHash == AttackTriggerHash) _hasAttackTrigger = true;
            if (parameter.nameHash == DeathTriggerHash) _hasDeathTrigger = true;
            if (parameter.nameHash == MoveXHash) _hasMoveX = true;
            if (parameter.nameHash == MoveYHash) _hasMoveY = true;
            if (parameter.nameHash == LastMoveXHash) _hasLastMoveX = true;
            if (parameter.nameHash == LastMoveYHash) _hasLastMoveY = true;
        }
    }

    private void SetBool(int hash, bool hasParameter, bool value)
    {
        if (hasParameter)
            animator.SetBool(hash, value);
    }

    private void SetFloat(int hash, bool hasParameter, float value)
    {
        if (hasParameter)
            animator.SetFloat(hash, value);
    }
}
