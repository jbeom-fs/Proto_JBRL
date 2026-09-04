using System.Collections.Generic;
using UnityEngine;

public class EnemyAnimationController : MonoBehaviour
{
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int AttackTriggerHash = Animator.StringToHash("AttackTrigger");
    private static readonly int ProjectileTriggerHash = Animator.StringToHash("ProjectileTrigger");
    private static readonly int DashTriggerHash = Animator.StringToHash("DashTrigger");
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
    private readonly HashSet<int> _presentParameters = new HashSet<int>();

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
            SetBool(IsMovingHash, isMoving);

        if (isMoving)
        {
            Vector2 direction = delta.normalized;
            if (animator != null)
            {
                SetFloat(MoveXHash, direction.x);
                SetFloat(MoveYHash, direction.y);
                SetFloat(LastMoveXHash, direction.x);
                SetFloat(LastMoveYHash, direction.y);
            }

            if (!_isDead && !_facingLocked && faceMoveDirectionWhenMoving && !_targetFacingAppliedThisFrame)
                FaceHorizontalDirection(delta.x);
        }

        _targetFacingAppliedThisFrame = false;
        _previousPosition = currentPosition;
    }

    public bool FaceTargetWhileChasing => faceTargetWhileChasing;
    public Transform VisualRoot
    {
        get
        {
            ResolveDependencies();
            return spriteRenderer != null ? spriteRenderer.transform : null;
        }
    }

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
        ResetTrigger(ProjectileTriggerHash);
        ResetTrigger(DashTriggerHash);
        ResetTrigger(ChargeTriggerHash);
        ResetTrigger(RushTriggerHash);
        ResetTrigger(JumpTriggerHash);
        ResetTrigger(LandTriggerHash);
        animator.ResetTrigger(DeathTriggerHash);
        SetBool(IsMovingHash, false);
        SetFloat(MoveXHash, 0f);
        SetFloat(MoveYHash, 0f);
        SetFloat(LastMoveXHash, 0f);
        SetFloat(LastMoveYHash, -1f);

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
        if (animator == null || !HasParameter(AttackTriggerHash))
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

        SetTriggerOrAttack(ChargeTriggerHash);
    }

    public void PlayRush()
    {
        SetTriggerOrAttack(RushTriggerHash);
    }

    public void PlayJump()
    {
        SetTriggerOrAttack(JumpTriggerHash);
    }

    public void PlayLand()
    {
        SetTriggerOrAttack(LandTriggerHash);
    }

    public void PlayPatternAnimation(EnemyAnimationKey key)
    {
        PlayPatternAnimation(key, transform.position);
    }

    public void PlayPatternAnimation(EnemyAnimationKey key, Vector3 targetPosition)
    {
        if (_isDead || key == EnemyAnimationKey.None)
            return;

        switch (key)
        {
            case EnemyAnimationKey.Attack:
                PlayAttack(targetPosition);
                break;

            case EnemyAnimationKey.Projectile:
                if (faceTargetOnAttack)
                    FacePosition(targetPosition);

                SetTriggerOrAttack(ProjectileTriggerHash);
                break;

            case EnemyAnimationKey.Dash:
                SetTriggerOrAttack(DashTriggerHash);
                break;

            case EnemyAnimationKey.Charge:
                PlayCharge(targetPosition);
                break;

            case EnemyAnimationKey.Rush:
                PlayRush();
                break;

            case EnemyAnimationKey.Jump:
                PlayJump();
                break;

            case EnemyAnimationKey.Land:
                PlayLand();
                break;
        }
    }

    public void PlayPatternAnimation(EnemyAnimationKey key, string customTrigger, Vector3 targetPosition)
    {
        if (_isDead)
            return;

        if (!string.IsNullOrWhiteSpace(customTrigger))
        {
            SetTriggerOrAttack(Animator.StringToHash(customTrigger));
            return;
        }

        PlayPatternAnimation(key, targetPosition);
    }

    public void LockSpecialFacing(Vector2 direction)
    {
        LockFacing(direction);
    }

    public void UnlockSpecialFacing()
    {
        UnlockFacing();
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

        if (animator == null || !HasParameter(DeathTriggerHash))
            return;

        ResetTrigger(AttackTriggerHash);
        ResetTrigger(ProjectileTriggerHash);
        ResetTrigger(DashTriggerHash);
        ResetTrigger(ChargeTriggerHash);
        ResetTrigger(RushTriggerHash);
        ResetTrigger(JumpTriggerHash);
        ResetTrigger(LandTriggerHash);
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
        _presentParameters.Clear();

        if (animator == null)
            return;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
            _presentParameters.Add(parameter.nameHash);
    }

    private bool HasParameter(int hash) => _presentParameters.Contains(hash);

    private void SetBool(int hash, bool value)
    {
        if (HasParameter(hash))
            animator.SetBool(hash, value);
    }

    private void SetFloat(int hash, float value)
    {
        if (HasParameter(hash))
            animator.SetFloat(hash, value);
    }

    private void SetTriggerOrAttack(int hash)
    {
        if (animator == null)
            return;

        if (HasParameter(hash))
        {
            animator.ResetTrigger(hash);
            animator.SetTrigger(hash);
            return;
        }

        PlayAttack();
    }

    private void ResetTrigger(int hash)
    {
        if (animator != null && HasParameter(hash))
            animator.ResetTrigger(hash);
    }
}
