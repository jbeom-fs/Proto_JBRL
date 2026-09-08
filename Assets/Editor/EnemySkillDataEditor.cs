using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemySkillData))]
[CanEditMultipleObjects]
public sealed class EnemySkillDataEditor : Editor
{
    private const float PreviewCellSize = 1f;
    private const int PreviewRadiusLimit = 12;

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

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script"))
                    EditorGUILayout.PropertyField(property, true);

                if (property.propertyPath == "searchShape")
                    DrawShapeGrid(_searchShape, _searchGrid, true);
                else if (property.propertyPath == "damageShape")
                    DrawShapeGrid(_damageShape, _damageGrid, false);
            }

            serializedObject.ApplyModifiedProperties();
        }
        finally
        {
            CustomCellGridPass.EndPass();
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
