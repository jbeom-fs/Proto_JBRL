using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SkillData))]
[CanEditMultipleObjects]
public sealed class SkillDataEditor : Editor
{
    private SerializedProperty _skillName;
    private SerializedProperty _icon;
    private SerializedProperty _description;
    private SerializedProperty _executionType;
    private SerializedProperty _resourceType;
    private SerializedProperty _requiredAmount;
    private SerializedProperty _consumeAmount;
    private SerializedProperty _cooldown;
    private SerializedProperty _castDelay;
    private SerializedProperty _recoveryDelay;
    private SerializedProperty _damage;

    private SerializedProperty _animationType;
    private SerializedProperty _customAnimationTrigger;
    private SerializedProperty _rotateAnimationByDirection;
    private SerializedProperty _animationBaseAngle;

    private SerializedProperty _attackPattern;
    private SerializedProperty _patternRange;
    private SerializedProperty _coneHalfAngle;
    private SerializedProperty _isMultiTarget;
    private SerializedProperty _canPenetrateWalls;

    private SerializedProperty _knockbackForce;
    private SerializedProperty _knockbackDuration;
    private SerializedProperty _slowPercentage;
    private SerializedProperty _slowDuration;

    private SerializedProperty _projectilePrefab;
    private SerializedProperty _projectileSpeed;
    private SerializedProperty _projectileLifetime;
    private SerializedProperty _projectileCount;
    private SerializedProperty _projectileSpreadAngle;
    private SerializedProperty _projectileFirePattern;
    private SerializedProperty _projectileWallHitMode;
    private SerializedProperty _projectileTargetHitMode;
    private SerializedProperty _projectileMaxBounceCount;
    private SerializedProperty _projectileSpawnOffset;
    private SerializedProperty _projectileBurstInterval;
    private SerializedProperty _projectileBurstSpacing;

    private SerializedProperty _dashDistance;
    private SerializedProperty _dashDuration;
    private SerializedProperty _dashStopOnWall;
    private SerializedProperty _dashDamageOnPath;
    private SerializedProperty _dashDamageOnContact;
    private SerializedProperty _dashInvincibleDuringDash;

    private bool _reservedFoldout;

    private void OnEnable()
    {
        _skillName = serializedObject.FindProperty("skillName");
        _icon = serializedObject.FindProperty("icon");
        _description = serializedObject.FindProperty("description");
        _executionType = serializedObject.FindProperty("executionType");
        _resourceType = serializedObject.FindProperty("resourceType");
        _requiredAmount = serializedObject.FindProperty("requiredAmount");
        _consumeAmount = serializedObject.FindProperty("consumeAmount");
        _cooldown = serializedObject.FindProperty("cooldown");
        _castDelay = serializedObject.FindProperty("castDelay");
        _recoveryDelay = serializedObject.FindProperty("recoveryDelay");
        _damage = serializedObject.FindProperty("damage");

        _animationType = serializedObject.FindProperty("animationType");
        _customAnimationTrigger = serializedObject.FindProperty("customAnimationTrigger");
        _rotateAnimationByDirection = serializedObject.FindProperty("rotateAnimationByDirection");
        _animationBaseAngle = serializedObject.FindProperty("animationBaseAngle");

        _attackPattern = serializedObject.FindProperty("attackPattern");
        _patternRange = serializedObject.FindProperty("patternRange");
        _coneHalfAngle = serializedObject.FindProperty("coneHalfAngle");
        _isMultiTarget = serializedObject.FindProperty("isMultiTarget");
        _canPenetrateWalls = serializedObject.FindProperty("canPenetrateWalls");

        _knockbackForce = serializedObject.FindProperty("knockbackForce");
        _knockbackDuration = serializedObject.FindProperty("knockbackDuration");
        _slowPercentage = serializedObject.FindProperty("slowPercentage");
        _slowDuration = serializedObject.FindProperty("slowDuration");

        _projectilePrefab = serializedObject.FindProperty("projectilePrefab");
        _projectileSpeed = serializedObject.FindProperty("projectileSpeed");
        _projectileLifetime = serializedObject.FindProperty("projectileLifetime");
        _projectileCount = serializedObject.FindProperty("projectileCount");
        _projectileSpreadAngle = serializedObject.FindProperty("projectileSpreadAngle");
        _projectileFirePattern = serializedObject.FindProperty("projectileFirePattern");
        _projectileWallHitMode = serializedObject.FindProperty("projectileWallHitMode");
        _projectileTargetHitMode = serializedObject.FindProperty("projectileTargetHitMode");
        _projectileMaxBounceCount = serializedObject.FindProperty("projectileMaxBounceCount");
        _projectileSpawnOffset = serializedObject.FindProperty("projectileSpawnOffset");
        _projectileBurstInterval = serializedObject.FindProperty("projectileBurstInterval");
        _projectileBurstSpacing = serializedObject.FindProperty("projectileBurstSpacing");

        _dashDistance = serializedObject.FindProperty("dashDistance");
        _dashDuration = serializedObject.FindProperty("dashDuration");
        _dashStopOnWall = serializedObject.FindProperty("dashStopOnWall");
        _dashDamageOnPath = serializedObject.FindProperty("dashDamageOnPath");
        _dashDamageOnContact = serializedObject.FindProperty("dashDamageOnContact");
        _dashInvincibleDuringDash = serializedObject.FindProperty("dashInvincibleDuringDash");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawBasicSection();
        DrawResourceSection();
        DrawAnimationSection();
        SkillExecutionType executionType = GetExecutionType();

        if (_executionType != null && _executionType.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox(
                "Multiple SkillData assets have different execution types. Execution-specific sections are hidden until the selection has one execution type.",
                MessageType.Info);
        }
        else
        {
            switch (executionType)
            {
                case SkillExecutionType.InstantArea:
                    DrawInstantAreaSection();
                    DrawCombatImpactSection("Combat Impact");
                    break;

                case SkillExecutionType.Projectile:
                    DrawProjectileSection();
                    DrawCombatImpactSection("Projectile Combat Impact");
                    break;

                case SkillExecutionType.Dash:
                    DrawDashSection();
                    if (HasDashDamageEnabled())
                        DrawCombatImpactSection("Dash Damage Impact");
                    break;

                case SkillExecutionType.AreaOverTime:
                case SkillExecutionType.Buff:
                    DrawReservedExecutionType(executionType);
                    break;
            }
        }

        DrawReservedSection();
        DrawValidationWarnings(executionType);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawBasicSection()
    {
        DrawSectionHeader("Basic");
        DrawProperty(_skillName);
        DrawProperty(_icon);
        DrawProperty(_description);
        DrawProperty(_executionType);
        DrawProperty(_cooldown);
        DrawProperty(_castDelay);
        DrawProperty(_recoveryDelay);
        DrawProperty(_damage);
    }

    private void DrawResourceSection()
    {
        DrawSectionHeader("Resource");
        DrawProperty(_resourceType);
        DrawProperty(_requiredAmount);
        DrawProperty(_consumeAmount);
    }

    private void DrawAnimationSection()
    {
        DrawSectionHeader("Animation");
        DrawProperty(_animationType);

        if (_animationType == null)
            return;

        if (_animationType.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox(
                "Multiple SkillData assets have different animation types. Animation-specific options are hidden until the selection has one animation type.",
                MessageType.Info);
            return;
        }

        SkillAnimationType animationType = GetAnimationType();
        if (animationType == SkillAnimationType.None)
        {
            EditorGUILayout.HelpBox("No animation request will be sent when this skill succeeds.", MessageType.Info);
            return;
        }

        if (animationType == SkillAnimationType.CustomTrigger)
            DrawProperty(_customAnimationTrigger);

        DrawProperty(_rotateAnimationByDirection);
        if (animationType == SkillAnimationType.Dash || IsBoolEnabled(_rotateAnimationByDirection))
            DrawProperty(_animationBaseAngle);
    }

    private void DrawInstantAreaSection()
    {
        DrawSectionHeader("Target / Instant Area");
        DrawProperty(_attackPattern);
        DrawProperty(_patternRange);
        if (IsAttackPattern(AttackPatternType.Cone))
            DrawProperty(_coneHalfAngle);
        DrawProperty(_isMultiTarget);
        DrawProperty(_canPenetrateWalls);
    }

    private void DrawProjectileSection()
    {
        DrawSectionHeader("Projectile");
        DrawProperty(_projectilePrefab);
        DrawProperty(_projectileSpeed);
        DrawProperty(_projectileLifetime);
        DrawProperty(_projectileSpawnOffset);
        DrawProperty(_projectileFirePattern);
        DrawProperty(_projectileWallHitMode);
        if (IsProjectileWallHitMode(ProjectileWallHitMode.Bounce))
            DrawProperty(_projectileMaxBounceCount);
        DrawProperty(_projectileTargetHitMode);

        ProjectileFirePattern firePattern = GetProjectileFirePattern();
        switch (firePattern)
        {
            case ProjectileFirePattern.Single:
                EditorGUILayout.HelpBox("Single fires one projectile. Count, spread, and burst interval are not used.", MessageType.Info);
                break;

            case ProjectileFirePattern.Spread:
                DrawProperty(_projectileCount);
                DrawProperty(_projectileSpreadAngle);
                break;

            case ProjectileFirePattern.Burst:
                DrawProperty(_projectileCount);
                DrawProperty(_projectileBurstInterval);
                break;

            case ProjectileFirePattern.Circle:
                DrawProperty(_projectileCount);
                break;
        }
    }

    private void DrawDashSection()
    {
        DrawSectionHeader("Dash");
        DrawProperty(_dashDistance);
        DrawProperty(_dashDuration);
        DrawProperty(_dashStopOnWall);
        DrawProperty(_dashInvincibleDuringDash);
        DrawProperty(_dashDamageOnPath);
        DrawProperty(_dashDamageOnContact);

        if (!HasDashDamageEnabled())
        {
            EditorGUILayout.HelpBox(
                "Dash movement is enabled, but dash damage is off. Damage and combat impact fields are not used unless Path or Contact damage is enabled.",
                MessageType.Info);
        }
    }

    private void DrawCombatImpactSection(string title)
    {
        DrawSectionHeader(title);
        DrawProperty(_knockbackForce);
        DrawProperty(_knockbackDuration);
        DrawProperty(_slowPercentage);
        DrawProperty(_slowDuration);
    }

    private void DrawReservedExecutionType(SkillExecutionType executionType)
    {
        DrawSectionHeader("Reserved / Not Implemented");
        EditorGUILayout.HelpBox(
            executionType + " is reserved. SkillExecutor currently reports it as unsupported at runtime.",
            MessageType.Warning);
    }

    private void DrawReservedSection()
    {
        EditorGUILayout.Space(6f);
        _reservedFoldout = EditorGUILayout.Foldout(_reservedFoldout, "Reserved / Future", true);
        if (!_reservedFoldout)
            return;

        EditorGUI.indentLevel++;
        EditorGUILayout.HelpBox("These fields are serialized for future work, but are not currently consumed by SkillExecutor.", MessageType.Info);
        using (new EditorGUI.DisabledScope(true))
        {
            DrawProperty(_projectileBurstSpacing);
        }
        EditorGUI.indentLevel--;
    }

    private void DrawValidationWarnings(SkillExecutionType executionType)
    {
        DrawNegativeWarning(_requiredAmount, "Required Amount");
        DrawNegativeWarning(_consumeAmount, "Consume Amount");
        DrawNegativeWarning(_cooldown, "Cooldown");
        DrawNegativeWarning(_castDelay, "Cast Delay");
        DrawNegativeWarning(_recoveryDelay, "Recovery Delay");
        DrawNegativeWarning(_damage, "Damage");

        if (_executionType != null && _executionType.hasMultipleDifferentValues)
            return;

        if (executionType == SkillExecutionType.Projectile)
        {
            if (_projectilePrefab != null && !_projectilePrefab.hasMultipleDifferentValues && _projectilePrefab.objectReferenceValue == null)
                EditorGUILayout.HelpBox("Projectile skill has no projectilePrefab assigned.", MessageType.Warning);
            DrawNonPositiveWarning(_projectileSpeed, "Projectile Speed");
            DrawNonPositiveWarning(_projectileLifetime, "Projectile Lifetime");
            if (GetProjectileFirePattern() != ProjectileFirePattern.Single)
                DrawNonPositiveWarning(_projectileCount, "Projectile Count");
            if (GetProjectileFirePattern() == ProjectileFirePattern.Burst)
                DrawNonPositiveWarning(_projectileBurstInterval, "Burst Interval");
        }

        if (executionType == SkillExecutionType.Dash)
        {
            DrawNonPositiveWarning(_dashDistance, "Dash Distance");
            DrawNonPositiveWarning(_dashDuration, "Dash Duration");
        }
    }

    private SkillExecutionType GetExecutionType()
    {
        if (_executionType == null || _executionType.hasMultipleDifferentValues)
            return SkillExecutionType.InstantArea;

        return (SkillExecutionType)_executionType.enumValueIndex;
    }

    private SkillAnimationType GetAnimationType()
    {
        if (_animationType == null || _animationType.hasMultipleDifferentValues)
            return SkillAnimationType.None;

        return (SkillAnimationType)_animationType.enumValueIndex;
    }

    private ProjectileFirePattern GetProjectileFirePattern()
    {
        if (_projectileFirePattern == null || _projectileFirePattern.hasMultipleDifferentValues)
            return ProjectileFirePattern.Single;

        return (ProjectileFirePattern)_projectileFirePattern.enumValueIndex;
    }

    private bool IsAttackPattern(AttackPatternType pattern)
    {
        return _attackPattern != null &&
               !_attackPattern.hasMultipleDifferentValues &&
               _attackPattern.enumValueIndex == (int)pattern;
    }

    private bool IsProjectileWallHitMode(ProjectileWallHitMode mode)
    {
        return _projectileWallHitMode != null &&
               !_projectileWallHitMode.hasMultipleDifferentValues &&
               _projectileWallHitMode.enumValueIndex == (int)mode;
    }

    private bool HasDashDamageEnabled()
    {
        return IsBoolEnabled(_dashDamageOnPath) || IsBoolEnabled(_dashDamageOnContact);
    }

    private static bool IsBoolEnabled(SerializedProperty property)
    {
        return property != null && !property.hasMultipleDifferentValues && property.boolValue;
    }

    private static void DrawSectionHeader(string title)
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private static void DrawProperty(SerializedProperty property)
    {
        if (property == null)
            return;

        EditorGUILayout.PropertyField(property, true);
    }

    private static void DrawNegativeWarning(SerializedProperty property, string label)
    {
        if (property == null || property.hasMultipleDifferentValues)
            return;

        if (property.propertyType == SerializedPropertyType.Integer && property.intValue < 0)
            EditorGUILayout.HelpBox(label + " is negative.", MessageType.Warning);
        else if (property.propertyType == SerializedPropertyType.Float && property.floatValue < 0f)
            EditorGUILayout.HelpBox(label + " is negative.", MessageType.Warning);
    }

    private static void DrawNonPositiveWarning(SerializedProperty property, string label)
    {
        if (property == null || property.hasMultipleDifferentValues)
            return;

        if (property.propertyType == SerializedPropertyType.Integer && property.intValue <= 0)
            EditorGUILayout.HelpBox(label + " should be greater than 0.", MessageType.Warning);
        else if (property.propertyType == SerializedPropertyType.Float && property.floatValue <= 0f)
            EditorGUILayout.HelpBox(label + " should be greater than 0.", MessageType.Warning);
    }
}
