using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class SkillDashboardWindow : EditorWindow
{
    private const string SplitRatioKey = "SkillDashboard.SplitRatio";
    private const float DefaultSplitRatio = 0.58f;
    private const float MinSplitRatio = 0.22f;
    private const float MaxSplitRatio = 0.78f;
    private const float SplitterHeight = 5f;
    private const float TableWidth = 1390f;
    private const float SkillHeaderHeight = 20f;
    private const float SkillRowHeight = 23f;
    private const float SkillFoldoutWidth = 16f;

    private enum SkillKindFilter
    {
        All,
        Plain,
        Engraving
    }

    private readonly List<SkillRow> _skillRows = new List<SkillRow>(64);
    private readonly List<ValidationResult> _results = new List<ValidationResult>(64);
    private readonly HashSet<SkillData> _expandedSkills = new HashSet<SkillData>();
    private bool _hasScanned;
    private string _search = string.Empty;
    private SkillKindFilter _kindFilter;
    private bool _showInfo = true;
    private Vector2 _skillScrollPosition;
    private Vector2 _resultScrollPosition;
    private float _splitRatio = DefaultSplitRatio;

    [MenuItem("JBRogLike/Skill Dashboard")]
    public static void Open()
    {
        GetWindow<SkillDashboardWindow>("Skill Dashboard");
    }

    private void OnEnable()
    {
        minSize = new Vector2(1050f, 520f);
        _splitRatio = Mathf.Clamp(EditorPrefs.GetFloat(SplitRatioKey, DefaultSplitRatio), MinSplitRatio, MaxSplitRatio);
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        ClearSkillSerializedObjectCache();
        _expandedSkills.Clear();
    }

    private void OnUndoRedoPerformed()
    {
        if (!_hasScanned)
            return;

        Scan();
        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (!_hasScanned)
        {
            EditorGUILayout.HelpBox("Scan을 눌러 스킬/각인/드랍을 검증", MessageType.Info);
            return;
        }

        float availableHeight = Mathf.Max(220f, position.height - 52f);
        float skillPanelHeight = Mathf.Clamp(
            availableHeight * _splitRatio,
            availableHeight * MinSplitRatio,
            availableHeight * MaxSplitRatio);

        DrawSkillsPanel(skillPanelHeight);
        DrawPanelSplitter(availableHeight);
        DrawResultsPanel();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            Scan();

        GUILayout.Space(6f);
        GUILayout.Label("Search", GUILayout.Width(44f));
        _search = GUILayout.TextField(
            _search,
            GUI.skin.FindStyle("ToolbarSearchTextField"),
            GUILayout.MinWidth(160f));

        GUILayout.Space(8f);
        GUILayout.Label("Kind", GUILayout.Width(32f));
        _kindFilter = (SkillKindFilter)EditorGUILayout.EnumPopup(
            _kindFilter,
            EditorStyles.toolbarPopup,
            GUILayout.Width(100f));

        GUILayout.Space(8f);
        _showInfo = GUILayout.Toggle(
            _showInfo,
            "Show Info",
            EditorStyles.toolbarButton,
            GUILayout.Width(82f));

        GUILayout.FlexibleSpace();
        if (_hasScanned)
            GUILayout.Label(_skillRows.Count + " skills / " + _results.Count + " results", EditorStyles.miniLabel);

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSkillsPanel(float height)
    {
        EditorGUILayout.LabelField("Skills", EditorStyles.boldLabel);
        Vector2 headerScrollPosition = new Vector2(_skillScrollPosition.x, 0f);
        EditorGUILayout.BeginHorizontal(GUIStyle.none);
        EditorGUILayout.BeginScrollView(
            headerScrollPosition,
            GUIStyle.none,
            GUIStyle.none,
            GUILayout.Height(SkillHeaderHeight));
        DrawSkillHeader();
        EditorGUILayout.EndScrollView();
        GUILayout.Space(GUI.skin.verticalScrollbar.fixedWidth);
        EditorGUILayout.EndHorizontal();

        _skillScrollPosition = EditorGUILayout.BeginScrollView(
            _skillScrollPosition,
            true,
            true,
            GUILayout.Height(Mathf.Max(60f, height - SkillHeaderHeight)));

        EditorGUILayout.BeginVertical(GUILayout.Width(TableWidth));

        int visibleCount = 0;
        for (int i = 0; i < _skillRows.Count; i++)
        {
            SkillRow row = _skillRows[i];
            if (!IsSkillVisible(row))
                continue;

            DrawSkillRow(row, visibleCount);
            visibleCount++;
        }

        if (visibleCount == 0)
            EditorGUILayout.HelpBox("현재 필터에 맞는 스킬이 없습니다.", MessageType.Info);

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
    }

    private static void DrawSkillHeader()
    {
        Rect headerRect = EditorGUILayout.BeginHorizontal(
            GUIStyle.none,
            GUILayout.Width(TableWidth),
            GUILayout.Height(SkillHeaderHeight));
        if (Event.current.type == EventType.Repaint)
        {
            Color headerColor = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : new Color(0.78f, 0.78f, 0.78f, 1f);
            EditorGUI.DrawRect(headerRect, headerColor);
        }

        GUILayout.Space(SkillFoldoutWidth);
        GUILayout.Label("", GUILayout.Width(42f));
        GUILayout.Label("Name", EditorStyles.miniBoldLabel, GUILayout.Width(190f));
        GUILayout.Label("Kind", EditorStyles.miniBoldLabel, GUILayout.Width(76f));
        GUILayout.Label("Execution", EditorStyles.miniBoldLabel, GUILayout.Width(112f));
        GUILayout.Label("Cooldown", EditorStyles.miniBoldLabel, GUILayout.Width(66f));
        GUILayout.Label("Damage", EditorStyles.miniBoldLabel, GUILayout.Width(58f));
        GUILayout.Label("Form / Grade", EditorStyles.miniBoldLabel, GUILayout.Width(155f));
        GUILayout.Label("Recast", EditorStyles.miniBoldLabel, GUILayout.Width(48f));
        GUILayout.Label("Hits", EditorStyles.miniBoldLabel, GUILayout.Width(40f));
        GUILayout.Label("Cells", EditorStyles.miniBoldLabel, GUILayout.Width(42f));
        GUILayout.Label("Ailments", EditorStyles.miniBoldLabel, GUILayout.Width(52f));
        GUILayout.Label("Linked ItemData itemCode", EditorStyles.miniBoldLabel, GUILayout.Width(390f));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSkillRow(SkillRow row, int visibleIndex)
    {
        Rect rowRect = EditorGUILayout.BeginHorizontal(
            GUIStyle.none,
            GUILayout.Width(TableWidth),
            GUILayout.Height(SkillRowHeight));
        if (visibleIndex % 2 == 0 && Event.current.type == EventType.Repaint)
        {
            Color stripeColor = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.035f)
                : new Color(0f, 0f, 0f, 0.035f);
            EditorGUI.DrawRect(rowRect, stripeColor);
        }

        Rect foldoutRect = GUILayoutUtility.GetRect(
            SkillFoldoutWidth,
            SkillRowHeight,
            GUILayout.Width(SkillFoldoutWidth),
            GUILayout.Height(SkillRowHeight));
        bool hasTarget = row.Asset != null;
        bool isExpanded = hasTarget && _expandedSkills.Contains(row.Asset);
        bool nextExpanded;
        using (new EditorGUI.DisabledScope(!hasTarget))
        {
            nextExpanded = EditorGUI.Foldout(
                foldoutRect,
                isExpanded,
                GUIContent.none,
                false);
        }

        if (nextExpanded != isExpanded)
        {
            if (nextExpanded)
                _expandedSkills.Add(row.Asset);
            else
                _expandedSkills.Remove(row.Asset);
        }

        using (new EditorGUI.DisabledScope(!hasTarget))
        {
            if (GUILayout.Button("Ping", GUILayout.Width(42f)))
                EditorGUIUtility.PingObject(row.Asset);
        }

        GUILayout.Label(row.DisplayName, GUILayout.Width(190f));
        GUILayout.Label(row.IsEngraving ? "Engraving" : "Plain", GUILayout.Width(76f));
        GUILayout.Label(row.ExecutionTypeText, GUILayout.Width(112f));
        GUILayout.Label(row.CooldownText, GUILayout.Width(66f));
        GUILayout.Label(row.DamageText, GUILayout.Width(58f));
        GUILayout.Label(row.FormAndGrade, GUILayout.Width(155f));
        GUILayout.Label(row.RecastCount.ToString(), GUILayout.Width(48f));
        GUILayout.Label(row.HitStepCount.ToString(), GUILayout.Width(40f));
        GUILayout.Label(row.CustomCellCount.ToString(), GUILayout.Width(42f));
        GUILayout.Label(row.AilmentCount.ToString(), GUILayout.Width(52f));
        GUILayout.Label(row.LinkedItemCodesText, GUILayout.Width(390f));

        EditorGUILayout.EndHorizontal();

        if (nextExpanded)
            DrawSkillEditPanel(row);
    }

    private void DrawSkillEditPanel(SkillRow row)
    {
        SerializedObject skillObject = row.SerializedObject;
        if (skillObject == null || skillObject.targetObject == null)
            return;

        skillObject.Update();

        SerializedProperty skillName = skillObject.FindProperty("skillName");
        SerializedProperty executionType = skillObject.FindProperty("executionType");
        SerializedProperty resourceType = skillObject.FindProperty("resourceType");
        SerializedProperty requiredAmount = skillObject.FindProperty("requiredAmount");
        SerializedProperty consumeAmount = skillObject.FindProperty("consumeAmount");
        SerializedProperty cooldown = skillObject.FindProperty("cooldown");
        SerializedProperty castDelay = skillObject.FindProperty("castDelay");
        SerializedProperty recoveryDelay = skillObject.FindProperty("recoveryDelay");
        SerializedProperty recastWindow = skillObject.FindProperty("recastWindow");
        SerializedProperty damage = skillObject.FindProperty("damage");
        SerializedProperty cancelable = skillObject.FindProperty("cancelable");
        SerializedProperty owningForm = skillObject.FindProperty("owningForm");
        SerializedProperty grade = skillObject.FindProperty("grade");

        Rect panelRect = EditorGUILayout.BeginVertical(
            GUIStyle.none,
            GUILayout.Width(TableWidth));
        if (Event.current.type == EventType.Repaint)
        {
            Color panelColor = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.16f, 0.16f, 1f)
                : new Color(0.9f, 0.9f, 0.9f, 1f);
            EditorGUI.DrawRect(panelRect, panelColor);
        }

        GUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal(GUIStyle.none);
        GUILayout.Space(SkillFoldoutWidth + 4f);
        DrawSkillProperty(skillName, "Skill Name", 330f, 72f);
        DrawSkillProperty(executionType, "Execution", 230f, 68f);
        DrawSkillProperty(resourceType, "Resource", 190f, 64f);
        DrawSkillProperty(cancelable, "Cancelable", 150f, 68f);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal(GUIStyle.none);
        GUILayout.Space(SkillFoldoutWidth + 4f);
        DrawSkillProperty(requiredAmount, "Required", 165f, 60f);
        DrawSkillProperty(consumeAmount, "Consume", 165f, 60f);
        DrawSkillProperty(cooldown, "Cooldown", 165f, 62f);
        DrawSkillProperty(castDelay, "Cast Delay", 165f, 66f);
        DrawSkillProperty(recoveryDelay, "Recovery", 175f, 64f);
        DrawSkillProperty(recastWindow, "Recast Win", 175f, 70f);
        DrawSkillProperty(damage, "Damage", 150f, 58f);
        EditorGUILayout.EndHorizontal();

        if (owningForm != null && grade != null)
        {
            EditorGUILayout.BeginHorizontal(GUIStyle.none);
            GUILayout.Space(SkillFoldoutWidth + 4f);
            DrawSkillProperty(owningForm, "Owning Form", 230f, 84f);
            DrawSkillProperty(grade, "Grade", 190f, 52f);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal(GUIStyle.none);
        GUILayout.Space(SkillFoldoutWidth + 4f);
        GUILayout.Label(
            "Read-only counts — Recast " + row.RecastCount +
            " / Hit Steps " + row.HitStepCount +
            " / Custom Cells " + row.CustomCellCount +
            " / Ailments " + row.AilmentCount,
            EditorStyles.miniLabel,
            GUILayout.Width(580f));
        EditorGUILayout.HelpBox(
            "복잡 필드는 인스펙터에서 편집",
            MessageType.Info);
        if (GUILayout.Button("Ping / Inspector", GUILayout.Width(130f)))
        {
            Selection.activeObject = row.Asset;
            EditorGUIUtility.PingObject(row.Asset);
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(4f);
        EditorGUILayout.EndVertical();

        if (skillObject.ApplyModifiedProperties())
        {
            RefreshSkillRowCachedValues(row, skillObject);
            Repaint();
        }
    }

    private static void DrawSkillProperty(
        SerializedProperty property,
        string label,
        float width,
        float labelWidth)
    {
        if (property == null)
            return;

        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = labelWidth;
        GUIContent labelContent = new GUIContent(label);
        switch (property.propertyType)
        {
            case SerializedPropertyType.String:
            {
                string nextValue = EditorGUILayout.TextField(
                    labelContent,
                    property.stringValue,
                    GUILayout.Width(width));
                if (!string.Equals(nextValue, property.stringValue, StringComparison.Ordinal))
                    property.stringValue = nextValue;
                break;
            }

            case SerializedPropertyType.Enum:
            {
                int nextValue = EditorGUILayout.Popup(
                    labelContent,
                    property.enumValueIndex,
                    property.enumDisplayNames,
                    GUILayout.Width(width));
                if (nextValue != property.enumValueIndex)
                    property.enumValueIndex = nextValue;
                break;
            }

            case SerializedPropertyType.Integer:
            {
                int nextValue = Mathf.Max(
                    0,
                    EditorGUILayout.IntField(
                        labelContent,
                        property.intValue,
                        GUILayout.Width(width)));
                if (nextValue != property.intValue)
                    property.intValue = nextValue;
                break;
            }

            case SerializedPropertyType.Float:
            {
                float nextValue = Mathf.Max(
                    0f,
                    EditorGUILayout.FloatField(
                        labelContent,
                        property.floatValue,
                        GUILayout.Width(width)));
                if (!Mathf.Approximately(nextValue, property.floatValue))
                    property.floatValue = nextValue;
                break;
            }

            case SerializedPropertyType.Boolean:
            {
                bool nextValue = EditorGUILayout.Toggle(
                    labelContent,
                    property.boolValue,
                    GUILayout.Width(width));
                if (nextValue != property.boolValue)
                    property.boolValue = nextValue;
                break;
            }
        }
        EditorGUIUtility.labelWidth = previousLabelWidth;
    }

    private static void RefreshSkillRowCachedValues(
        SkillRow row,
        SerializedObject skillObject)
    {
        SerializedProperty skillName = skillObject.FindProperty("skillName");
        string serializedName = GetString(skillName);
        row.DisplayName = string.IsNullOrWhiteSpace(serializedName)
            ? row.Asset.name
            : serializedName;

        SerializedProperty owningForm = skillObject.FindProperty("owningForm");
        SerializedProperty grade = skillObject.FindProperty("grade");
        row.FormAndGrade = owningForm != null && grade != null
            ? ((PlayerFormId)owningForm.intValue) + " / " + ((EngravingGrade)grade.intValue)
            : "—";

        SerializedProperty executionType = skillObject.FindProperty("executionType");
        SerializedProperty cooldown = skillObject.FindProperty("cooldown");
        SerializedProperty damage = skillObject.FindProperty("damage");
        row.ExecutionTypeText = executionType != null
            ? ((SkillExecutionType)executionType.intValue).ToString()
            : "—";
        row.CooldownText = cooldown != null ? cooldown.floatValue.ToString("0.###") : "—";
        row.DamageText = damage != null ? damage.intValue.ToString() : "—";

        row.RecastCount = GetArraySize(skillObject.FindProperty("recastStages"));
        row.HitStepCount = GetArraySize(skillObject.FindProperty("hitSteps"));
        row.CustomCellCount = GetArraySize(skillObject.FindProperty("customCells"));
        row.AilmentCount = GetArraySize(skillObject.FindProperty("ailments"));
    }

    private static int GetArraySize(SerializedProperty property)
    {
        return property != null && property.isArray ? property.arraySize : 0;
    }

    private bool IsSkillVisible(SkillRow row)
    {
        if (_kindFilter == SkillKindFilter.Plain && row.IsEngraving)
            return false;
        if (_kindFilter == SkillKindFilter.Engraving && !row.IsEngraving)
            return false;

        string query = NormalizeCode(_search);
        if (string.IsNullOrEmpty(query))
            return true;

        return ContainsIgnoreCase(row.DisplayName, query) ||
               ContainsIgnoreCase(row.LinkedItemCodesText, query);
    }

    private static bool ContainsIgnoreCase(string value, string query)
    {
        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void DrawPanelSplitter(float availableHeight)
    {
        Rect splitterRect = GUILayoutUtility.GetRect(0f, SplitterHeight, GUILayout.ExpandWidth(true));
        int id = GUIUtility.GetControlID(FocusType.Passive);
        Event evt = Event.current;

        if (evt.type == EventType.Repaint)
        {
            Color color = EditorGUIUtility.isProSkin
                ? new Color(0.32f, 0.32f, 0.32f, 1f)
                : new Color(0.62f, 0.62f, 0.62f, 1f);
            EditorGUI.DrawRect(new Rect(splitterRect.x, splitterRect.y + 2f, splitterRect.width, 1f), color);
        }

        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);
        switch (evt.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (evt.button == 0 && splitterRect.Contains(evt.mousePosition))
                {
                    GUIUtility.hotControl = id;
                    evt.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    _splitRatio = Mathf.Clamp(
                        _splitRatio + evt.delta.y / availableHeight,
                        MinSplitRatio,
                        MaxSplitRatio);
                    evt.Use();
                    Repaint();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    EditorPrefs.SetFloat(SplitRatioKey, _splitRatio);
                    evt.Use();
                }
                break;
        }
    }

    private void DrawResultsPanel()
    {
        int errorCount = 0;
        int warningCount = 0;
        int infoCount = 0;
        for (int i = 0; i < _results.Count; i++)
        {
            switch (_results[i].Severity)
            {
                case ResultSeverity.Error:
                    errorCount++;
                    break;
                case ResultSeverity.Warning:
                    warningCount++;
                    break;
                default:
                    infoCount++;
                    break;
            }
        }

        EditorGUILayout.LabelField(
            "Validation — Error " + errorCount + " / Warning " + warningCount + " / Info " + infoCount,
            EditorStyles.boldLabel);

        _resultScrollPosition = EditorGUILayout.BeginScrollView(_resultScrollPosition, false, true);
        int visibleCount = 0;
        for (int i = 0; i < _results.Count; i++)
        {
            ValidationResult result = _results[i];
            if (!_showInfo && result.Severity == ResultSeverity.Info)
                continue;

            DrawResult(result);
            visibleCount++;
        }

        if (visibleCount == 0)
            EditorGUILayout.HelpBox("No issues found for current filters.", MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void DrawResult(ValidationResult result)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        GUILayout.Label(GetSeverityIcon(result.Severity), GUILayout.Width(24f), GUILayout.Height(20f));
        EditorGUILayout.LabelField(result.Message, EditorStyles.wordWrappedLabel);

        if (result.Target != null && GUILayout.Button("Ping", GUILayout.Width(48f)))
            EditorGUIUtility.PingObject(result.Target);

        if (result.CanFix && GUILayout.Button(result.FixLabel, GUILayout.Width(140f)))
        {
            ApplyFix(result);
            GUIUtility.ExitGUI();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void Scan()
    {
        ClearSkillSerializedObjectCache();
        _skillRows.Clear();
        _results.Clear();
        _hasScanned = true;
        _expandedSkills.RemoveWhere(skill => skill == null);

        List<SkillData> skills = LoadAssets<SkillData>("t:SkillData");
        List<PassiveEngravingData> passiveEngravings =
            LoadAssets<PassiveEngravingData>("t:PassiveEngravingData");
        List<ItemDatabase> itemDatabases = LoadAssets<ItemDatabase>("t:ItemDatabase");
        List<EnemyDropDatabase> dropDatabases = LoadAssets<EnemyDropDatabase>("t:EnemyDropDatabase");

        ScanContext context = BuildItemContext(itemDatabases);
        BuildSkillRows(skills, context);
        List<DropRecord> dropRecords = BuildDropRecords(dropDatabases);

        AddOrphanResults(skills, passiveEngravings, itemDatabases, context);
        AddItemDatabaseResults(context);
        AddDropDatabaseResults(dropRecords, context);
    }

    private void BuildSkillRows(List<SkillData> skills, ScanContext context)
    {
        for (int i = 0; i < skills.Count; i++)
        {
            SkillData skill = skills[i];
            if (skill == null)
                continue;

            EngravingData engraving = skill as EngravingData;
            List<ItemRecord> linkedItems = null;
            if (engraving != null)
                context.ItemsByEngraving.TryGetValue(engraving, out linkedItems);

            _skillRows.Add(new SkillRow
            {
                Asset = skill,
                SerializedObject = new SerializedObject(skill),
                DisplayName = string.IsNullOrWhiteSpace(skill.skillName) ? skill.name : skill.skillName,
                IsEngraving = engraving != null,
                ExecutionTypeText = skill.executionType.ToString(),
                CooldownText = skill.cooldown.ToString("0.###"),
                DamageText = skill.damage.ToString(),
                FormAndGrade = engraving != null ? engraving.owningForm + " / " + engraving.grade : "—",
                RecastCount = skill.recastStages != null ? skill.recastStages.Count : 0,
                HitStepCount = skill.hitSteps != null ? skill.hitSteps.Count : 0,
                CustomCellCount = skill.customCells != null ? skill.customCells.Count : 0,
                AilmentCount = skill.ailments != null ? skill.ailments.Length : 0,
                LinkedItemCodesText = BuildLinkedItemCodes(linkedItems)
            });
        }
    }

    private void ClearSkillSerializedObjectCache()
    {
        for (int i = 0; i < _skillRows.Count; i++)
        {
            SerializedObject skillObject = _skillRows[i].SerializedObject;
            if (skillObject == null)
                continue;

            skillObject.Dispose();
            _skillRows[i].SerializedObject = null;
        }
    }

    private static string BuildLinkedItemCodes(List<ItemRecord> items)
    {
        if (items == null || items.Count == 0)
            return "(none)";

        List<string> codes = new List<string>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            string code = DisplayCode(items[i]);
            if (!codes.Contains(code))
                codes.Add(code);
        }

        codes.Sort(StringComparer.Ordinal);
        return string.Join(", ", codes.ToArray());
    }

    private ScanContext BuildItemContext(List<ItemDatabase> itemDatabases)
    {
        ScanContext context = new ScanContext();

        for (int dbIndex = 0; dbIndex < itemDatabases.Count; dbIndex++)
        {
            ItemDatabase database = itemDatabases[dbIndex];
            if (database == null)
                continue;

            SerializedObject databaseObject = new SerializedObject(database);
            SerializedProperty items = databaseObject.FindProperty("items");
            if (items == null || !items.isArray)
            {
                AddResult(
                    ResultSeverity.Error,
                    "ItemDatabase '" + database.name + "' has no serialized items list.",
                    database);
                continue;
            }

            Dictionary<string, List<ItemRecord>> codesInDatabase =
                new Dictionary<string, List<ItemRecord>>(StringComparer.Ordinal);
            context.CodesByDatabase.Add(database, codesInDatabase);

            for (int itemIndex = 0; itemIndex < items.arraySize; itemIndex++)
            {
                SerializedProperty item = items.GetArrayElementAtIndex(itemIndex);
                ItemRecord record = CreateItemRecord(database, itemIndex, item);
                context.Items.Add(record);

                if (record.Engraving != null)
                    AddToLookup(context.ItemsByEngraving, record.Engraving, record);
                if (record.PassiveEngraving != null)
                    AddToLookup(context.ItemsByPassiveEngraving, record.PassiveEngraving, record);

                if (string.IsNullOrEmpty(record.Code))
                {
                    AddResult(
                        ResultSeverity.Warning,
                        "Empty itemCode in ItemDatabase '" + database.name + "' entry " + itemIndex + ".",
                        database);
                    continue;
                }

                AddToLookup(context.ItemsByCode, record.Code, record);
                AddToLookup(codesInDatabase, record.Code, record);
            }
        }

        return context;
    }

    private static ItemRecord CreateItemRecord(
        ItemDatabase database,
        int itemIndex,
        SerializedProperty item)
    {
        SerializedProperty itemCode = item.FindPropertyRelative("itemCode");
        SerializedProperty displayName = item.FindPropertyRelative("displayName");
        SerializedProperty itemType = item.FindPropertyRelative("itemType");
        SerializedProperty engraving = item.FindPropertyRelative("engraving");
        SerializedProperty passiveEngraving = item.FindPropertyRelative("passiveEngraving");

        return new ItemRecord
        {
            Database = database,
            Index = itemIndex,
            Code = NormalizeCode(GetString(itemCode)),
            DisplayName = GetString(displayName),
            ItemType = itemType != null ? (ItemType)itemType.intValue : ItemType.Key,
            Engraving = engraving != null ? engraving.objectReferenceValue as EngravingData : null,
            PassiveEngraving = passiveEngraving != null
                ? passiveEngraving.objectReferenceValue as PassiveEngravingData
                : null
        };
    }

    private List<DropRecord> BuildDropRecords(List<EnemyDropDatabase> dropDatabases)
    {
        List<DropRecord> records = new List<DropRecord>(64);

        for (int dbIndex = 0; dbIndex < dropDatabases.Count; dbIndex++)
        {
            EnemyDropDatabase database = dropDatabases[dbIndex];
            if (database == null)
                continue;

            SerializedObject databaseObject = new SerializedObject(database);
            SerializedProperty groups = databaseObject.FindProperty("groups");
            if (groups == null || !groups.isArray)
            {
                AddResult(
                    ResultSeverity.Error,
                    "EnemyDropDatabase '" + database.name + "' has no serialized groups list.",
                    database);
            }
            else
            {
                for (int groupIndex = 0; groupIndex < groups.arraySize; groupIndex++)
                {
                    SerializedProperty group = groups.GetArrayElementAtIndex(groupIndex);
                    AddDropGroup(
                        database,
                        groupIndex,
                        GetGroupLabel(group, groupIndex),
                        group,
                        records);
                }
            }

            AddDropGroup(
                database,
                -1,
                "Rank:Normal",
                databaseObject.FindProperty("normalRankDrops"),
                records);
            AddDropGroup(
                database,
                -1,
                "Rank:Elite",
                databaseObject.FindProperty("eliteRankDrops"),
                records);
            AddDropGroup(
                database,
                -1,
                "Rank:Boss",
                databaseObject.FindProperty("bossRankDrops"),
                records);
        }

        return records;
    }

    private static void AddDropGroup(
        EnemyDropDatabase database,
        int groupIndex,
        string groupLabel,
        SerializedProperty group,
        List<DropRecord> records)
    {
        if (group == null)
            return;

        SerializedProperty drops = group.FindPropertyRelative("drops");
        if (drops != null && drops.isArray)
            AddDropEntries(database, groupIndex, groupLabel, drops, records);

        SerializedProperty choiceGroups = group.FindPropertyRelative("choiceGroups");
        if (choiceGroups != null && choiceGroups.isArray)
            AddChoiceEntries(database, groupIndex, groupLabel, choiceGroups, records);
    }

    private static void AddDropEntries(
        EnemyDropDatabase database,
        int groupIndex,
        string groupLabel,
        SerializedProperty drops,
        List<DropRecord> records)
    {
        for (int dropIndex = 0; dropIndex < drops.arraySize; dropIndex++)
        {
            SerializedProperty drop = drops.GetArrayElementAtIndex(dropIndex);
            records.Add(CreateDropRecord(
                database,
                groupIndex,
                groupLabel,
                "drops[" + dropIndex + "]",
                drop.FindPropertyRelative("itemCode"),
                drop.FindPropertyRelative("minAmount"),
                drop.FindPropertyRelative("maxAmount")));
        }
    }

    private static void AddChoiceEntries(
        EnemyDropDatabase database,
        int groupIndex,
        string groupLabel,
        SerializedProperty choiceGroups,
        List<DropRecord> records)
    {
        for (int choiceGroupIndex = 0; choiceGroupIndex < choiceGroups.arraySize; choiceGroupIndex++)
        {
            SerializedProperty choiceGroup = choiceGroups.GetArrayElementAtIndex(choiceGroupIndex);
            SerializedProperty choices = choiceGroup.FindPropertyRelative("choices");
            if (choices == null || !choices.isArray)
                continue;

            for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
            {
                SerializedProperty choice = choices.GetArrayElementAtIndex(choiceIndex);
                records.Add(CreateDropRecord(
                    database,
                    groupIndex,
                    groupLabel,
                    "choiceGroups[" + choiceGroupIndex + "].choices[" + choiceIndex + "]",
                    choice.FindPropertyRelative("itemCode"),
                    choice.FindPropertyRelative("minAmount"),
                    choice.FindPropertyRelative("maxAmount")));
            }
        }
    }

    private static DropRecord CreateDropRecord(
        EnemyDropDatabase database,
        int groupIndex,
        string groupLabel,
        string path,
        SerializedProperty itemCode,
        SerializedProperty minAmount,
        SerializedProperty maxAmount)
    {
        return new DropRecord
        {
            Database = database,
            GroupIndex = groupIndex,
            GroupLabel = groupLabel,
            Path = path,
            Code = NormalizeCode(GetString(itemCode)),
            MinAmount = minAmount != null ? minAmount.intValue : 0,
            MaxAmount = maxAmount != null ? maxAmount.intValue : 0
        };
    }

    private void AddOrphanResults(
        List<SkillData> skills,
        List<PassiveEngravingData> passiveEngravings,
        List<ItemDatabase> itemDatabases,
        ScanContext context)
    {
        ItemDatabase targetDatabase = itemDatabases.Count > 0 ? itemDatabases[0] : null;

        for (int i = 0; i < skills.Count; i++)
        {
            EngravingData engraving = skills[i] as EngravingData;
            if (engraving == null || context.ItemsByEngraving.ContainsKey(engraving))
                continue;

            AddOrphanResult("engraving", engraving, targetDatabase, ItemType.Engraving);
        }

        for (int i = 0; i < passiveEngravings.Count; i++)
        {
            PassiveEngravingData passive = passiveEngravings[i];
            if (passive == null || context.ItemsByPassiveEngraving.ContainsKey(passive))
                continue;

            AddOrphanResult(
                "passive engraving",
                passive,
                targetDatabase,
                ItemType.PassiveEngraving);
        }
    }

    private void AddOrphanResult(
        string kindLabel,
        UnityEngine.Object asset,
        ItemDatabase targetDatabase,
        ItemType itemType)
    {
        string message =
            "Orphan " + kindLabel + " '" + asset.name + "': no ItemDatabase entry references it.";
        if (targetDatabase != null)
            message += " Fix target: '" + targetDatabase.name + "'.";
        else
            message += " No ItemDatabase found for auto-fix.";

        ValidationResult result = AddResult(ResultSeverity.Error, message, asset);
        if (targetDatabase == null)
            return;

        result.CanFix = true;
        result.FixLabel = "Add to ItemDatabase";
        result.FixDatabase = targetDatabase;
        result.FixAsset = asset;
        result.FixItemType = itemType;
    }

    private void AddItemDatabaseResults(ScanContext context)
    {
        for (int i = 0; i < context.Items.Count; i++)
        {
            ItemRecord item = context.Items[i];
            if (item.ItemType == ItemType.Engraving && item.Engraving == null)
            {
                AddResult(
                    ResultSeverity.Error,
                    FormatItemPrefix(item) + " has itemType Engraving but engraving is null.",
                    item.Database);
            }

            if (item.Engraving != null && item.ItemType != ItemType.Engraving)
            {
                AddResult(
                    ResultSeverity.Error,
                    FormatItemPrefix(item) + " references engraving '" + item.Engraving.name +
                    "' but itemType is " + item.ItemType + ".",
                    item.Database);
            }

            if (item.ItemType == ItemType.PassiveEngraving && item.PassiveEngraving == null)
            {
                AddResult(
                    ResultSeverity.Error,
                    FormatItemPrefix(item) +
                    " has itemType PassiveEngraving but passiveEngraving is null.",
                    item.Database);
            }

            if (item.PassiveEngraving != null && item.ItemType != ItemType.PassiveEngraving)
            {
                AddResult(
                    ResultSeverity.Error,
                    FormatItemPrefix(item) + " references passive engraving '" +
                    item.PassiveEngraving.name + "' but itemType is " + item.ItemType + ".",
                    item.Database);
            }
        }

        foreach (KeyValuePair<EngravingData, List<ItemRecord>> pair in context.ItemsByEngraving)
        {
            if (pair.Value.Count < 2)
                continue;

            AddResult(
                ResultSeverity.Warning,
                "Duplicate engraving reference '" + pair.Key.name + "': " +
                FormatItemLocations(pair.Value) + ".",
                pair.Key);
        }

        foreach (
            KeyValuePair<PassiveEngravingData, List<ItemRecord>> pair
            in context.ItemsByPassiveEngraving)
        {
            if (pair.Value.Count < 2)
                continue;

            AddResult(
                ResultSeverity.Warning,
                "Duplicate passive engraving reference '" + pair.Key.name + "': " +
                FormatItemLocations(pair.Value) + ".",
                pair.Key);
        }

        foreach (
            KeyValuePair<ItemDatabase, Dictionary<string, List<ItemRecord>>> databasePair
            in context.CodesByDatabase)
        {
            foreach (KeyValuePair<string, List<ItemRecord>> codePair in databasePair.Value)
            {
                if (codePair.Value.Count < 2)
                    continue;

                AddResult(
                    ResultSeverity.Warning,
                    "Duplicate itemCode '" + codePair.Key + "' in ItemDatabase '" +
                    databasePair.Key.name + "': " + FormatItemLocations(codePair.Value) + ".",
                    databasePair.Key);
            }
        }
    }

    private void AddDropDatabaseResults(List<DropRecord> dropRecords, ScanContext context)
    {
        for (int i = 0; i < dropRecords.Count; i++)
        {
            DropRecord drop = dropRecords[i];
            string location = FormatDropLocation(drop);

            if (string.IsNullOrEmpty(drop.Code))
            {
                AddResult(
                    ResultSeverity.Warning,
                    "Empty itemCode in " + location + ".",
                    drop.Database);
                continue;
            }

            if (!context.ItemsByCode.ContainsKey(drop.Code))
            {
                AddResult(
                    ResultSeverity.Error,
                    "Dead drop itemCode '" + drop.Code + "' in " + location +
                    ": no ItemDatabase entry exists.",
                    drop.Database);
                continue;
            }

            if (!IsEngravingLikeCode(drop.Code, context))
                continue;

            int effectiveMax = Mathf.Max(Mathf.Max(1, drop.MinAmount), drop.MaxAmount);
            if (effectiveMax > 1)
            {
                AddResult(
                    ResultSeverity.Warning,
                    "Engraving-like drop itemCode '" + drop.Code + "' in " + location +
                    " rolls up to " + effectiveMax + " (>1). Set min=max=1.",
                    drop.Database);
            }
        }
    }

    private void ApplyFix(ValidationResult result)
    {
        if (!result.CanFix || result.FixDatabase == null || result.FixAsset == null)
            return;

        if (result.FixItemType == ItemType.Engraving)
        {
            EngravingData engraving = result.FixAsset as EngravingData;
            if (engraving == null)
                return;
            AddEngravingToItemDatabase(result.FixDatabase, engraving);
        }
        else if (result.FixItemType == ItemType.PassiveEngraving)
        {
            PassiveEngravingData passive = result.FixAsset as PassiveEngravingData;
            if (passive == null)
                return;
            AddPassiveEngravingToItemDatabase(result.FixDatabase, passive);
        }
        else
        {
            return;
        }

        EditorGUIUtility.PingObject(result.FixDatabase);
        Scan();
    }

    private static void AddEngravingToItemDatabase(
        ItemDatabase database,
        EngravingData engraving)
    {
        AddEngravingLikeToItemDatabase(
            database,
            engraving,
            ItemType.Engraving,
            string.IsNullOrWhiteSpace(engraving.skillName) ? engraving.name : engraving.skillName,
            engraving.owningForm,
            engraving.grade);
    }

    private static void AddPassiveEngravingToItemDatabase(
        ItemDatabase database,
        PassiveEngravingData passive)
    {
        AddEngravingLikeToItemDatabase(
            database,
            passive,
            ItemType.PassiveEngraving,
            string.IsNullOrWhiteSpace(passive.passiveName) ? passive.name : passive.passiveName,
            passive.owningForm,
            passive.grade);
    }

    private static void AddEngravingLikeToItemDatabase(
        ItemDatabase database,
        UnityEngine.Object asset,
        ItemType itemType,
        string displayName,
        PlayerFormId owningForm,
        EngravingGrade grade)
    {
        SerializedObject databaseObject = new SerializedObject(database);
        SerializedProperty items = databaseObject.FindProperty("items");
        if (items == null || !items.isArray)
            return;

        HashSet<string> existingCodes = new HashSet<string>(StringComparer.Ordinal);
        List<string> existingTypeCodes = new List<string>(8);
        for (int i = 0; i < items.arraySize; i++)
        {
            SerializedProperty item = items.GetArrayElementAtIndex(i);
            string code = NormalizeCode(GetString(item.FindPropertyRelative("itemCode")));
            if (!string.IsNullOrEmpty(code))
                existingCodes.Add(code);

            SerializedProperty typeProperty = item.FindPropertyRelative("itemType");
            if (typeProperty != null &&
                typeProperty.intValue == (int)itemType &&
                !string.IsNullOrEmpty(code))
            {
                existingTypeCodes.Add(code);
            }
        }

        Undo.RecordObject(
            database,
            itemType == ItemType.Engraving
                ? "Add Engraving ItemData"
                : "Add Passive Engraving ItemData");

        items.arraySize++;
        SerializedProperty newItem = items.GetArrayElementAtIndex(items.arraySize - 1);
        ResetItemEntry(newItem);

        SetString(
            newItem,
            "itemCode",
            GenerateUniqueItemCode(
                itemType,
                owningForm,
                grade,
                existingCodes,
                existingTypeCodes));
        SetString(newItem, "displayName", displayName);
        SetInt(newItem, "itemType", (int)itemType);
        SetObject(
            newItem,
            itemType == ItemType.Engraving ? "engraving" : "passiveEngraving",
            asset);
        SetBool(newItem, "stackable", false);
        SetInt(newItem, "maxStack", 1);

        databaseObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(database);
    }

    private static void ResetItemEntry(SerializedProperty item)
    {
        SetString(item, "itemCode", string.Empty);
        SetString(item, "displayName", string.Empty);
        SetObject(item, "icon", null);
        SetString(item, "description", string.Empty);
        SetInt(item, "itemType", (int)ItemType.Key);
        SetInt(item, "rarity", (int)ItemRarity.Common);
        SetBool(item, "stackable", false);
        SetInt(item, "maxStack", 1);
        SetBool(item, "removeOnFloorTransition", false);
        SetBool(item, "removeOnDungeonExit", false);
        SetArraySize(item, "useEffects", 0);
        SetArraySize(item, "passiveEffects", 0);
        SetArraySize(item, "behaviorEffects", 0);
        SetInt(item, "soulFormId", (int)PlayerFormId.Normal);
        SetObject(item, "engraving", null);
        SetObject(item, "passiveEngraving", null);
        SetString(item, "salvageItemCode", string.Empty);
        SetInt(item, "salvageMinAmount", 1);
        SetInt(item, "salvageMaxAmount", 1);
    }

    private static string GenerateUniqueItemCode(
        ItemType itemType,
        PlayerFormId owningForm,
        EngravingGrade grade,
        HashSet<string> existingCodes,
        List<string> existingTypeCodes)
    {
        string baseCode = GenerateItemCodeBase(itemType, owningForm, grade, existingTypeCodes);
        if (!existingCodes.Contains(baseCode))
            return baseCode;

        int suffix = 2;
        while (true)
        {
            string candidate = baseCode + "_" + suffix;
            if (!existingCodes.Contains(candidate))
                return candidate;
            suffix++;
        }
    }

    private static string GenerateItemCodeBase(
        ItemType itemType,
        PlayerFormId owningForm,
        EngravingGrade grade,
        List<string> existingTypeCodes)
    {
        bool hasExisting = existingTypeCodes.Count > 0;
        bool lowerSnake = hasExisting && IsLowerSnake(existingTypeCodes[0]);
        string prefix = itemType == ItemType.Engraving ? "Eng" : "Psv";

        if (lowerSnake)
        {
            return prefix.ToLowerInvariant() + "_" +
                   ToSnakeToken(owningForm.ToString()) + "_" +
                   ToSnakeToken(grade.ToString());
        }

        return prefix + "_" +
               SanitizePascalToken(owningForm.ToString()) + "_" +
               SanitizePascalToken(grade.ToString());
    }

    private ValidationResult AddResult(
        ResultSeverity severity,
        string message,
        UnityEngine.Object target)
    {
        ValidationResult result = new ValidationResult
        {
            Severity = severity,
            Message = message,
            Target = target,
            FixLabel = string.Empty
        };
        _results.Add(result);
        return result;
    }

    private static bool IsEngravingLikeCode(string code, ScanContext context)
    {
        if (!context.ItemsByCode.TryGetValue(code, out List<ItemRecord> items))
            return false;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].ItemType == ItemType.Engraving ||
                items[i].ItemType == ItemType.PassiveEngraving)
            {
                return true;
            }
        }

        return false;
    }

    private static GUIContent GetSeverityIcon(ResultSeverity severity)
    {
        switch (severity)
        {
            case ResultSeverity.Error:
                return EditorGUIUtility.IconContent("console.erroricon");
            case ResultSeverity.Warning:
                return EditorGUIUtility.IconContent("console.warnicon");
            default:
                return EditorGUIUtility.IconContent("console.infoicon");
        }
    }

    private static string FormatItemPrefix(ItemRecord item)
    {
        return "ItemDatabase '" + item.Database.name + "' entry " + item.Index +
               " itemCode '" + DisplayCode(item) + "'";
    }

    private static string FormatItemLocations(List<ItemRecord> items)
    {
        string result = string.Empty;
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
                result += ", ";

            ItemRecord item = items[i];
            result += item.Database.name + "[" + item.Index + "] '" + DisplayCode(item) + "'";
        }
        return result;
    }

    private static string FormatDropLocation(DropRecord drop)
    {
        string group = drop.GroupIndex >= 0
            ? "groups[" + drop.GroupIndex + "] (" + drop.GroupLabel + ")"
            : drop.GroupLabel;
        return "EnemyDropDatabase '" + drop.Database.name + "' " + group + " " + drop.Path;
    }

    private static string GetGroupLabel(SerializedProperty group, int groupIndex)
    {
        SerializedProperty enemy = group.FindPropertyRelative("enemy");
        if (enemy != null && enemy.objectReferenceValue != null)
            return enemy.objectReferenceValue.name;
        return "group " + groupIndex;
    }

    private static string DisplayCode(ItemRecord item)
    {
        return string.IsNullOrEmpty(item.Code) ? "<empty>" : item.Code;
    }

    private static string NormalizeCode(string code)
    {
        return string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim();
    }

    private static string GetString(SerializedProperty property)
    {
        return property != null ? property.stringValue : string.Empty;
    }

    private static void SetString(SerializedProperty parent, string propertyName, string value)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null)
            property.stringValue = value;
    }

    private static void SetBool(SerializedProperty parent, string propertyName, bool value)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null)
            property.boolValue = value;
    }

    private static void SetInt(SerializedProperty parent, string propertyName, int value)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null)
            property.intValue = value;
    }

    private static void SetObject(
        SerializedProperty parent,
        string propertyName,
        UnityEngine.Object value)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static void SetArraySize(
        SerializedProperty parent,
        string propertyName,
        int size)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null && property.isArray)
            property.arraySize = size;
    }

    private static void AddToLookup<TKey, TValue>(
        Dictionary<TKey, List<TValue>> lookup,
        TKey key,
        TValue value)
    {
        if (!lookup.TryGetValue(key, out List<TValue> values))
        {
            values = new List<TValue>();
            lookup.Add(key, values);
        }
        values.Add(value);
    }

    private static List<T> LoadAssets<T>(string filter) where T : UnityEngine.Object
    {
        string[] guids = AssetDatabase.FindAssets(filter);
        List<string> paths = new List<string>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
            paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));

        paths.Sort(StringComparer.Ordinal);

        List<T> assets = new List<T>(paths.Count);
        for (int i = 0; i < paths.Count; i++)
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(paths[i]);
            if (asset != null)
                assets.Add(asset);
        }
        return assets;
    }

    private static bool IsLowerSnake(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains("_"))
            return false;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsLetter(c) && char.IsUpper(c))
                return false;
        }
        return true;
    }

    private static string ToSnakeToken(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "engraving";

        string result = string.Empty;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsLetterOrDigit(c))
            {
                if (i > 0 && char.IsUpper(c) && result.Length > 0 && result[result.Length - 1] != '_')
                    result += "_";
                result += char.ToLowerInvariant(c);
            }
            else if (result.Length > 0 && result[result.Length - 1] != '_')
            {
                result += "_";
            }
        }
        return string.IsNullOrEmpty(result) ? "engraving" : result.Trim('_');
    }

    private static string SanitizePascalToken(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "Engraving";

        string result = string.Empty;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsLetterOrDigit(c))
                result += c;
        }
        return string.IsNullOrEmpty(result) ? "Engraving" : result;
    }

    private enum ResultSeverity
    {
        Error,
        Warning,
        Info
    }

    private sealed class SkillRow
    {
        public SkillData Asset;
        public SerializedObject SerializedObject;
        public string DisplayName;
        public bool IsEngraving;
        public string ExecutionTypeText;
        public string CooldownText;
        public string DamageText;
        public string FormAndGrade;
        public int RecastCount;
        public int HitStepCount;
        public int CustomCellCount;
        public int AilmentCount;
        public string LinkedItemCodesText;
    }

    private sealed class ScanContext
    {
        public readonly List<ItemRecord> Items = new List<ItemRecord>(64);
        public readonly Dictionary<string, List<ItemRecord>> ItemsByCode =
            new Dictionary<string, List<ItemRecord>>(StringComparer.Ordinal);
        public readonly Dictionary<EngravingData, List<ItemRecord>> ItemsByEngraving =
            new Dictionary<EngravingData, List<ItemRecord>>();
        public readonly Dictionary<PassiveEngravingData, List<ItemRecord>> ItemsByPassiveEngraving =
            new Dictionary<PassiveEngravingData, List<ItemRecord>>();
        public readonly Dictionary<ItemDatabase, Dictionary<string, List<ItemRecord>>> CodesByDatabase =
            new Dictionary<ItemDatabase, Dictionary<string, List<ItemRecord>>>();
    }

    private sealed class ItemRecord
    {
        public ItemDatabase Database;
        public int Index;
        public string Code;
        public string DisplayName;
        public ItemType ItemType;
        public EngravingData Engraving;
        public PassiveEngravingData PassiveEngraving;
    }

    private sealed class DropRecord
    {
        public EnemyDropDatabase Database;
        public int GroupIndex;
        public string GroupLabel;
        public string Path;
        public string Code;
        public int MinAmount;
        public int MaxAmount;
    }

    private sealed class ValidationResult
    {
        public ResultSeverity Severity;
        public string Message;
        public UnityEngine.Object Target;
        public bool CanFix;
        public string FixLabel;
        public ItemDatabase FixDatabase;
        public UnityEngine.Object FixAsset;
        public ItemType FixItemType;
    }
}
