using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CustomCellGridPass
{
    private static readonly List<WeakReference> s_Drawers = new();

    internal static void Register(CustomCellGridDrawer drawer)
    {
        s_Drawers.Add(new WeakReference(drawer));
    }

    public static void BeginPass()
    {
        VisitDrawers(drawer => drawer.BeginPass());
    }

    public static void EndPass()
    {
        VisitDrawers(drawer => drawer.EndPass());
    }

    private static void VisitDrawers(Action<CustomCellGridDrawer> action)
    {
        for (int i = s_Drawers.Count - 1; i >= 0; i--)
        {
            CustomCellGridDrawer drawer =
                s_Drawers[i].Target as CustomCellGridDrawer;
            if (drawer == null)
            {
                s_Drawers.RemoveAt(i);
                continue;
            }

            action(drawer);
        }
    }
}

public sealed class CustomCellGridDrawer
{
    private const float CustomCellSize = 18f;

    private int _customCellGridRadius = 4;
    private bool _rawCustomCellsFoldout;
    private bool _isPainting;
    private bool _paintErase;
    private readonly HashSet<Vector2Int> _strokeVisited = new();
    private Vector2Int? _dragRectStart;
    private Vector2Int? _dragRectCurrent;
    private Vector2Int? _lastPaintCell;
    private int _paintUndoGroup = -1;
    private int _paintControlId;
    private GUIStyle _customCellLabelStyle;
    private bool _usedThisPass;

    public CustomCellGridDrawer()
    {
        CustomCellGridPass.Register(this);
    }

    public void Draw(
        SerializedObject serializedObject,
        SerializedProperty cellsProperty,
        bool isMultiEdit,
        HashSet<Vector2Int> hintCells,
        Action repaint)
    {
        _usedThisPass = true;

        if (cellsProperty == null)
            return;

        if (isMultiEdit || cellsProperty.hasMultipleDifferentValues)
        {
            EndStroke();
            EditorGUILayout.HelpBox("Select a single asset to edit cells.", MessageType.Info);
            EditorGUILayout.PropertyField(cellsProperty, true);
            return;
        }

        HashSet<Vector2Int> cells = ReadCustomCellSet(cellsProperty);
        ExpandCustomCellGridRadius(cells);
        if (hintCells != null)
            ExpandCustomCellGridRadius(hintCells);
        DrawCustomCellGridControls(serializedObject, cellsProperty, cells.Count);
        DrawCustomCellGrid(serializedObject, cellsProperty, cells, hintCells, repaint);

        _rawCustomCellsFoldout = EditorGUILayout.Foldout(
            _rawCustomCellsFoldout,
            "Raw Cell List",
            true);
        if (_rawCustomCellsFoldout)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(cellsProperty, true);
            EditorGUI.indentLevel--;
        }
    }

    public void DrawPreview(HashSet<Vector2Int> cells, string note)
    {
        _usedThisPass = true;
        EndStroke();

        if (cells == null)
            cells = new HashSet<Vector2Int>();
        ExpandCustomCellGridRadius(cells);
        DrawPreviewControls(cells.Count);
        DrawPreviewGrid(cells);

        if (!string.IsNullOrEmpty(note))
            EditorGUILayout.HelpBox(note, MessageType.Info);
    }

    public void EndStroke()
    {
        if (!_isPainting)
            return;

        if (_paintUndoGroup >= 0)
            Undo.CollapseUndoOperations(_paintUndoGroup);
        if (GUIUtility.hotControl == _paintControlId)
            GUIUtility.hotControl = 0;

        _isPainting = false;
        _paintErase = false;
        _strokeVisited.Clear();
        _dragRectStart = null;
        _dragRectCurrent = null;
        _lastPaintCell = null;
        _paintUndoGroup = -1;
        _paintControlId = 0;
    }

    internal void BeginPass()
    {
        _usedThisPass = false;
    }

    internal void EndPass()
    {
        if (!_usedThisPass)
            EndStroke();
    }

    private static HashSet<Vector2Int> ReadCustomCellSet(SerializedProperty cellsProperty)
    {
        HashSet<Vector2Int> cells = new();
        if (cellsProperty == null)
            return cells;

        for (int i = 0; i < cellsProperty.arraySize; i++)
            cells.Add(cellsProperty.GetArrayElementAtIndex(i).vector2IntValue);

        return cells;
    }

    private void ExpandCustomCellGridRadius(HashSet<Vector2Int> cells)
    {
        int requiredRadius = _customCellGridRadius;
        foreach (Vector2Int cell in cells)
            requiredRadius = Mathf.Max(requiredRadius, Mathf.Abs(cell.x), Mathf.Abs(cell.y));

        _customCellGridRadius = Mathf.Clamp(requiredRadius, 1, 12);
    }

    private void DrawCustomCellGridControls(
        SerializedObject serializedObject,
        SerializedProperty cellsProperty,
        int cellCount)
    {
        EditorGUILayout.BeginHorizontal();
        DrawRadiusControls();

        GUILayout.Space(8f);
        EditorGUILayout.LabelField("Cells: " + cellCount, GUILayout.Width(64f));
        if (GUILayout.Button("Clear", GUILayout.Width(52f)))
        {
            cellsProperty.arraySize = 0;
            serializedObject.ApplyModifiedProperties();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawPreviewControls(int cellCount)
    {
        EditorGUILayout.BeginHorizontal();
        DrawRadiusControls();
        GUILayout.Space(8f);
        EditorGUILayout.LabelField("Cells: " + cellCount, GUILayout.Width(64f));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRadiusControls()
    {
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
    }

    private void DrawCustomCellGrid(
        SerializedObject serializedObject,
        SerializedProperty cellsProperty,
        HashSet<Vector2Int> cells,
        HashSet<Vector2Int> hintCells,
        Action repaint)
    {
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        Rect gridRect = GetCustomCellGridRect();
        Event evt = Event.current;

        if (evt.type == EventType.Repaint)
            DrawCustomCellGridCells(gridRect, cells, hintCells, true);

        if (evt.type == EventType.MouseDown &&
            (evt.button == 0 || evt.button == 1) &&
            TryGetCustomCellAtPosition(evt.mousePosition, gridRect, out Vector2Int downCell))
        {
            BeginCustomCellStroke(
                controlId,
                downCell,
                evt.button == 1 || cells.Contains(downCell),
                evt.button == 0 && evt.shift);

            if (!_dragRectStart.HasValue)
            {
                ApplyCustomCellPaint(serializedObject, cellsProperty, cells, downCell);
                _strokeVisited.Add(downCell);
                _lastPaintCell = downCell;
            }

            evt.Use();
            repaint?.Invoke();
            return;
        }

        if (_isPainting &&
            evt.type == EventType.MouseDrag &&
            GUIUtility.hotControl == _paintControlId)
        {
            if (_dragRectStart.HasValue)
            {
                _dragRectCurrent = GetCustomCellCoordinate(evt.mousePosition, gridRect);
            }
            else if (TryGetCustomCellAtPosition(
                         evt.mousePosition,
                         gridRect,
                         out Vector2Int dragCell))
            {
                ApplyCustomCellPaintLine(serializedObject, cellsProperty, cells, dragCell);
            }

            evt.Use();
            repaint?.Invoke();
            return;
        }

        if (_isPainting &&
            evt.type == EventType.MouseUp &&
            GUIUtility.hotControl == _paintControlId)
        {
            if (_dragRectStart.HasValue)
            {
                _dragRectCurrent = GetCustomCellCoordinate(evt.mousePosition, gridRect);
                ApplyCustomCellRectangle(serializedObject, cellsProperty, cells);
            }

            EndStroke();
            evt.Use();
            repaint?.Invoke();
        }
    }

    private void DrawPreviewGrid(HashSet<Vector2Int> cells)
    {
        Rect gridRect = GetCustomCellGridRect();
        if (Event.current.type == EventType.Repaint)
            DrawCustomCellGridCells(gridRect, cells, null, false);
    }

    private Rect GetCustomCellGridRect()
    {
        int diameter = _customCellGridRadius * 2 + 1;
        float gridSize = diameter * CustomCellSize;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUI.indentLevel * 15f);
        Rect gridRect = GUILayoutUtility.GetRect(
            gridSize,
            gridSize,
            GUILayout.Width(gridSize),
            GUILayout.Height(gridSize));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        return gridRect;
    }

    private void DrawCustomCellGridCells(
        Rect gridRect,
        HashSet<Vector2Int> cells,
        HashSet<Vector2Int> hintCells,
        bool drawRectanglePreview)
    {
        Color fallback = EditorGUIUtility.isProSkin
            ? new Color(0.28f, 0.28f, 0.28f, 1f)
            : new Color(0.78f, 0.78f, 0.78f, 1f);
        Color border = EditorGUIUtility.isProSkin
            ? new Color(0.12f, 0.12f, 0.12f, 1f)
            : new Color(0.42f, 0.42f, 0.42f, 1f);

        for (int y = _customCellGridRadius; y >= -_customCellGridRadius; y--)
        {
            for (int x = -_customCellGridRadius; x <= _customCellGridRadius; x++)
            {
                Vector2Int cell = new(x, y);
                Rect cellRect = GetCustomCellRect(gridRect, cell);
                bool active = cells.Contains(cell);
                bool hinted = !active && hintCells != null && hintCells.Contains(cell);
                bool isCenter = cell == Vector2Int.zero;

                EditorGUI.DrawRect(cellRect, border);
                Rect fillRect = new(
                    cellRect.x + 1f,
                    cellRect.y + 1f,
                    cellRect.width - 2f,
                    cellRect.height - 2f);
                EditorGUI.DrawRect(
                    fillRect,
                    GetCustomCellButtonColor(active, hinted, isCenter, fallback));

                if (drawRectanglePreview && IsCustomCellRectanglePreview(cell))
                {
                    Color previewColor = _paintErase
                        ? new Color(1f, 0.2f, 0.2f, 0.45f)
                        : new Color(0.2f, 1f, 0.45f, 0.45f);
                    EditorGUI.DrawRect(fillRect, previewColor);
                }

                string label = isCenter ? "P" : active ? "X" : hinted ? "." : string.Empty;
                GUI.Label(cellRect, label, GetCustomCellLabelStyle());
            }
        }
    }

    private Rect GetCustomCellRect(Rect gridRect, Vector2Int cell)
    {
        int column = cell.x + _customCellGridRadius;
        int row = _customCellGridRadius - cell.y;
        return new Rect(
            gridRect.x + column * CustomCellSize,
            gridRect.y + row * CustomCellSize,
            CustomCellSize,
            CustomCellSize);
    }

    private bool TryGetCustomCellAtPosition(
        Vector2 mousePosition,
        Rect gridRect,
        out Vector2Int cell)
    {
        if (!gridRect.Contains(mousePosition))
        {
            cell = default;
            return false;
        }

        cell = GetCustomCellCoordinate(mousePosition, gridRect);
        return Mathf.Abs(cell.x) <= _customCellGridRadius &&
               Mathf.Abs(cell.y) <= _customCellGridRadius;
    }

    private Vector2Int GetCustomCellCoordinate(Vector2 mousePosition, Rect gridRect)
    {
        int column = Mathf.FloorToInt(
            (mousePosition.x - gridRect.x) / CustomCellSize);
        int row = Mathf.FloorToInt(
            (mousePosition.y - gridRect.y) / CustomCellSize);
        return new Vector2Int(
            column - _customCellGridRadius,
            _customCellGridRadius - row);
    }

    private void BeginCustomCellStroke(
        int controlId,
        Vector2Int startCell,
        bool erase,
        bool rectangle)
    {
        Undo.IncrementCurrentGroup();
        _paintUndoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Paint Cells");

        _isPainting = true;
        _paintErase = erase;
        _strokeVisited.Clear();
        _dragRectStart = rectangle ? startCell : (Vector2Int?)null;
        _dragRectCurrent = rectangle ? startCell : (Vector2Int?)null;
        _lastPaintCell = rectangle ? (Vector2Int?)null : startCell;
        _paintControlId = controlId;
        GUIUtility.hotControl = controlId;
    }

    private void ApplyCustomCellPaint(
        SerializedObject serializedObject,
        SerializedProperty cellsProperty,
        HashSet<Vector2Int> cells,
        Vector2Int cell)
    {
        bool changed = _paintErase ? cells.Remove(cell) : cells.Add(cell);
        if (!changed)
            return;

        WriteCustomCellSet(cellsProperty, cells);
        serializedObject.ApplyModifiedProperties();
    }

    private void ApplyCustomCellPaintLine(
        SerializedObject serializedObject,
        SerializedProperty cellsProperty,
        HashSet<Vector2Int> cells,
        Vector2Int endCell)
    {
        Vector2Int startCell = _lastPaintCell ?? endCell;
        int x = startCell.x;
        int y = startCell.y;
        int deltaX = Mathf.Abs(endCell.x - startCell.x);
        int deltaY = Mathf.Abs(endCell.y - startCell.y);
        int stepX = startCell.x < endCell.x ? 1 : -1;
        int stepY = startCell.y < endCell.y ? 1 : -1;
        int error = deltaX - deltaY;
        bool changed = false;

        while (true)
        {
            Vector2Int cell = new(x, y);
            if (_strokeVisited.Add(cell))
                changed |= _paintErase ? cells.Remove(cell) : cells.Add(cell);

            if (x == endCell.x && y == endCell.y)
                break;

            int doubleError = error * 2;
            if (doubleError > -deltaY)
            {
                error -= deltaY;
                x += stepX;
            }
            if (doubleError < deltaX)
            {
                error += deltaX;
                y += stepY;
            }
        }

        _lastPaintCell = endCell;
        if (!changed)
            return;

        WriteCustomCellSet(cellsProperty, cells);
        serializedObject.ApplyModifiedProperties();
    }

    private void ApplyCustomCellRectangle(
        SerializedObject serializedObject,
        SerializedProperty cellsProperty,
        HashSet<Vector2Int> cells)
    {
        if (!_dragRectStart.HasValue || !_dragRectCurrent.HasValue)
            return;

        Vector2Int start = _dragRectStart.Value;
        Vector2Int end = _dragRectCurrent.Value;
        int minX = Mathf.Max(
            -_customCellGridRadius,
            Mathf.Min(start.x, end.x));
        int maxX = Mathf.Min(
            _customCellGridRadius,
            Mathf.Max(start.x, end.x));
        int minY = Mathf.Max(
            -_customCellGridRadius,
            Mathf.Min(start.y, end.y));
        int maxY = Mathf.Min(
            _customCellGridRadius,
            Mathf.Max(start.y, end.y));

        bool changed = false;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2Int cell = new(x, y);
                changed |= _paintErase ? cells.Remove(cell) : cells.Add(cell);
            }
        }

        if (!changed)
            return;

        WriteCustomCellSet(cellsProperty, cells);
        serializedObject.ApplyModifiedProperties();
    }

    private bool IsCustomCellRectanglePreview(Vector2Int cell)
    {
        if (!_isPainting ||
            !_dragRectStart.HasValue ||
            !_dragRectCurrent.HasValue)
        {
            return false;
        }

        Vector2Int start = _dragRectStart.Value;
        Vector2Int end = _dragRectCurrent.Value;
        return cell.x >= Mathf.Min(start.x, end.x) &&
               cell.x <= Mathf.Max(start.x, end.x) &&
               cell.y >= Mathf.Min(start.y, end.y) &&
               cell.y <= Mathf.Max(start.y, end.y);
    }

    private GUIStyle GetCustomCellLabelStyle()
    {
        if (_customCellLabelStyle == null)
        {
            _customCellLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
        }

        return _customCellLabelStyle;
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

    private static void WriteCustomCellSet(
        SerializedProperty cellsProperty,
        HashSet<Vector2Int> cells)
    {
        List<Vector2Int> orderedCells = new(cells);
        orderedCells.Sort((left, right) =>
        {
            int yCompare = left.y.CompareTo(right.y);
            return yCompare != 0 ? yCompare : left.x.CompareTo(right.x);
        });

        cellsProperty.arraySize = orderedCells.Count;
        for (int i = 0; i < orderedCells.Count; i++)
            cellsProperty.GetArrayElementAtIndex(i).vector2IntValue = orderedCells[i];
    }
}
