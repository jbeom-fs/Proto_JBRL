using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemySkillData))]
[CanEditMultipleObjects]
public sealed class EnemySkillDataEditor : Editor
{
    private const float PreviewCellSize = 1f;
    private const int PreviewRadiusLimit = 12;

    [System.Flags]
    private enum ExecutionTypes
    {
        InstantArea = 1 << 0,
        Projectile = 1 << 1,
        Dash = 1 << 2,
        Jump = 1 << 3
    }

    private static readonly Dictionary<string, ExecutionTypes> s_VisibleExecutionTypes = new()
    {
        { "searchShape", ExecutionTypes.Jump | ExecutionTypes.Dash },
        { "damageShape", ExecutionTypes.Jump | ExecutionTypes.Dash | ExecutionTypes.InstantArea },
        { "damageRange", ExecutionTypes.Jump | ExecutionTypes.Dash | ExecutionTypes.InstantArea },
        { "projectile", ExecutionTypes.Projectile },
        { "moveSpeed", ExecutionTypes.Jump | ExecutionTypes.Dash },
        { "stayInRoom", ExecutionTypes.Jump | ExecutionTypes.Dash },
        { "lockFacingDuringExecute", ExecutionTypes.Jump | ExecutionTypes.Dash },
        { "jumpVisualHeight", ExecutionTypes.Jump | ExecutionTypes.Dash },
        { "stopOnWall", ExecutionTypes.Dash }
    };

    private readonly CustomCellGridDrawer _searchGrid = new();
    private readonly CustomCellGridDrawer _damageGrid = new();
    private readonly List<Vector2Int> _previewCellBuffer = new();

    private SerializedProperty _maxRange;
    private SerializedProperty _damageRange;
    private SerializedProperty _searchShape;
    private SerializedProperty _damageShape;

    private void OnEnable()
    {
        _maxRange = serializedObject.FindProperty("maxRange");
        _damageRange = serializedObject.FindProperty("damageRange");
        _searchShape = serializedObject.FindProperty("searchShape");
        _damageShape = serializedObject.FindProperty("damageShape");
    }

    private void OnDisable()
    {
        _searchGrid.EndStroke();
        _damageGrid.EndStroke();
    }

    public override void OnInspectorGUI()
    {
        CustomCellGridPass.BeginPass();
        try
        {
            serializedObject.Update();

            SerializedProperty executionTypeProperty = serializedObject.FindProperty("executionType");
            bool mixedExecutionTypes = executionTypeProperty != null &&
                                       executionTypeProperty.hasMultipleDifferentValues;
            EnemySkillExecutionType executionType = executionTypeProperty != null
                ? (EnemySkillExecutionType)executionTypeProperty.enumValueIndex
                : default;
            if (mixedExecutionTypes)
                EditorGUILayout.HelpBox(
                    "선택한 에셋의 실행타입이 달라 모든 필드를 표시함",
                    MessageType.Info);

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                string path = property.propertyPath;
                if (!mixedExecutionTypes && !ShouldShow(path, executionType))
                    continue;

                if (path == "searchShape")
                    DrawShape(_searchShape, _searchGrid, true);
                else if (path == "damageShape")
                    DrawShape(_damageShape, _damageGrid, false);
                else
                {
                    using (new EditorGUI.DisabledScope(path == "m_Script"))
                        EditorGUILayout.PropertyField(property, true);
                }

                if (path == "executionType" && !mixedExecutionTypes)
                {
                    executionType = (EnemySkillExecutionType)property.enumValueIndex;
                    if (executionType == EnemySkillExecutionType.InstantArea)
                        EditorGUILayout.HelpBox(
                            "InstantArea는 미구현 실행타입 — 런타임 Start가 실패함",
                            MessageType.Warning);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
        finally
        {
            CustomCellGridPass.EndPass();
        }
    }

    private static bool ShouldShow(string path, EnemySkillExecutionType executionType)
    {
        if (!s_VisibleExecutionTypes.TryGetValue(path, out ExecutionTypes visibleTypes))
            return true;

        ExecutionTypes selectedType = (ExecutionTypes)(1 << (int)executionType);
        return (visibleTypes & selectedType) != 0;
    }

    private void DrawShape(
        SerializedProperty shapeProperty,
        CustomCellGridDrawer grid,
        bool isSearchShape)
    {
        if (shapeProperty == null)
            return;

        SerializedProperty patternTypeProperty =
            shapeProperty.FindPropertyRelative("patternType");
        SerializedProperty coneHalfAngleProperty =
            shapeProperty.FindPropertyRelative("coneHalfAngle");

        EditorGUILayout.LabelField(shapeProperty.displayName, EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        try
        {
            if (patternTypeProperty != null)
            {
                EditorGUILayout.PropertyField(patternTypeProperty);
                if (coneHalfAngleProperty != null &&
                    (patternTypeProperty.hasMultipleDifferentValues ||
                     (AttackPatternType)patternTypeProperty.enumValueIndex == AttackPatternType.Cone))
                {
                    EditorGUILayout.PropertyField(coneHalfAngleProperty);
                }
            }

            DrawShapeGrid(shapeProperty, grid, isSearchShape);
        }
        finally
        {
            EditorGUI.indentLevel--;
        }
    }

    private void DrawShapeGrid(
        SerializedProperty shapeProperty,
        CustomCellGridDrawer grid,
        bool isSearchShape)
    {
        if (shapeProperty == null)
            return;

        SerializedProperty patternTypeProperty =
            shapeProperty.FindPropertyRelative("patternType");
        SerializedProperty customCellsProperty =
            shapeProperty.FindPropertyRelative("customCells");
        if (patternTypeProperty == null || customCellsProperty == null)
            return;

        bool isMultiEdit = targets.Length > 1;
        if (patternTypeProperty.hasMultipleDifferentValues)
        {
            grid.EndStroke();
            EditorGUILayout.HelpBox(
                "Selected assets use different pattern types.",
                MessageType.Info);
            return;
        }

        AttackPatternType patternType =
            (AttackPatternType)patternTypeProperty.enumValueIndex;
        if (patternType == AttackPatternType.Custom)
        {
            grid.Draw(
                serializedObject,
                customCellsProperty,
                isMultiEdit,
                null,
                Repaint);
            return;
        }

        HashSet<Vector2Int> previewCells = ResolvePreviewCells(
            shapeProperty,
            patternType,
            isSearchShape);
        grid.DrawPreview(previewCells, BuildPreviewNote(previewCells));
    }

    private HashSet<Vector2Int> ResolvePreviewCells(
        SerializedProperty shapeProperty,
        AttackPatternType patternType,
        bool isSearchShape)
    {
        SerializedProperty coneHalfAngleProperty =
            shapeProperty.FindPropertyRelative("coneHalfAngle");
        float coneHalfAngle = coneHalfAngleProperty != null &&
                              !coneHalfAngleProperty.hasMultipleDifferentValues
            ? coneHalfAngleProperty.floatValue
            : 45f;
        int range = isSearchShape
            ? Mathf.RoundToInt(GetFloatValue(_maxRange) / PreviewCellSize)
            : GetIntValue(_damageRange);

        _previewCellBuffer.Clear();
        AttackPattern.FillTargets(
            patternType,
            Vector2Int.zero,
            Vector2Int.up,
            Mathf.Max(0, range),
            coneHalfAngle,
            _previewCellBuffer);

        if (!isSearchShape && !_previewCellBuffer.Contains(Vector2Int.zero))
            _previewCellBuffer.Add(Vector2Int.zero);

        return new HashSet<Vector2Int>(_previewCellBuffer);
    }

    private static string BuildPreviewNote(HashSet<Vector2Int> cells)
    {
        int maxAbs = 0;
        foreach (Vector2Int cell in cells)
            maxAbs = Mathf.Max(maxAbs, Mathf.Abs(cell.x), Mathf.Abs(cell.y));

        const string cellSizeNote = "미리보기 기준 cellSize = 1";
        return maxAbs > PreviewRadiusLimit
            ? cellSizeNote + $"\n반경 12까지만 표시됨 (실제 {maxAbs})"
            : cellSizeNote;
    }

    private static float GetFloatValue(SerializedProperty property)
    {
        return property != null && !property.hasMultipleDifferentValues
            ? Mathf.Max(0f, property.floatValue)
            : 0f;
    }

    private static int GetIntValue(SerializedProperty property)
    {
        return property != null && !property.hasMultipleDifferentValues
            ? Mathf.Max(0, property.intValue)
            : 0;
    }
}
