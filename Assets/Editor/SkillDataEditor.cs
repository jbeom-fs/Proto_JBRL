using System.Collections.Generic;
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
    private SerializedProperty _recastStages;
    private SerializedProperty _recastWindow;
    private SerializedProperty _damage;

    private SerializedProperty _animationType;
    private SerializedProperty _customAnimationTrigger;
    private SerializedProperty _rotateAnimationByDirection;
    private SerializedProperty _animationBaseAngle;

    private SerializedProperty _attackPattern;
    private SerializedProperty _patternRange;
    private SerializedProperty _coneHalfAngle;
    private SerializedProperty _customCells;
    private SerializedProperty _hitSteps;
    private SerializedProperty _cancelable;
    private SerializedProperty _isMultiTarget;
    private SerializedProperty _canPenetrateWalls;

    private SerializedProperty _knockbackForce;
    private SerializedProperty _knockbackDuration;
    private SerializedProperty _slowPercentage;
    private SerializedProperty _slowDuration;
    private SerializedProperty _ailments;
    private SerializedProperty _zoneSprite;
    private SerializedProperty _zoneRadius;
    private SerializedProperty _zoneTickInterval;
    private SerializedProperty _zoneDuration;

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
    private int _customCellGridRadius = 4;
    private bool _rawCustomCellsFoldout;
    private int _selectedHitPhaseIndex;

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
        _recastStages = serializedObject.FindProperty("recastStages");
        _recastWindow = serializedObject.FindProperty("recastWindow");
        _damage = serializedObject.FindProperty("damage");

        _animationType = serializedObject.FindProperty("animationType");
        _customAnimationTrigger = serializedObject.FindProperty("customAnimationTrigger");
        _rotateAnimationByDirection = serializedObject.FindProperty("rotateAnimationByDirection");
        _animationBaseAngle = serializedObject.FindProperty("animationBaseAngle");

        _attackPattern = serializedObject.FindProperty("attackPattern");
        _patternRange = serializedObject.FindProperty("patternRange");
        _coneHalfAngle = serializedObject.FindProperty("coneHalfAngle");
        _customCells = serializedObject.FindProperty("customCells");
        _hitSteps = serializedObject.FindProperty("hitSteps");
        _cancelable = serializedObject.FindProperty("cancelable");
        _isMultiTarget = serializedObject.FindProperty("isMultiTarget");
        _canPenetrateWalls = serializedObject.FindProperty("canPenetrateWalls");

        _knockbackForce = serializedObject.FindProperty("knockbackForce");
        _knockbackDuration = serializedObject.FindProperty("knockbackDuration");
        _slowPercentage = serializedObject.FindProperty("slowPercentage");
        _slowDuration = serializedObject.FindProperty("slowDuration");
        _ailments = serializedObject.FindProperty("ailments");
        _zoneSprite = serializedObject.FindProperty("zoneSprite");
        _zoneRadius = serializedObject.FindProperty("zoneRadius");
        _zoneTickInterval = serializedObject.FindProperty("zoneTickInterval");
        _zoneDuration = serializedObject.FindProperty("zoneDuration");

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
        DrawRecastChainSection();
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
                    else
                        DrawAilmentsOnlySection("Dash Ailments (no damage)");
                    DrawDaggerMarkerSection();
                    break;

                case SkillExecutionType.Blink:
                    DrawBlinkSection();
                    DrawAilmentsOnlySection("Blink Target Ailments");
                    DrawDaggerMarkerSection();
                    break;

                case SkillExecutionType.Buff:
                    DrawBuffSection();
                    DrawDaggerMarkerSection();
                    break;

                case SkillExecutionType.AreaOverTime:
                    DrawZoneSection();
                    DrawCombatImpactSection("Zone Combat Impact");
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
        DrawProperty(_cancelable);
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

    private void DrawRecastChainSection()
    {
        DrawSectionHeader("Recast Chain");
        DrawProperty(_recastStages);

        if (_recastStages == null ||
            _recastStages.hasMultipleDifferentValues ||
            _recastStages.arraySize == 0)
        {
            return;
        }

        DrawProperty(_recastWindow);
        if (targets == null || targets.Length != 1)
            return;

        for (int i = 0; i < _recastStages.arraySize; i++)
        {
            SerializedProperty element = _recastStages.GetArrayElementAtIndex(i);
            SkillData stage = element.objectReferenceValue as SkillData;
            if (stage == null)
            {
                EditorGUILayout.HelpBox(
                    $"Recast stage {i + 1} is null. Runtime ignores the request and lets the chain expire naturally.",
                    MessageType.Warning);
                continue;
            }

            if (stage.recastStages != null && stage.recastStages.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"Recast stage {i + 1} has its own recastStages. Nested chains are unsupported in V1 and ignored.",
                    MessageType.Warning);
            }

            if (stage.castDelay > 0f)
            {
                EditorGUILayout.HelpBox(
                    $"Recast stage {i + 1} has castDelay > 0. V1 executes recast stages immediately and ignores this delay.",
                    MessageType.Warning);
            }
        }
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
        DrawHitStepsEditor();
        DrawProperty(_isMultiTarget);
        DrawProperty(_canPenetrateWalls);
    }

    private void DrawHitStepsEditor()
    {
        if (_hitSteps == null)
        {
            DrawBaseHitEditor();
            return;
        }

        if (targets.Length > 1 || _hitSteps.hasMultipleDifferentValues)
        {
            DrawBaseHitEditor();
            DrawProperty(_hitSteps);
            return;
        }

        _selectedHitPhaseIndex = Mathf.Clamp(_selectedHitPhaseIndex, 0, _hitSteps.arraySize);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Hit Timeline", EditorStyles.boldLabel);
        string[] phaseLabels = new string[_hitSteps.arraySize + 1];
        phaseLabels[0] = "Base";
        for (int i = 0; i < _hitSteps.arraySize; i++)
            phaseLabels[i + 1] = "Step " + (i + 1);

        int columnCount = Mathf.Min(phaseLabels.Length, 4);
        _selectedHitPhaseIndex = GUILayout.SelectionGrid(
            _selectedHitPhaseIndex,
            phaseLabels,
            columnCount,
            EditorStyles.miniButton);
        DrawHitStepListControls();

        if (_selectedHitPhaseIndex == 0)
            DrawBaseHitEditor();
        else
            DrawHitStepEditor(_selectedHitPhaseIndex - 1);

        if (_hitSteps.arraySize > 0)
        {
            EditorGUILayout.HelpBox(
                "Steps are follow-up hits after the base hit (1 step = 2 hits total).",
                MessageType.Info);
        }
    }

    private void DrawBaseHitEditor()
    {
        if (!IsAttackPattern(AttackPatternType.Custom))
            return;

        DrawCustomCellsEditor(_customCells);
        EditorGUILayout.HelpBox(
            "Cells are authored relative to the caster with +Y as forward. patternRange is not used by Custom.",
            MessageType.Info);
        if (_customCells != null && !_customCells.hasMultipleDifferentValues && _customCells.arraySize == 0)
            EditorGUILayout.HelpBox("Custom pattern has no cells.", MessageType.Warning);
    }

    private void DrawHitStepEditor(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex >= _hitSteps.arraySize)
            return;

        SerializedProperty step = _hitSteps.GetArrayElementAtIndex(stepIndex);
        DrawProperty(step.FindPropertyRelative("delay"));
        DrawProperty(step.FindPropertyRelative("damagePct"));

        SerializedProperty overrideCells = step.FindPropertyRelative("overrideCells");
        if (overrideCells == null)
            return;

        HashSet<Vector2Int> hintCells = null;
        if (overrideCells.arraySize == 0)
        {
            hintCells = ReadBaseShapeCellSet();
            EditorGUILayout.HelpBox(
                "Empty override repeats the base shape. Faint cells show the current base shape; click any cell to start an override.",
                MessageType.Info);
        }

        DrawCustomCellsEditor(overrideCells, hintCells);
    }

    private void DrawHitStepListControls()
    {
        bool addClicked;
        bool deleteClicked;
        bool moveUpClicked;
        bool moveDownClicked;

        EditorGUILayout.BeginHorizontal();
        addClicked = GUILayout.Button("+", GUILayout.Width(32f));
        using (new EditorGUI.DisabledScope(_selectedHitPhaseIndex == 0))
            deleteClicked = GUILayout.Button("-", GUILayout.Width(32f));
        using (new EditorGUI.DisabledScope(_selectedHitPhaseIndex <= 1))
            moveUpClicked = GUILayout.Button("Up", GUILayout.Width(44f));
        using (new EditorGUI.DisabledScope(
                   _selectedHitPhaseIndex == 0 || _selectedHitPhaseIndex >= _hitSteps.arraySize))
        {
            moveDownClicked = GUILayout.Button("Down", GUILayout.Width(48f));
        }
        EditorGUILayout.EndHorizontal();

        if (addClicked)
        {
            int newStepIndex = _hitSteps.arraySize;
            _hitSteps.InsertArrayElementAtIndex(newStepIndex);
            SerializedProperty newStep = _hitSteps.GetArrayElementAtIndex(newStepIndex);
            newStep.FindPropertyRelative("delay").floatValue = 0f;
            newStep.FindPropertyRelative("damagePct").intValue = 100;
            newStep.FindPropertyRelative("overrideCells").arraySize = 0;
            _selectedHitPhaseIndex = newStepIndex + 1;
            ApplyHitStepListChangeAndExitGUI();
        }

        if (deleteClicked)
        {
            int stepIndex = _selectedHitPhaseIndex - 1;
            _hitSteps.DeleteArrayElementAtIndex(stepIndex);
            _selectedHitPhaseIndex = Mathf.Clamp(_selectedHitPhaseIndex, 0, _hitSteps.arraySize);
            ApplyHitStepListChangeAndExitGUI();
        }

        if (moveUpClicked)
        {
            int stepIndex = _selectedHitPhaseIndex - 1;
            _hitSteps.MoveArrayElement(stepIndex, stepIndex - 1);
            _selectedHitPhaseIndex--;
            ApplyHitStepListChangeAndExitGUI();
        }

        if (moveDownClicked)
        {
            int stepIndex = _selectedHitPhaseIndex - 1;
            _hitSteps.MoveArrayElement(stepIndex, stepIndex + 1);
            _selectedHitPhaseIndex++;
            ApplyHitStepListChangeAndExitGUI();
        }
    }

    private void ApplyHitStepListChangeAndExitGUI()
    {
        serializedObject.ApplyModifiedProperties();
        GUIUtility.ExitGUI();
    }

    private void DrawCustomCellsEditor(
        SerializedProperty cellsProperty,
        HashSet<Vector2Int> hintCells = null)
    {
        if (cellsProperty == null)
            return;

        if (targets.Length > 1 || cellsProperty.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox("Select a single asset to edit cells.", MessageType.Info);
            DrawProperty(cellsProperty);
            return;
        }

        HashSet<Vector2Int> cells = ReadCustomCellSet(cellsProperty);
        ExpandCustomCellGridRadius(cells);
        if (hintCells != null)
            ExpandCustomCellGridRadius(hintCells);
        DrawCustomCellGridControls(cellsProperty, cells.Count);
        DrawCustomCellGrid(cellsProperty, cells, hintCells);

        _rawCustomCellsFoldout = EditorGUILayout.Foldout(_rawCustomCellsFoldout, "Raw Cell List", true);
        if (_rawCustomCellsFoldout)
        {
            EditorGUI.indentLevel++;
            DrawProperty(cellsProperty);
            EditorGUI.indentLevel--;
        }
    }

    private static HashSet<Vector2Int> ReadCustomCellSet(SerializedProperty cellsProperty)
    {
        HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
        if (cellsProperty == null)
            return cells;

        for (int i = 0; i < cellsProperty.arraySize; i++)
            cells.Add(cellsProperty.GetArrayElementAtIndex(i).vector2IntValue);

        return cells;
    }

    private HashSet<Vector2Int> ReadBaseShapeCellSet()
    {
        if (IsAttackPattern(AttackPatternType.Custom))
            return ReadCustomCellSet(_customCells);

        HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
        if (_attackPattern == null || _attackPattern.hasMultipleDifferentValues)
            return cells;

        List<Vector2Int> resolvedCells = new List<Vector2Int>();
        float coneHalfAngle = _coneHalfAngle != null && !_coneHalfAngle.hasMultipleDifferentValues
            ? _coneHalfAngle.floatValue
            : 45f;
        AttackPattern.FillTargets(
            (AttackPatternType)_attackPattern.enumValueIndex,
            Vector2Int.zero,
            Vector2Int.up,
            Mathf.Max(0, GetIntValue(_patternRange)),
            coneHalfAngle,
            resolvedCells);
        cells.UnionWith(resolvedCells);
        return cells;
    }

    private void ExpandCustomCellGridRadius(HashSet<Vector2Int> cells)
    {
        int requiredRadius = _customCellGridRadius;
        foreach (Vector2Int cell in cells)
            requiredRadius = Mathf.Max(requiredRadius, Mathf.Abs(cell.x), Mathf.Abs(cell.y));

        _customCellGridRadius = Mathf.Clamp(requiredRadius, 1, 12);
    }

    private void DrawCustomCellGridControls(SerializedProperty cellsProperty, int cellCount)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Radius: " + _customCellGridRadius, GUILayout.Width(72f));

        using (new EditorGUI.DisabledScope(_customCellGridRadius >= 12))
        {
            if (GUILayout.Button("+", GUILayout.Width(24f)))
                _customCellGridRadius = Mathf.Min(12, _customCellGridRadius + 1);
        }

        using (new EditorGUI.DisabledScope(_customCellGridRadius <= 1))
        {
            if (GUILayout.Button("-", GUILayout.Width(24f)))
                _customCellGridRadius = Mathf.Max(1, _customCellGridRadius - 1);
        }

        GUILayout.Space(8f);
        EditorGUILayout.LabelField("Cells: " + cellCount, GUILayout.Width(64f));
        if (GUILayout.Button("Clear", GUILayout.Width(52f)))
        {
            cellsProperty.arraySize = 0;
            serializedObject.ApplyModifiedProperties();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawCustomCellGrid(
        SerializedProperty cellsProperty,
        HashSet<Vector2Int> cells,
        HashSet<Vector2Int> hintCells)
    {
        Color originalColor = GUI.backgroundColor;
        for (int y = _customCellGridRadius; y >= -_customCellGridRadius; y--)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);

            for (int x = -_customCellGridRadius; x <= _customCellGridRadius; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                bool active = cells.Contains(cell);
                bool hinted = !active && hintCells != null && hintCells.Contains(cell);
                bool isCenter = x == 0 && y == 0;

                GUI.backgroundColor = GetCustomCellButtonColor(active, hinted, isCenter, originalColor);
                string label = isCenter ? "P" : active ? "X" : hinted ? "." : string.Empty;
                if (GUILayout.Button(label, GUILayout.Width(18f), GUILayout.Height(18f)))
                    ToggleCustomCell(cellsProperty, cells, cell);
            }

            GUI.backgroundColor = originalColor;
            EditorGUILayout.EndHorizontal();
        }

        GUI.backgroundColor = originalColor;
    }

    private static Color GetCustomCellButtonColor(
        bool active,
        bool hinted,
        bool isCenter,
        Color fallback)
    {
        if (active && isCenter)
            return new Color(0.25f, 0.9f, 1f);
        if (active)
            return new Color(0.35f, 0.9f, 0.35f);
        if (hinted)
            return Color.Lerp(fallback, new Color(0.35f, 0.9f, 0.35f), 0.3f);
        if (isCenter)
            return new Color(1f, 0.8f, 0.25f);

        return fallback;
    }

    private void ToggleCustomCell(
        SerializedProperty cellsProperty,
        HashSet<Vector2Int> cells,
        Vector2Int cell)
    {
        if (!cells.Add(cell))
            cells.Remove(cell);

        WriteCustomCellSet(cellsProperty, cells);
        serializedObject.ApplyModifiedProperties();
    }

    private static void WriteCustomCellSet(
        SerializedProperty cellsProperty,
        HashSet<Vector2Int> cells)
    {
        List<Vector2Int> orderedCells = new List<Vector2Int>(cells);
        orderedCells.Sort((left, right) =>
        {
            int yCompare = left.y.CompareTo(right.y);
            return yCompare != 0 ? yCompare : left.x.CompareTo(right.x);
        });

        cellsProperty.arraySize = orderedCells.Count;
        for (int i = 0; i < orderedCells.Count; i++)
            cellsProperty.GetArrayElementAtIndex(i).vector2IntValue = orderedCells[i];
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
                "Dash movement is enabled, but dash damage is off. Ailments still sweep along the dash path when configured.",
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
        DrawProperty(_ailments);
    }

    private void DrawZoneSection()
    {
        DrawSectionHeader("Zone");
        DrawProperty(_zoneSprite);
        DrawProperty(_zoneRadius);
        DrawProperty(_zoneTickInterval);
        DrawProperty(_zoneDuration);
    }

    private void DrawAilmentsOnlySection(string title)
    {
        DrawSectionHeader(title);
        DrawProperty(_ailments);
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
        DrawNegativeWarning(_recastWindow, "Recast Window");
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

        if (executionType == SkillExecutionType.AreaOverTime)
        {
            DrawNonPositiveWarning(_zoneRadius, "Zone Radius");
            DrawNonPositiveWarning(_zoneTickInterval, "Zone Tick Interval");
            DrawNonPositiveWarning(_zoneDuration, "Zone Duration");
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
            "No linked ItemData - this engraving cannot drop. Use the Skill Dashboard (Scan -> Add to ItemDatabase) to create the entry.",
            MessageType.Info);

        if (GUILayout.Button("Open Skill Dashboard"))
            SkillDashboardWindow.Open();
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
