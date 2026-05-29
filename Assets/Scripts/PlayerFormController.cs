using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerAnimationController))]
public sealed class PlayerFormController : MonoBehaviour
{
    private const float FacingDeadZone = 0.01f;
    private static readonly int AttackTriggerHash = Animator.StringToHash("AttackTrigger");
    private static readonly int SpinTriggerHash = Animator.StringToHash("SpinTrigger");
    private static readonly int DashTriggerHash = Animator.StringToHash("DashTrigger");
    private static readonly int DeathTriggerHash = Animator.StringToHash("DeathTrigger");
    private static readonly int DashStateHash = Animator.StringToHash("Dash");

    [Header("Dependencies")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform visualTransform;

    [Header("Form")]
    [SerializeField] private PlayerFormData defaultForm;
    [SerializeField] private PlayerFormData currentForm;

    public PlayerFormData CurrentForm => currentForm;

    private PlayerFormData _appliedForm;
    private Quaternion _defaultVisualRotation = Quaternion.identity;
    private bool _dashPreFlipX;
    private bool _hasAttackTrigger;
    private bool _hasSpinTrigger;
    private bool _hasDashTrigger;
    private bool _hasDeathTrigger;
    private bool _dashVisualRotationActive;
    private bool _dashStateObserved;
    private bool _dashMovementCompleted;
    private int _dashVisualVersion;
    private int _activeDashVisualToken;
    private int _dashVisualStartFrame;
    private bool _isInitialized;
    private PlayerCombatController _combat;

    private void Awake()
    {
        CacheVisualDefaults();
        _combat = GetComponent<PlayerCombatController>();
        _isInitialized = true;

        PlayerFormData initialForm = currentForm != null ? currentForm : defaultForm;
        if (currentForm == null)
            currentForm = initialForm;

        ApplyForm(initialForm);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying || !_isInitialized)
            return;

        if (currentForm != _appliedForm)
            ApplyForm(currentForm);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[PlayerFormController] Form data is missing.", this);
#endif
            return;
        }

        if (spriteRenderer == null || animator == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError("[PlayerFormController] SpriteRenderer and Animator must be assigned in Inspector.", this);
#endif
            return;
        }

        if (_appliedForm == formData &&
            (formData.DefaultSprite == null || spriteRenderer.sprite == formData.DefaultSprite) &&
            (formData.AnimatorController == null || animator.runtimeAnimatorController == formData.AnimatorController))
        {
            currentForm = formData;
            return;
        }

        currentForm = formData;
        _appliedForm = formData;
        ResetDashVisualRotation();
        if (formData.DefaultWeapon != null && _combat != null)
            _combat.EquipWeapon(formData.DefaultWeapon);

        if (formData.DefaultSprite != null)
            spriteRenderer.sprite = formData.DefaultSprite;

        if (formData.AnimatorController != null && animator.runtimeAnimatorController != formData.AnimatorController)
        {
            animator.runtimeAnimatorController = formData.AnimatorController;
            animator.Rebind();
            animator.Update(0f);
        }

        CacheAnimatorParameters();
        ResetCachedTriggers();
    }

    public void SetCurrentForm(PlayerFormData formData)
    {
        if (formData == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[PlayerFormController] Cannot set current form because form data is missing.", this);
#endif
            return;
        }

        ApplyForm(formData);
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

    public int PlaySkillAnimation(SkillData skillData, Vector2 direction)
    {
        if (skillData == null)
            return 0;

        return PlaySkillAnimation(
            skillData.AnimationType,
            direction,
            skillData.RotateAnimationByDirection,
            skillData.AnimationBaseAngle,
            skillData.CustomAnimationTrigger);
    }

    public int PlaySkillAnimation(
        SkillAnimationType animationType,
        Vector2 direction,
        bool rotateByDirection,
        float baseAngle,
        string customTrigger)
    {
        switch (animationType)
        {
            case SkillAnimationType.Attack:
                PlayTriggerAnimation(AttackTriggerHash, _hasAttackTrigger, direction);
                return 0;

            case SkillAnimationType.Spin:
                PlayTriggerAnimation(SpinTriggerHash, _hasSpinTrigger, direction);
                return 0;

            case SkillAnimationType.Dash:
                return PlayDashAnimation(direction, rotateByDirection, baseAngle);

            case SkillAnimationType.CustomTrigger:
                PlayCustomTriggerAnimation(customTrigger, direction);
                return 0;

            case SkillAnimationType.None:
            default:
                return 0;
        }
    }

    public int TriggerDashAnimation(Vector2 direction)
    {
        bool rotateByDirection = currentForm != null && currentForm.RotateDashAnimationByDirection;
        float baseAngle = currentForm != null ? currentForm.DashBaseAngle : 0f;
        return PlaySkillAnimation(SkillAnimationType.Dash, direction, rotateByDirection, baseAngle, null);
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
        PlaySkillAnimation(SkillAnimationType.Attack, direction, false, 0f, null);
    }

    private int PlayDashAnimation(Vector2 direction, bool rotateByDirection, float baseAngle)
    {
        int token = BeginDashVisualToken();
        ApplyFacing(direction);
        ApplyDashVisualRotation(direction, rotateByDirection, baseAngle);

        if (_hasDashTrigger)
            SetTrigger(DashTriggerHash);

        return token;
    }

    private void PlayTriggerAnimation(int triggerHash, bool hasTrigger, Vector2 direction)
    {
        ResetDashVisualRotation();
        ApplyFacing(direction);

        if (hasTrigger)
            SetTrigger(triggerHash);
    }

    private void PlayCustomTriggerAnimation(string triggerName, Vector2 direction)
    {
        if (string.IsNullOrWhiteSpace(triggerName))
            return;

        int triggerHash = Animator.StringToHash(triggerName);
        if (HasTrigger(triggerHash))
            PlayTriggerAnimation(triggerHash, true, direction);
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
        _hasSpinTrigger = false;
        _hasDashTrigger = false;
        _hasDeathTrigger = false;
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
            else if (parameter.nameHash == SpinTriggerHash)
                _hasSpinTrigger = true;
            else if (parameter.nameHash == DashTriggerHash)
                _hasDashTrigger = true;
            else if (parameter.nameHash == DeathTriggerHash)
                _hasDeathTrigger = true;
        }
    }

    private void ResetCachedTriggers()
    {
        if (animator == null)
            return;

        if (_hasAttackTrigger)
            animator.ResetTrigger(AttackTriggerHash);
        if (_hasSpinTrigger)
            animator.ResetTrigger(SpinTriggerHash);
        if (_hasDashTrigger)
            animator.ResetTrigger(DashTriggerHash);
        if (_hasDeathTrigger)
            animator.ResetTrigger(DeathTriggerHash);
    }

    private void CacheVisualDefaults()
    {
        if (visualTransform == null && spriteRenderer != null)
            visualTransform = spriteRenderer.transform;

        if (visualTransform != null)
            _defaultVisualRotation = visualTransform.localRotation;

    }

    private void ApplyDashVisualRotation(Vector2 direction, bool rotateByDirection, float baseAngle)
    {
        if (CurrentForm == null ||
            !CurrentForm.RotateDashAnimationByDirection ||
            !rotateByDirection ||
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

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + baseAngle;
        visualTransform.localRotation = _defaultVisualRotation * Quaternion.Euler(0f, 0f, angle);
    }

    private bool HasTrigger(int triggerHash)
    {
        if (animator == null)
            return false;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Trigger &&
                parameter.nameHash == triggerHash)
            {
                return true;
            }
        }

        return false;
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
