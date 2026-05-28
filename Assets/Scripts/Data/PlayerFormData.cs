using UnityEngine;

[CreateAssetMenu(fileName = "NewPlayerForm", menuName = "JBRogLike/Player/Form")]
public sealed class PlayerFormData : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private PlayerFormId formId;
    [SerializeField] private string displayName;

    [Header("Animation")]
    [SerializeField] private RuntimeAnimatorController animatorController;
    [SerializeField] private Sprite defaultSprite;
    [SerializeField] private bool defaultSpriteFacesRight = true;
    [SerializeField] private bool useHorizontalFlipForFacing = true;
    [SerializeField] private bool rotateDashAnimationByDirection;
    [SerializeField] private float dashBaseAngle;

    [Header("Future Extensions")]
    [SerializeField] private WeaponData defaultWeapon;
    [SerializeField] private SkillData[] skills;

    public PlayerFormId FormId => formId;
    public string DisplayName => displayName;
    public RuntimeAnimatorController AnimatorController => animatorController;
    public Sprite DefaultSprite => defaultSprite;
    public bool DefaultSpriteFacesRight => defaultSpriteFacesRight;
    public bool UseHorizontalFlipForFacing => useHorizontalFlipForFacing;
    public bool RotateDashAnimationByDirection => rotateDashAnimationByDirection;
    public float DashBaseAngle => dashBaseAngle;
    public WeaponData DefaultWeapon => defaultWeapon;
    public SkillData[] Skills => skills;
}
