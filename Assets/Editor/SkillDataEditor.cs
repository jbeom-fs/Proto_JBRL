using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SkillData), true)]
[CanEditMultipleObjects]
public sealed class SkillDataEditor : Editor
{
    private SerializedProperty _owningForm;
    private SerializedProperty _grade;

    private SerializedProperty _skillName;
    private SerializedProperty _icon;
    private SerializedProperty _description;
    private SerializedProperty _executionType;
    private SerializedProperty _resourceType;
    private SerializedProperty _requiredAmount;
    private SerializedProperty _consumeAmount;
    private SerializedProperty _bulletShortageMode;
    private SerializedProperty _reloadAmount;
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
    private SerializedProperty _customCells;
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
    private SerializedProperty _appliesDaggerMarker;
    private SerializedProperty _detonatesDaggerMarker;
    private SerializedProperty _markerDetonationDamage;
    private SerializedProperty _resetCooldownOnMarkerDetonate;
    private SerializedProperty _markerDuration;
    private SerializedProperty _blinkBehindOffset;

    private bool _reservedFoldout;
    private GUIStyle _faintGradeLabelStyle;
    private GUIStyle _wholeGradeLabelStyle;
    private GUIStyle _primordialGradeLabelStyle;
    private ItemDatabase _linkedItemDatabase;
    private string _linkedItemCode;
    private string _linkedItemDisplayName;

    private void OnEnable()
    {
        _owningForm = serializedObject.FindProperty("owningForm");
        _grade = serializedObject.FindProperty("grade");

        _skillName = serializedObject.FindProperty("skillName");
        _icon = serializedObject.FindProperty("icon");
        _description = serializedObject.FindProperty("description");
        _executionType = serializedObject.FindProperty("executionType");
        _resourceType = serializedObject.FindProperty("resourceType");
        _requiredAmount = serializedObject.FindProperty("requiredAmount");
        _consumeAmount = serializedObject.FindProperty("consumeAmount");
        _bulletShortageMode = serializedObject.FindProperty("bulletShortageMode");
        _reloadAmount = serializedObject.FindProperty("reloadAmount");
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
        _customCells = serializedObject.FindProperty("customCells");
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
        _appliesDaggerMarker = serializedObject.FindProperty("appliesDaggerMarker");
        _detonatesDaggerMarker = serializedObject.FindProperty("detonatesDaggerMarker");
        _markerDetonationDamage = serializedObject.FindProperty("markerDetonationDamage");
        _resetCooldownOnMarkerDetonate = serializedObject.FindProperty("resetCooldownOnMarkerDetonate");
        _markerDuration = serializedObject.FindProperty("markerDuration");
        _blinkBehindOffset = serializedObject.FindProperty("blinkBehindOffset");

        RefreshLinkedItemCache();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawEngravingSection();
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
                    DrawDaggerMarkerSection();
                    break;

                case SkillExecutionType.Blink:
                    DrawBlinkSection();
                    DrawDaggerMarkerSection();
                    break;

                case SkillExecutionType.Buff:
                    DrawBuffSection();
                    DrawDaggerMarkerSection();
                    break;

                case SkillExecutionType.AreaOverTime:
                    DrawReservedExecutionType(executionType);
                    break;
            }
        }

        DrawReservedSection();
        DrawValidationWarnings(executionType);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawEngravingSection()
    {
        if (!CanDrawEngravingSection())
            return;

        DrawSectionHeader("Engraving");
        DrawProperty(_owningForm);
        DrawProperty(_grade);
        DrawGradeLabel();
        DrawLinkedItemControls();
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
        if (GetResourceType() == SkillResourceType.Bullet)
            DrawProperty(_bulletShortageMode);
        DrawProperty(_reloadAmount);
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
        if (IsAttackPattern(AttackPatternType.Custom))
        {
            DrawProperty(_customCells);
            EditorGUILayout.HelpBox(
                "Cells are authored relative to the caster with +Y as forward. patternRange is not used by Custom.",
                MessageType.Info);
            if (_customCells != null && !_customCells.hasMultipleDifferentValues && _customCells.arraySize == 0)
                EditorGUILayout.HelpBox("Custom pattern has no cells.", MessageType.Warning);
        }
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

    private void DrawBlinkSection()
    {
        DrawSectionHeader("Blink");
        DrawProperty(_patternRange);
        DrawProperty(_blinkBehindOffset);
    }

    private void DrawDaggerMarkerSection()
    {
        DrawSectionHeader("Dagger Marker");
        DrawProperty(_appliesDaggerMarker);
        DrawProperty(_detonatesDaggerMarker);
        if (IsBoolEnabled(_detonatesDaggerMarker))
        {
            DrawProperty(_markerDetonationDamage);
            DrawProperty(_resetCooldownOnMarkerDetonate);
        }
        DrawProperty(_markerDuration);
    }

    private void DrawBuffSection()
    {
        DrawSectionHeader("Buff");
        EditorGUILayout.HelpBox("Buff execution succeeds immediately. Dagger marker flags currently drive timed basic-attack marker behavior.", MessageType.Info);
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
        DrawNegativeWarning(_reloadAmount, "Reload Amount");
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

        if (GetResourceType() == SkillResourceType.Bullet &&
            GetBulletShortageMode() == BulletShortageMode.AllowPartialUse &&
            executionType == SkillExecutionType.Projectile &&
            GetEffectiveProjectileCountForEditor() != Mathf.Max(0, GetIntValue(_consumeAmount)))
        {
            EditorGUILayout.HelpBox(
                "AllowPartialUse expects effective projectile count to match consumeAmount. Runtime falls back to full-cost behavior when they differ.",
                MessageType.Warning);
        }

        if (executionType == SkillExecutionType.Dash)
        {
            DrawNonPositiveWarning(_dashDistance, "Dash Distance");
            DrawNonPositiveWarning(_dashDuration, "Dash Duration");
        }
    }

    private void DrawGradeLabel()
    {
        if (_grade == null || _grade.hasMultipleDifferentValues)
            return;

        EngravingGrade grade = (EngravingGrade)_grade.enumValueIndex;
        EditorGUILayout.LabelField("Grade: " + grade, GetGradeLabelStyle(grade));
    }

    private void DrawLinkedItemControls()
    {
        if (!IsSingleEngravingTarget(out _))
            return;

        EditorGUILayout.Space(4f);
        if (_linkedItemDatabase != null)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Linked ItemDatabase", _linkedItemDatabase, typeof(ItemDatabase), false);
                EditorGUILayout.TextField("Item Code", _linkedItemCode ?? string.Empty);
                EditorGUILayout.TextField("Display Name", _linkedItemDisplayName ?? string.Empty);
            }

            if (GUILayout.Button("Ping"))
                EditorGUIUtility.PingObject(_linkedItemDatabase);

            return;
        }

        EditorGUILayout.HelpBox(
            "No linked ItemData - this engraving cannot drop. Use the Engraving Validator (Scan -> Add to ItemDatabase) to create the entry.",
            MessageType.Info);

        if (GUILayout.Button("Open Engraving Validator"))
            EngravingValidatorWindow.Open();
    }

    private void RefreshLinkedItemCache()
    {
        _linkedItemDatabase = null;
        _linkedItemCode = null;
        _linkedItemDisplayName = null;

        if (!IsSingleEngravingTarget(out EngravingData engraving))
            return;

        string[] guids = AssetDatabase.FindAssets("t:ItemDatabase");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            ItemDatabase database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(path);
            if (database == null)
                continue;

            SerializedObject databaseObject = new SerializedObject(database);
            SerializedProperty items = databaseObject.FindProperty("items");
            if (items == null || !items.isArray)
                continue;

            for (int itemIndex = 0; itemIndex < items.arraySize; itemIndex++)
            {
                SerializedProperty item = items.GetArrayElementAtIndex(itemIndex);
                SerializedProperty engravingProperty = item.FindPropertyRelative("engraving");
                if (engravingProperty == null || engravingProperty.objectReferenceValue != engraving)
                    continue;

                _linkedItemDatabase = database;
                _linkedItemCode = GetStringValue(item.FindPropertyRelative("itemCode"));
                _linkedItemDisplayName = GetStringValue(item.FindPropertyRelative("displayName"));
                return;
            }
        }
    }

    private bool CanDrawEngravingSection()
    {
        if (_owningForm == null || _grade == null)
            return false;

        for (int i = 0; i < targets.Length; i++)
        {
            if (!(targets[i] is EngravingData))
                return false;
        }

        return true;
    }

    private bool IsSingleEngravingTarget(out EngravingData engraving)
    {
        engraving = null;

        if (targets == null || targets.Length != 1)
            return false;

        engraving = target as EngravingData;
        return engraving != null;
    }

    private GUIStyle GetGradeLabelStyle(EngravingGrade grade)
    {
        EnsureGradeLabelStyles();

        switch (grade)
        {
            case EngravingGrade.Whole:
                return _wholeGradeLabelStyle;

            case EngravingGrade.Primordial:
                return _primordialGradeLabelStyle;

            default:
                return _faintGradeLabelStyle;
        }
    }

    private void EnsureGradeLabelStyles()
    {
        if (_faintGradeLabelStyle != null)
            return;

        _faintGradeLabelStyle = CreateGradeLabelStyle(new Color(0.6f, 0.6f, 0.6f));
        _wholeGradeLabelStyle = CreateGradeLabelStyle(Color.white);
        _primordialGradeLabelStyle = CreateGradeLabelStyle(new Color(1f, 0.85f, 0.3f));
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

    private SkillResourceType GetResourceType()
    {
        if (_resourceType == null || _resourceType.hasMultipleDifferentValues)
            return SkillResourceType.None;

        return (SkillResourceType)_resourceType.enumValueIndex;
    }

    private BulletShortageMode GetBulletShortageMode()
    {
        if (_bulletShortageMode == null || _bulletShortageMode.hasMultipleDifferentValues)
            return BulletShortageMode.RequireFullCost;

        return (BulletShortageMode)_bulletShortageMode.enumValueIndex;
    }

    private ProjectileFirePattern GetProjectileFirePattern()
    {
        if (_projectileFirePattern == null || _projectileFirePattern.hasMultipleDifferentValues)
            return ProjectileFirePattern.Single;

        return (ProjectileFirePattern)_projectileFirePattern.enumValueIndex;
    }

    private int GetEffectiveProjectileCountForEditor()
    {
        switch (GetProjectileFirePattern())
        {
            case ProjectileFirePattern.Burst:
            case ProjectileFirePattern.Spread:
            case ProjectileFirePattern.Circle:
                return Mathf.Max(1, GetIntValue(_projectileCount));

            default:
                return 1;
        }
    }

    private static int GetIntValue(SerializedProperty property)
    {
        return property != null && !property.hasMultipleDifferentValues ? property.intValue : 0;
    }

    private static string GetStringValue(SerializedProperty property)
    {
        return property != null ? property.stringValue : string.Empty;
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

    private static GUIStyle CreateGradeLabelStyle(Color textColor)
    {
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
        style.normal.textColor = textColor;
        return style;
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
