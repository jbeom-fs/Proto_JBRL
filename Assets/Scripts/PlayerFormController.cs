using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerAnimationController))]
public sealed class PlayerFormController : MonoBehaviour
{
    private const float FacingDeadZone = 0.01f;
    private static readonly int AttackTriggerHash = Animator.StringToHash("AttackTrigger");
    private static readonly int DashTriggerHash = Animator.StringToHash("DashTrigger");
    private static readonly int DashStateHash = Animator.StringToHash("Dash");

    [Header("Dependencies")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform visualTransform;

    [Header("Form")]
    [SerializeField] private PlayerFormData defaultForm;

    public PlayerFormData CurrentForm { get; private set; }

    private Quaternion _defaultVisualRotation = Quaternion.identity;
    private bool _dashPreFlipX;
    private bool _hasAttackTrigger;
    private bool _hasDashTrigger;
    private bool _dashVisualRotationActive;
    private bool _dashStateObserved;
    private bool _dashMovementCompleted;
    private int _dashVisualVersion;
    private int _activeDashVisualToken;
    private int _dashVisualStartFrame;

    private void Awake()
    {
        CacheVisualDefaults();

        if (defaultForm != null)
            ApplyForm(defaultForm);
    }

    private void LateUpdate()
    {
        if (!_dashVisualRotationActive)
            return;

        if (Time.frameCount <= _dashVisualStartFrame)
            return;

        if (IsAnimatorInDashState())
        {
            _dashStateObserved = true;
            return;
        }

        if (_dashMovementCompleted)
            ResetDashVisualRotation(_activeDashVisualToken);
    }

    public void ApplyForm(PlayerFormData formData)
    {
        if (formData == null)
        {
            Debug.LogWarning("[PlayerFormController] Form data is missing.", this);
            return;
        }

        if (spriteRenderer == null || animator == null)
        {
            Debug.LogError("[PlayerFormController] SpriteRenderer and Animator must be assigned in Inspector.", this);
            return;
        }

        CurrentForm = formData;
        ResetDashVisualRotation();

        if (formData.DefaultSprite != null)
            spriteRenderer.sprite = formData.DefaultSprite;

        if (formData.AnimatorController != null && animator.runtimeAnimatorController != formData.AnimatorController)
        {
            animator.runtimeAnimatorController = formData.AnimatorController;
            animator.Rebind();
            animator.Update(0f);
        }

        CacheAnimatorParameters();
    }

    public void ApplyFacing(Vector2 direction)
    {
        if (CurrentForm == null || spriteRenderer == null)
            return;

        if (_dashVisualRotationActive)
            return;

        if (!CurrentForm.UseHorizontalFlipForFacing)
            return;

        if (Mathf.Abs(direction.x) <= FacingDeadZone)
            return;

        spriteRenderer.flipX = ResolveFlipX(direction);
    }

    public int TriggerDashAnimation(Vector2 direction)
    {
        int token = BeginDashVisualToken();
        ApplyFacing(direction);
        ApplyDashVisualRotation(direction);

        if (_hasDashTrigger)
            SetTrigger(DashTriggerHash);

        return token;
    }

    public void ResetDashAnimationVisual()
    {
        ResetDashVisualRotation();
    }

    public void CompleteDashAnimationVisual(int token)
    {
        if (token == 0 || token != _activeDashVisualToken)
            return;

        _dashMovementCompleted = true;

        if (Time.frameCount > _dashVisualStartFrame &&
            (!_dashStateObserved || animator == null || !IsAnimatorInDashState()))
        {
            ResetDashVisualRotation(token);
        }
    }

    public void TriggerAttackAnimation(Vector2 direction)
    {
        ResetDashVisualRotation();
        ApplyFacing(direction);

        if (_hasAttackTrigger)
            SetTrigger(AttackTriggerHash);
    }

    private void SetTrigger(int triggerHash)
    {
        if (animator == null)
            return;

        animator.ResetTrigger(triggerHash);
        animator.SetTrigger(triggerHash);
    }

    private void CacheAnimatorParameters()
    {
        _hasAttackTrigger = false;
        _hasDashTrigger = false;
        if (animator == null)
            return;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type != AnimatorControllerParameterType.Trigger)
                continue;

            if (parameter.nameHash == AttackTriggerHash)
                _hasAttackTrigger = true;
            else if (parameter.nameHash == DashTriggerHash)
                _hasDashTrigger = true;
        }
    }

    private void CacheVisualDefaults()
    {
        if (visualTransform == null && spriteRenderer != null)
            visualTransform = spriteRenderer.transform;

        if (visualTransform != null)
            _defaultVisualRotation = visualTransform.localRotation;

    }

    private void ApplyDashVisualRotation(Vector2 direction)
    {
        if (CurrentForm == null ||
            !CurrentForm.RotateDashAnimationByDirection ||
            spriteRenderer == null ||
            visualTransform == null ||
            direction.sqrMagnitude <= FacingDeadZone * FacingDeadZone)
        {
            return;
        }

        bool wasDashVisualRotationActive = _dashVisualRotationActive;
        _dashVisualRotationActive = true;
        _dashStateObserved = false;
        _dashMovementCompleted = false;
        if (Mathf.Abs(direction.x) > FacingDeadZone)
            _dashPreFlipX = ResolveFlipX(direction);
        else if (!wasDashVisualRotationActive)
            _dashPreFlipX = spriteRenderer.flipX;

        spriteRenderer.flipX = false;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + CurrentForm.DashBaseAngle;
        visualTransform.localRotation = _defaultVisualRotation * Quaternion.Euler(0f, 0f, angle);
    }

    private bool ResolveFlipX(Vector2 direction)
    {
        return CurrentForm.DefaultSpriteFacesRight
            ? direction.x < 0f
            : direction.x > 0f;
    }

    private void ResetDashVisualRotation()
    {
        ResetDashVisualRotation(_activeDashVisualToken);
    }

    private void ResetDashVisualRotation(int token)
    {
        if (token != _activeDashVisualToken)
            return;

        if (visualTransform != null)
            visualTransform.localRotation = _defaultVisualRotation;

        if (_dashVisualRotationActive && spriteRenderer != null)
            spriteRenderer.flipX = _dashPreFlipX;

        _dashVisualRotationActive = false;
        _dashStateObserved = false;
        _dashMovementCompleted = false;
    }

    private int BeginDashVisualToken()
    {
        _dashVisualVersion++;
        if (_dashVisualVersion == 0)
            _dashVisualVersion = 1;

        _activeDashVisualToken = _dashVisualVersion;
        _dashVisualStartFrame = Time.frameCount;
        return _activeDashVisualToken;
    }

    private bool IsAnimatorInDashState()
    {
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        if (current.shortNameHash == DashStateHash)
            return true;

        if (!animator.IsInTransition(0))
            return false;

        AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
        return next.shortNameHash == DashStateHash;
    }
}
