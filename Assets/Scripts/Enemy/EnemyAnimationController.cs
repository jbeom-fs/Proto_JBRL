using UnityEngine;

public class EnemyAnimationController : MonoBehaviour
{
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int AttackTriggerHash = Animator.StringToHash("AttackTrigger");
    private static readonly int ChargeTriggerHash = Animator.StringToHash("ChargeTrigger");
    private static readonly int RushTriggerHash = Animator.StringToHash("RushTrigger");
    private static readonly int JumpTriggerHash = Animator.StringToHash("JumpTrigger");
    private static readonly int LandTriggerHash = Animator.StringToHash("LandTrigger");
    private static readonly int DeathTriggerHash = Animator.StringToHash("DeathTrigger");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int LastMoveXHash = Animator.StringToHash("LastMoveX");
    private static readonly int LastMoveYHash = Animator.StringToHash("LastMoveY");

    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float movementThreshold = 0.001f;
    [SerializeField] private bool defaultFacesRight = true;
    [SerializeField] private bool faceMoveDirectionWhenMoving = true;
    [SerializeField] private bool faceTargetOnAttack = true;
    [SerializeField] private bool faceTargetWhileChasing = false;
    [SerializeField] private float facingDeadZone = 0.03f;

    private Vector3 _previousPosition;
    private bool _isDead;
    private bool _targetFacingAppliedThisFrame;
    private bool _facingLocked;
    private bool _lockedFacingRight;
    private bool _hasIsMoving;
    private bool _hasAttackTrigger;
    private bool _hasChargeTrigger;
    private bool _hasRushTrigger;
    private bool _hasJumpTrigger;
    private bool _hasLandTrigger;
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
        _targetFacingAppliedThisFrame = false;
        _previousPosition = transform.position;
    }

    private void LateUpdate()
    {
        Vector3 currentPosition = transform.position;
        Vector2 delta = currentPosition - _previousPosition;
        bool isMoving = delta.sqrMagnitude > movementThreshold * movementThreshold;

        if (animator != null)
            SetBool(IsMovingHash, _hasIsMoving, isMoving);

        if (isMoving)
        {
            Vector2 direction = delta.normalized;
            if (animator != null)
            {
                SetFloat(MoveXHash, _hasMoveX, direction.x);
                SetFloat(MoveYHash, _hasMoveY, direction.y);
                SetFloat(LastMoveXHash, _hasLastMoveX, direction.x);
                SetFloat(LastMoveYHash, _hasLastMoveY, direction.y);
            }

            if (!_isDead && !_facingLocked && faceMoveDirectionWhenMoving && !_targetFacingAppliedThisFrame)
                FaceHorizontalDirection(delta.x);
        }

        _targetFacingAppliedThisFrame = false;
        _previousPosition = currentPosition;
    }

    public bool FaceTargetWhileChasing => faceTargetWhileChasing;

    public void ResetAnimationState()
    {
        ResolveDependencies();
        CacheAnimatorParameters();
        _isDead = false;
        _targetFacingAppliedThisFrame = false;
        _facingLocked = false;

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;
            SetFacingRight(defaultFacesRight);
        }

        if (animator == null)
            return;

        animator.ResetTrigger(AttackTriggerHash);
        ResetTrigger(ChargeTriggerHash, _hasChargeTrigger);
        ResetTrigger(RushTriggerHash, _hasRushTrigger);
        ResetTrigger(JumpTriggerHash, _hasJumpTrigger);
        ResetTrigger(LandTriggerHash, _hasLandTrigger);
        animator.ResetTrigger(DeathTriggerHash);
        SetBool(IsMovingHash, _hasIsMoving, false);
        SetFloat(MoveXHash, _hasMoveX, 0f);
        SetFloat(MoveYHash, _hasMoveY, 0f);
        SetFloat(LastMoveXHash, _hasLastMoveX, 0f);
        SetFloat(LastMoveYHash, _hasLastMoveY, -1f);

        if (!animator.gameObject.activeInHierarchy)
        {
            _previousPosition = transform.position;
            return;
        }

        animator.Rebind();
        animator.Update(0f);
        animator.Play("Idle", 0, 0f);
        _previousPosition = transform.position;
    }

    public void TriggerAttack()
    {
        PlayAttack();
    }

    public void PlayAttack()
    {
        if (animator == null || !_hasAttackTrigger)
            return;

        animator.ResetTrigger(AttackTriggerHash);
        animator.SetTrigger(AttackTriggerHash);
    }

    public void PlayAttack(Vector3 targetPosition)
    {
        if (faceTargetOnAttack)
            FacePosition(targetPosition);

        PlayAttack();
    }

    public void PlayCharge(Vector3 targetPosition)
    {
        if (faceTargetOnAttack)
            FacePosition(targetPosition);

        SetTriggerOrAttack(ChargeTriggerHash, _hasChargeTrigger);
    }

    public void PlayRush()
    {
        SetTriggerOrAttack(RushTriggerHash, _hasRushTrigger);
    }

    public void PlayJump()
    {
        SetTriggerOrAttack(JumpTriggerHash, _hasJumpTrigger);
    }

    public void PlayLand()
    {
        SetTriggerOrAttack(LandTriggerHash, _hasLandTrigger);
    }

    public void LockFacing(Vector2 direction)
    {
        if (_isDead)
            return;

        if (Mathf.Abs(direction.x) <= facingDeadZone)
        {
            _lockedFacingRight = spriteRenderer == null || spriteRenderer.flipX != defaultFacesRight;
            _facingLocked = true;
            _targetFacingAppliedThisFrame = true;
            return;
        }

        _lockedFacingRight = direction.x > 0f;
        _facingLocked = true;
        SetFacingRight(_lockedFacingRight);
        _targetFacingAppliedThisFrame = true;
    }

    public void UnlockFacing()
    {
        _facingLocked = false;
    }

    public void TriggerDeath()
    {
        PlayDeath();
    }

    public void PlayDeath()
    {
        _isDead = true;
        _facingLocked = false;

        if (animator == null || !_hasDeathTrigger)
            return;

        ResetTrigger(AttackTriggerHash, _hasAttackTrigger);
        ResetTrigger(ChargeTriggerHash, _hasChargeTrigger);
        ResetTrigger(RushTriggerHash, _hasRushTrigger);
        ResetTrigger(JumpTriggerHash, _hasJumpTrigger);
        ResetTrigger(LandTriggerHash, _hasLandTrigger);
        animator.SetTrigger(DeathTriggerHash);
    }

    private void ResolveDependencies()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
    }

    public void FacePosition(Vector3 targetPosition)
    {
        if (_isDead || _facingLocked)
            return;

        if (faceTargetWhileChasing)
            _targetFacingAppliedThisFrame = true;

        FaceHorizontalDirection(targetPosition.x - transform.position.x);
    }

    private void FaceHorizontalDirection(float directionX)
    {
        if (Mathf.Abs(directionX) <= facingDeadZone)
            return;

        SetFacingRight(directionX > 0f);
    }

    private void SetFacingRight(bool faceRight)
    {
        if (spriteRenderer == null)
            return;

        spriteRenderer.flipX = defaultFacesRight != faceRight;
    }

    private void CacheAnimatorParameters()
    {
        _hasIsMoving = false;
        _hasAttackTrigger = false;
        _hasChargeTrigger = false;
        _hasRushTrigger = false;
        _hasJumpTrigger = false;
        _hasLandTrigger = false;
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
            if (parameter.nameHash == ChargeTriggerHash) _hasChargeTrigger = true;
            if (parameter.nameHash == RushTriggerHash) _hasRushTrigger = true;
            if (parameter.nameHash == JumpTriggerHash) _hasJumpTrigger = true;
            if (parameter.nameHash == LandTriggerHash) _hasLandTrigger = true;
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

    private void SetTriggerOrAttack(int hash, bool hasParameter)
    {
        if (animator == null)
            return;

        if (hasParameter)
        {
            animator.ResetTrigger(hash);
            animator.SetTrigger(hash);
            return;
        }

        PlayAttack();
    }

    private void ResetTrigger(int hash, bool hasParameter)
    {
        if (animator != null && hasParameter)
            animator.ResetTrigger(hash);
    }
}
