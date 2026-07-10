using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class ItemDashboardWindow : EditorWindow
{
    private const float FoldoutWidth = 20f;
    private const float WarningWidth = 28f;
    private const float IconColumnWidth = 34f;
    private const float IconSize = 20f;
    private const float CodeWidth = 160f;
    private const float NameWidth = 170f;
    private const float TypeWidth = 92f;
    private const float StackWidth = 68f;
    private const float ExpireWidth = 76f;
    private const float DescWidth = 52f;
    private const float TypeSummaryWidth = 300f;
    private const float DropSourceWidth = 420f;
    private const float WarningPanelHeight = 180f;
    private const string ShowInfoWarningsKey = "JBRogLike.ItemDashboard.ShowInfoWarnings";

    private static readonly string[] s_TypeFilterOptions = BuildTypeFilterOptions();

    private readonly List<ItemRow> _rows = new List<ItemRow>(64);
    private readonly List<DashboardWarning> _warnings = new List<DashboardWarning>(64);
    private readonly List<EnemyDropDatabase> _dropDatabases = new List<EnemyDropDatabase>(4);
    private readonly List<DropEntryRecord> _dropEntries = new List<DropEntryRecord>(64);
    private readonly List<EnemyOption> _enemyOptions = new List<EnemyOption>(32);
    private readonly HashSet<string> _itemCodes = new HashSet<string>(StringComparer.Ordinal);
    private string[] _enemyOptionLabels = Array.Empty<string>();
    private Vector2 _rowScrollPosition;
    private Vector2 _warningScrollPosition;
    private bool _hasScanned;
    private bool _hasAssetChanges;
    private bool _showInfoWarnings = true;
    private int _typeFilterIndex;
    private string _searchText = string.Empty;
    private string _lastScanLabel = "-";
    private EnemyDropDatabase _primaryDropDatabase;
    private string _operationFeedback = string.Empty;
    private MessageType _operationFeedbackType = MessageType.Info;

    [MenuItem("JBRogLike/Item Dashboard")]
    public static void Open()
    {
        GetWindow<ItemDashboardWindow>("Item Dashboard");
    }

    private void OnEnable()
    {
        minSize = new Vector2(1280f, 560f);
        _showInfoWarnings = EditorPrefs.GetBool(ShowInfoWarningsKey, true);
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (!_hasScanned)
        {
            EditorGUILayout.HelpBox("Click Scan to build the Item Dashboard.", MessageType.Info);
            return;
        }

        DrawSummary();
        DrawRowsPanel();
        DrawWarningsPanel();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            Scan();

        _typeFilterIndex = EditorGUILayout.Popup(_typeFilterIndex, s_TypeFilterOptions, GetToolbarPopupStyle(), GUILayout.Width(128f));
        _searchText = GUILayout.TextField(_searchText ?? string.Empty, GetToolbarSearchStyle(), GUILayout.Width(220f));

        EditorGUI.BeginChangeCheck();
        _showInfoWarnings = GUILayout.Toggle(_showInfoWarnings, "Info 경고", EditorStyles.toolbarButton, GUILayout.Width(86f));
        if (EditorGUI.EndChangeCheck())
            EditorPrefs.SetBool(ShowInfoWarningsKey, _showInfoWarnings);

        GUILayout.FlexibleSpace();
        GUILayout.Label("Last scan: " + _lastScanLabel, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSummary()
    {
        int visibleRows = GetVisibleRowCount();
        int visibleWarnings = GetVisibleWarningCount();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Items: " + visibleRows + "/" + _rows.Count, EditorStyles.boldLabel, GUILayout.Width(140f));
        EditorGUILayout.LabelField("Warnings: " + visibleWarnings + "/" + _warnings.Count, EditorStyles.boldLabel, GUILayout.Width(160f));
        if (_hasAssetChanges)
            EditorGUILayout.LabelField("값 변경됨 — Rescan 권장", EditorStyles.miniBoldLabel, GUILayout.Width(180f));
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrWhiteSpace(_operationFeedback))
            EditorGUILayout.HelpBox(_operationFeedback, _operationFeedbackType);
    }

    private void DrawRowsPanel()
    {
        if (_rows.Count == 0)
        {
            EditorGUILayout.HelpBox("No ItemDatabase entries found.", MessageType.Info);
            return;
        }

        _rowScrollPosition = EditorGUILayout.BeginScrollView(_rowScrollPosition, true, true, GUILayout.MinHeight(260f));
        DrawHeader();

        bool drewAny = false;
        for (int i = 0; i < _rows.Count; i++)
        {
            ItemRow row = _rows[i];
            if (!ShouldShowRow(row))
                continue;

            DrawRow(row);
            drewAny = true;
        }

        if (!drewAny)
            EditorGUILayout.HelpBox("No items match current filters.", MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        Header("", FoldoutWidth);
        Header("!", WarningWidth);
        Header("icon", IconColumnWidth);
        Header("itemCode", CodeWidth);
        Header("displayName", NameWidth);
        Header("Type", TypeWidth);
        Header("Stack", StackWidth);
        Header("소멸", ExpireWidth);
        Header("Desc", DescWidth);
        Header("타입요약", TypeSummaryWidth);
        Header("드랍소스", DropSourceWidth);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRow(ItemRow row)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        Rect foldoutRect = GUILayoutUtility.GetRect(FoldoutWidth, EditorGUIUtility.singleLineHeight, GUILayout.Width(FoldoutWidth));
        row.Foldout = EditorGUI.Foldout(foldoutRect, row.Foldout, GUIContent.none, true);

        if (row.GetVisibleWarningCount(_showInfoWarnings) > 0)
            GUILayout.Label(GetSeverityIcon(row.GetHighestSeverity(_showInfoWarnings), row.GetWarningTooltip(_showInfoWarnings)), GUILayout.Width(WarningWidth), GUILayout.Height(IconSize));
        else
            GUILayout.Label(GUIContent.none, GUILayout.Width(WarningWidth), GUILayout.Height(IconSize));

        DrawSpriteCell(row.Icon);

        if (GUILayout.Button(GetDisplayCode(row.ItemCode), GUILayout.Width(CodeWidth)))
            EditorGUIUtility.PingObject(row.Database);

        Cell(row.DisplayName, NameWidth);
        Cell(row.ItemType.ToString(), TypeWidth);
        Cell(row.StackText, StackWidth);
        Cell(row.ExpireText, ExpireWidth);
        Cell(row.HasDescription ? "✓" : "✗", DescWidth);
        Cell(row.TypeSummary, TypeSummaryWidth);
        DrawDropSourceCell(row);

        EditorGUILayout.EndHorizontal();

        if (row.Foldout)
            DrawRowFoldout(row);

        EditorGUILayout.EndVertical();
    }

    private void DrawDropSourceCell(ItemRow row)
    {
        if (row.DropSources.Count == 0)
        {
            Cell("-", DropSourceWidth);
            return;
        }

        if (GUILayout.Button(row.DropSummary, GUILayout.Width(DropSourceWidth)))
            EditorGUIUtility.PingObject(row.DropSources[0].Database);
    }

    private void DrawRowFoldout(ItemRow row)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(FoldoutWidth + WarningWidth);
        EditorGUILayout.BeginVertical();

        if (!TryGetRowItemProperty(row, out SerializedObject databaseObject, out SerializedProperty item))
        {
            EditorGUILayout.HelpBox("항목 위치 변경됨. Rescan 권장", MessageType.Warning);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            return;
        }

        DrawItemFieldsSection(row, databaseObject, item);
        DrawDropEntriesSection(row);

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawItemFieldsSection(ItemRow row, SerializedObject databaseObject, SerializedProperty item)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("아이템 필드", EditorStyles.boldLabel);

        DrawItemCodeField(row, databaseObject, item);
        DrawStringProperty(row, databaseObject, item, "displayName", "displayName");
        DrawSpriteProperty(row, databaseObject, item);
        DrawDescriptionProperty(row, databaseObject, item);
        DrawStackProperties(row, databaseObject, item);
        DrawExpireProperties(row, databaseObject, item);
        DrawTypeSpecificFields(row, databaseObject, item);

        EditorGUILayout.EndVertical();
    }

    private void DrawItemCodeField(ItemRow row, SerializedObject databaseObject, SerializedProperty item)
    {
        SerializedProperty property = item.FindPropertyRelative("itemCode");
        if (property == null)
        {
            EditorGUILayout.LabelField("itemCode", "<missing>");
            return;
        }

        string oldCode = property.stringValue;
        EditorGUI.BeginChangeCheck();
        string nextRaw = EditorGUILayout.DelayedTextField("itemCode", oldCode);
        if (!EditorGUI.EndChangeCheck())
            return;

        TryApplyItemCode(row, databaseObject, item, property, oldCode, nextRaw);
    }

    private void TryApplyItemCode(
        ItemRow row,
        SerializedObject databaseObject,
        SerializedProperty item,
        SerializedProperty property,
        string oldCode,
        string nextRaw)
    {
        string nextCode = (nextRaw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nextCode))
        {
            SetOperationFeedback("itemCode 공백은 적용할 수 없음.", MessageType.Error);
            return;
        }

        if (!string.Equals(oldCode, nextCode, StringComparison.Ordinal) && _itemCodes.Contains(nextCode))
        {
            SetOperationFeedback("itemCode 중복: '" + nextCode + "'", MessageType.Error);
            return;
        }

        if (string.Equals(oldCode, nextCode, StringComparison.Ordinal))
            return;

        int dropReferenceCount = CountDropReferences(oldCode);
        int salvageReferenceCount = CountSalvageReferences(row, oldCode);
        property.stringValue = nextCode;
        if (!ApplyAndMark(databaseObject))
            return;

        databaseObject.Update();
        RefreshRowFromProperty(row, GetItemProperty(databaseObject, row.Index));
        RebuildItemCodeSetFromRows();
        RebuildDropCachesForAllRows();

        if (dropReferenceCount > 0 || salvageReferenceCount > 0)
        {
            SetOperationFeedback(
                "옛 코드 '" + oldCode + "' 참조 잔존: 드랍DB " + dropReferenceCount +
                "건 / salvage " + salvageReferenceCount + "건 — Rescan으로 확인",
                MessageType.Warning);
        }
        else
        {
            ClearOperationFeedback();
        }
    }

    private void DrawStringProperty(ItemRow row, SerializedObject serializedObject, SerializedProperty item, string propertyName, string label)
    {
        SerializedProperty property = item.FindPropertyRelative(propertyName);
        if (property == null)
        {
            EditorGUILayout.LabelField(label, "<missing>");
            return;
        }

        EditorGUI.BeginChangeCheck();
        string next = EditorGUILayout.DelayedTextField(label, property.stringValue);
        if (!EditorGUI.EndChangeCheck())
            return;

        property.stringValue = next;
        ApplyItemPropertyChange(row, serializedObject);
    }

    private void DrawSpriteProperty(ItemRow row, SerializedObject serializedObject, SerializedProperty item)
    {
        SerializedProperty property = item.FindPropertyRelative("icon");
        if (property == null)
        {
            EditorGUILayout.LabelField("icon", "<missing>");
            return;
        }

        EditorGUI.BeginChangeCheck();
        Sprite next = (Sprite)EditorGUILayout.ObjectField("icon", property.objectReferenceValue, typeof(Sprite), false);
        if (!EditorGUI.EndChangeCheck())
            return;

        property.objectReferenceValue = next;
        ApplyItemPropertyChange(row, serializedObject);
    }

    private void DrawDescriptionProperty(ItemRow row, SerializedObject serializedObject, SerializedProperty item)
    {
        SerializedProperty property = item.FindPropertyRelative("description");
        if (property == null)
        {
            EditorGUILayout.LabelField("description", "<missing>");
            return;
        }

        EditorGUILayout.LabelField("description");
        EditorGUI.BeginChangeCheck();
        string next = EditorGUILayout.TextArea(property.stringValue, GUILayout.Height(EditorGUIUtility.singleLineHeight * 3f));
        if (!EditorGUI.EndChangeCheck())
            return;

        property.stringValue = next;
        ApplyItemPropertyChange(row, serializedObject);
    }

    private void DrawStackProperties(ItemRow row, SerializedObject serializedObject, SerializedProperty item)
    {
        SerializedProperty stackable = item.FindPropertyRelative("stackable");
        SerializedProperty maxStack = item.FindPropertyRelative("maxStack");

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        bool nextStackable = stackable != null && EditorGUILayout.Toggle("stackable", stackable.boolValue);
        int nextMaxStack = maxStack != null ? EditorGUILayout.DelayedIntField("maxStack", maxStack.intValue) : 1;
        bool changed = EditorGUI.EndChangeCheck();
        EditorGUILayout.EndHorizontal();

        if (!changed)
            return;

        if (stackable != null)
            stackable.boolValue = nextStackable;
        if (maxStack != null)
            maxStack.intValue = Mathf.Max(1, nextMaxStack);

        ApplyItemPropertyChange(row, serializedObject);
    }

    private void DrawExpireProperties(ItemRow row, SerializedObject serializedObject, SerializedProperty item)
    {
        SerializedProperty removeOnFloorTransition = item.FindPropertyRelative("removeOnFloorTransition");
        SerializedProperty removeOnDungeonExit = item.FindPropertyRelative("removeOnDungeonExit");

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        bool nextFloor = removeOnFloorTransition != null && EditorGUILayout.Toggle("removeOnFloorTransition", removeOnFloorTransition.boolValue);
        bool nextDungeon = removeOnDungeonExit != null && EditorGUILayout.Toggle("removeOnDungeonExit", removeOnDungeonExit.boolValue);
        bool changed = EditorGUI.EndChangeCheck();
        EditorGUILayout.EndHorizontal();

        if (!changed)
            return;

        if (removeOnFloorTransition != null)
            removeOnFloorTransition.boolValue = nextFloor;
        if (removeOnDungeonExit != null)
            removeOnDungeonExit.boolValue = nextDungeon;

        ApplyItemPropertyChange(row, serializedObject);
    }

    private void DrawTypeSpecificFields(ItemRow row, SerializedObject serializedObject, SerializedProperty item)
    {
        switch (row.ItemType)
        {
            case ItemType.Soul:
                DrawSoulFields(row, serializedObject, item);
                break;

            case ItemType.Engraving:
                DrawEngravingFields(row, serializedObject, item);
                break;

            case ItemType.Consumable:
                DrawEffectArray(row, serializedObject, item.FindPropertyRelative("useEffects"), "useEffects[]");
                break;

            case ItemType.Relic:
                DrawEffectArray(row, serializedObject, item.FindPropertyRelative("passiveEffects"), "passiveEffects[]");
                break;
        }
    }

    private void DrawSoulFields(ItemRow row, SerializedObject serializedObject, SerializedProperty item)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Soul", EditorStyles.miniBoldLabel);

        SerializedProperty soulFormId = item.FindPropertyRelative("soulFormId");
        if (soulFormId != null)
        {
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup("soulFormId", soulFormId.enumValueIndex, soulFormId.enumDisplayNames);
            if (EditorGUI.EndChangeCheck())
            {
                soulFormId.enumValueIndex = next;
                ApplyItemPropertyChange(row, serializedObject);
            }
        }

        SerializedProperty salvageItemCode = item.FindPropertyRelative("salvageItemCode");
        EditorGUILayout.BeginHorizontal();
        if (salvageItemCode != null)
        {
            EditorGUI.BeginChangeCheck();
            string next = EditorGUILayout.DelayedTextField("salvageItemCode", salvageItemCode.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                salvageItemCode.stringValue = next;
                ApplyItemPropertyChange(row, serializedObject);
            }

            string code = salvageItemCode.stringValue;
            GUIContent validationContent = string.IsNullOrWhiteSpace(code) || _itemCodes.Contains(code)
                ? GUIContent.none
                : GetSeverityIcon(WarningSeverity.Error, "itemCode 없음: " + code);
            GUILayout.Label(validationContent, GUILayout.Width(20f), GUILayout.Height(18f));
        }
        else
        {
            EditorGUILayout.LabelField("salvageItemCode", "<missing>");
            GUILayout.Label(GUIContent.none, GUILayout.Width(20f), GUILayout.Height(18f));
        }
        EditorGUILayout.EndHorizontal();

        SerializedProperty salvageMinAmount = item.FindPropertyRelative("salvageMinAmount");
        SerializedProperty salvageMaxAmount = item.FindPropertyRelative("salvageMaxAmount");
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        int min = salvageMinAmount != null ? EditorGUILayout.DelayedIntField("salvageMin", salvageMinAmount.intValue) : 1;
        int max = salvageMaxAmount != null ? EditorGUILayout.DelayedIntField("salvageMax", salvageMaxAmount.intValue) : min;
        bool changed = EditorGUI.EndChangeCheck();
        EditorGUILayout.EndHorizontal();

        if (!changed)
            return;

        min = Mathf.Max(1, min);
        max = Mathf.Max(min, max);
        if (salvageMinAmount != null)
            salvageMinAmount.intValue = min;
        if (salvageMaxAmount != null)
            salvageMaxAmount.intValue = max;

        ApplyItemPropertyChange(row, serializedObject);
    }

    private void DrawEngravingFields(ItemRow row, SerializedObject serializedObject, SerializedProperty item)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Engraving", EditorStyles.miniBoldLabel);

        SerializedProperty engraving = item.FindPropertyRelative("engraving");
        EditorGUILayout.BeginHorizontal();
        if (engraving != null)
        {
            EditorGUI.BeginChangeCheck();
            EngravingData next = (EngravingData)EditorGUILayout.ObjectField("engraving", engraving.objectReferenceValue, typeof(EngravingData), false);
            if (EditorGUI.EndChangeCheck())
            {
                engraving.objectReferenceValue = next;
                ApplyItemPropertyChange(row, serializedObject);
            }
        }
        else
        {
            EditorGUILayout.LabelField("engraving", "<missing>");
        }

        EditorGUI.BeginDisabledGroup(row.Engraving == null);
        if (GUILayout.Button("Ping", GUILayout.Width(48f)))
            EditorGUIUtility.PingObject(row.Engraving);
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("상세는 Engraving Validator", GUILayout.Width(170f)))
            EngravingValidatorWindow.Open();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawEffectArray(ItemRow row, SerializedObject serializedObject, SerializedProperty effects, string label)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

        if (effects == null || !effects.isArray)
        {
            EditorGUILayout.HelpBox(label + " missing.", MessageType.Error);
            return;
        }

        for (int i = 0; i < effects.arraySize; i++)
        {
            SerializedProperty effect = effects.GetArrayElementAtIndex(i);
            SerializedProperty type = effect.FindPropertyRelative("type");
            SerializedProperty value = effect.FindPropertyRelative("value");

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            int nextType = type != null ? EditorGUILayout.Popup(type.enumValueIndex, type.enumDisplayNames, GUILayout.Width(170f)) : 0;
            int nextValue = value != null ? EditorGUILayout.DelayedIntField(value.intValue, GUILayout.Width(80f)) : 0;
            bool changed = EditorGUI.EndChangeCheck();

            bool deleted = false;
            if (GUILayout.Button("-", GUILayout.Width(24f)))
            {
                DeleteArrayElement(effects, i);
                deleted = ApplyItemPropertyChange(row, serializedObject);
            }

            EditorGUILayout.EndHorizontal();

            if (deleted)
                GUIUtility.ExitGUI();

            if (!changed)
                continue;

            if (type != null)
                type.enumValueIndex = nextType;
            if (value != null)
                value.intValue = nextValue;

            ApplyItemPropertyChange(row, serializedObject);
        }

        bool added = false;
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16f);
        if (GUILayout.Button("+ effect", GUILayout.Width(90f)))
        {
            int index = effects.arraySize;
            effects.InsertArrayElementAtIndex(index);
            SerializedProperty effect = effects.GetArrayElementAtIndex(index);
            SetEnum(effect.FindPropertyRelative("type"), 0);
            SetInt(effect.FindPropertyRelative("value"), 0);
            added = ApplyItemPropertyChange(row, serializedObject);
        }
        EditorGUILayout.EndHorizontal();

        if (added)
            GUIUtility.ExitGUI();
    }

    private bool ApplyItemPropertyChange(ItemRow row, SerializedObject serializedObject)
    {
        if (!ApplyAndMark(serializedObject))
            return false;

        serializedObject.Update();
        RefreshRowFromProperty(row, GetItemProperty(serializedObject, row.Index));
        ClearOperationFeedback();
        return true;
    }

    private void DrawDropEntriesSection(ItemRow row)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("드랍 엔트리", EditorStyles.boldLabel);

        if (row.DropSources.Count == 0)
        {
            EditorGUILayout.LabelField("이 itemCode를 참조하는 드랍 엔트리 없음", EditorStyles.miniLabel);
        }
        else
        {
            for (int i = 0; i < row.DropSources.Count; i++)
                DrawDropEntryRow(row, row.DropSources[i]);
        }

        DrawAddDropEntry(row);
        EditorGUILayout.EndVertical();
    }

    private void DrawDropEntryRow(ItemRow row, DropSourceRecord source)
    {
        EditorGUILayout.BeginHorizontal();

        if (source.Enemy != null)
        {
            if (GUILayout.Button(source.EnemyName, GUILayout.Width(140f)))
                EditorGUIUtility.PingObject(source.Enemy);
        }
        else
        {
            GUILayout.Label(source.EnemyName, GUILayout.Width(140f));
        }

        GUILayout.Label(source.Kind == DropEntryKind.Drop ? "drop" : "택1", GUILayout.Width(42f));

        EditorGUI.BeginChangeCheck();
        int min = EditorGUILayout.DelayedIntField(source.MinAmount, GUILayout.Width(58f));
        int max = EditorGUILayout.DelayedIntField(source.MaxAmount, GUILayout.Width(58f));
        bool amountChanged = EditorGUI.EndChangeCheck();

        if (source.Kind == DropEntryKind.Drop)
        {
            EditorGUI.BeginChangeCheck();
            float chance = EditorGUILayout.DelayedFloatField(source.Chance, GUILayout.Width(76f));
            bool chanceChanged = EditorGUI.EndChangeCheck();
            if (amountChanged || chanceChanged)
                ApplyDropEntryValues(row, source, min, max, chance);
        }
        else
        {
            GUILayout.Label("그룹 " + FormatChance(source.GroupChance), GUILayout.Width(86f));
            EditorGUI.BeginChangeCheck();
            float weight = EditorGUILayout.DelayedFloatField(source.Weight, GUILayout.Width(76f));
            bool weightChanged = EditorGUI.EndChangeCheck();
            if (amountChanged || weightChanged)
                ApplyDropEntryValues(row, source, min, max, weight);
        }

        bool removed = GUILayout.Button("-", GUILayout.Width(24f)) && RemoveDropEntry(row, source);

        EditorGUILayout.EndHorizontal();

        if (removed)
            GUIUtility.ExitGUI();
    }

    private void ApplyDropEntryValues(ItemRow row, DropSourceRecord source, int min, int max, float chanceOrWeight)
    {
        if (!TryGetDropEntryProperty(source, row.ItemCode, out SerializedObject databaseObject, out SerializedProperty entry, out _))
        {
            SetOperationFeedback("드랍 엔트리 위치 변경됨. Rescan 권장.", MessageType.Warning);
            return;
        }

        min = Mathf.Max(1, min);
        max = Mathf.Max(min, max);
        SetInt(entry.FindPropertyRelative("minAmount"), min);
        SetInt(entry.FindPropertyRelative("maxAmount"), max);

        if (source.Kind == DropEntryKind.Drop)
            SetFloat(entry.FindPropertyRelative("chance"), Mathf.Clamp01(chanceOrWeight));
        else
            SetFloat(entry.FindPropertyRelative("weight"), Mathf.Max(0f, chanceOrWeight));

        if (!ApplyAndMark(databaseObject))
            return;

        RebuildDropCachesForAllRows();
        ClearOperationFeedback();
    }

    private bool RemoveDropEntry(ItemRow row, DropSourceRecord source)
    {
        if (!TryGetDropEntryProperty(source, row.ItemCode, out SerializedObject databaseObject, out _, out SerializedProperty parentArray))
        {
            SetOperationFeedback("드랍 엔트리 위치 변경됨. Rescan 권장.", MessageType.Warning);
            return false;
        }

        DeleteArrayElement(parentArray, source.EntryIndex);
        if (!ApplyAndMark(databaseObject))
            return false;

        RebuildDropCachesForAllRows();
        SetOperationFeedback("드랍 엔트리 제거 완료. Rescan 권장.", MessageType.Info);
        return true;
    }

    private void DrawAddDropEntry(ItemRow row)
    {
        EditorGUILayout.Space(4f);
        if (_primaryDropDatabase == null)
        {
            EditorGUILayout.HelpBox("EnemyDropDatabase asset not found.", MessageType.Warning);
            return;
        }

        if (_enemyOptions.Count == 0)
        {
            EditorGUILayout.HelpBox("EnemyData asset not found.", MessageType.Warning);
            return;
        }

        row.AddDropEnemyIndex = Mathf.Clamp(row.AddDropEnemyIndex, 0, _enemyOptions.Count - 1);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("추가 대상", GUILayout.Width(70f));
        row.AddDropEnemyIndex = EditorGUILayout.Popup(row.AddDropEnemyIndex, _enemyOptionLabels, GUILayout.Width(220f));

        EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(row.ItemCode));
        bool added = GUILayout.Button("+ 드랍 추가", GUILayout.Width(100f)) && AddDropEntry(row);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.LabelField("DB: " + _primaryDropDatabase.name, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        if (added)
            GUIUtility.ExitGUI();
    }

    private bool AddDropEntry(ItemRow row)
    {
        if (_primaryDropDatabase == null || row.AddDropEnemyIndex < 0 || row.AddDropEnemyIndex >= _enemyOptions.Count)
            return false;

        EnemyData enemy = _enemyOptions[row.AddDropEnemyIndex].Enemy;
        if (enemy == null)
            return false;

        SerializedObject databaseObject = new SerializedObject(_primaryDropDatabase);
        databaseObject.Update();

        SerializedProperty groups = databaseObject.FindProperty("groups");
        if (groups == null || !groups.isArray)
        {
            SetOperationFeedback("EnemyDropDatabase.groups를 찾을 수 없음.", MessageType.Error);
            return false;
        }

        SerializedProperty group = FindOrCreateDropGroup(groups, enemy);
        SerializedProperty drops = group != null ? group.FindPropertyRelative("drops") : null;
        if (drops == null || !drops.isArray)
        {
            SetOperationFeedback("EnemyDropDatabase drops[]를 찾을 수 없음.", MessageType.Error);
            return false;
        }

        int index = drops.arraySize;
        drops.InsertArrayElementAtIndex(index);
        SerializedProperty drop = drops.GetArrayElementAtIndex(index);
        InitializeDrop(drop, row.ItemCode);

        if (!ApplyAndMark(databaseObject))
            return false;

        RebuildDropCachesForAllRows();
        SetOperationFeedback("드랍 추가 완료: " + GetEnemyDisplayName(enemy) + " -> " + row.ItemCode, MessageType.Info);
        return true;
    }

    private SerializedProperty FindOrCreateDropGroup(SerializedProperty groups, EnemyData enemy)
    {
        for (int i = 0; i < groups.arraySize; i++)
        {
            SerializedProperty group = groups.GetArrayElementAtIndex(i);
            if (GetObject<EnemyData>(group.FindPropertyRelative("enemy")) == enemy)
                return group;
        }

        int index = groups.arraySize;
        groups.InsertArrayElementAtIndex(index);
        SerializedProperty newGroup = groups.GetArrayElementAtIndex(index);
        InitializeDropGroup(newGroup, enemy);
        return newGroup;
    }

    private bool TryGetDropEntryProperty(
        DropSourceRecord source,
        string expectedItemCode,
        out SerializedObject databaseObject,
        out SerializedProperty entry,
        out SerializedProperty parentArray)
    {
        databaseObject = null;
        entry = null;
        parentArray = null;

        if (source == null || source.Database == null)
            return false;

        databaseObject = new SerializedObject(source.Database);
        databaseObject.Update();

        SerializedProperty groups = databaseObject.FindProperty("groups");
        if (groups == null || !groups.isArray || source.GroupIndex < 0 || source.GroupIndex >= groups.arraySize)
            return false;

        SerializedProperty group = groups.GetArrayElementAtIndex(source.GroupIndex);
        if (GetObject<EnemyData>(group.FindPropertyRelative("enemy")) != source.Enemy)
            return false;

        if (source.Kind == DropEntryKind.Drop)
        {
            parentArray = group.FindPropertyRelative("drops");
            if (parentArray == null || !parentArray.isArray || source.EntryIndex < 0 || source.EntryIndex >= parentArray.arraySize)
                return false;

            entry = parentArray.GetArrayElementAtIndex(source.EntryIndex);
        }
        else
        {
            SerializedProperty choiceGroups = group.FindPropertyRelative("choiceGroups");
            if (choiceGroups == null || !choiceGroups.isArray || source.ChoiceGroupIndex < 0 || source.ChoiceGroupIndex >= choiceGroups.arraySize)
                return false;

            SerializedProperty choiceGroup = choiceGroups.GetArrayElementAtIndex(source.ChoiceGroupIndex);
            parentArray = choiceGroup.FindPropertyRelative("choices");
            if (parentArray == null || !parentArray.isArray || source.EntryIndex < 0 || source.EntryIndex >= parentArray.arraySize)
                return false;

            entry = parentArray.GetArrayElementAtIndex(source.EntryIndex);
        }

        return string.Equals(GetString(entry.FindPropertyRelative("itemCode")), expectedItemCode, StringComparison.Ordinal);
    }

    private void DrawWarningsPanel()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Warnings", EditorStyles.boldLabel);

        _warningScrollPosition = EditorGUILayout.BeginScrollView(_warningScrollPosition, false, true, GUILayout.MaxHeight(WarningPanelHeight));
        if (GetVisibleWarningCount() == 0)
        {
            EditorGUILayout.HelpBox("No warnings.", MessageType.Info);
        }
        else
        {
            for (int i = 0; i < _warnings.Count; i++)
            {
                DashboardWarning warning = _warnings[i];
                if (!ShouldShowWarning(warning))
                    continue;

                DrawWarning(warning);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawWarning(DashboardWarning warning)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label(GetSeverityIcon(warning.Severity, warning.Message), GUILayout.Width(24f), GUILayout.Height(20f));
        EditorGUILayout.LabelField(warning.Message, EditorStyles.wordWrappedLabel);
        if (warning.Target != null && GUILayout.Button("Ping", GUILayout.Width(48f)))
            EditorGUIUtility.PingObject(warning.Target);
        EditorGUILayout.EndHorizontal();
    }

    private void Scan()
    {
        _rows.Clear();
        _warnings.Clear();
        _dropDatabases.Clear();
        _dropEntries.Clear();
        _itemCodes.Clear();
        _hasScanned = true;
        _hasAssetChanges = false;
        _operationFeedback = string.Empty;
        _lastScanLabel = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        List<ItemDatabase> itemDatabases = LoadAssets<ItemDatabase>("t:ItemDatabase");
        List<EnemyDropDatabase> dropDatabases = LoadAssets<EnemyDropDatabase>("t:EnemyDropDatabase");
        List<EnemyData> enemies = LoadAssets<EnemyData>("t:EnemyData");

        _dropDatabases.AddRange(dropDatabases);
        _primaryDropDatabase = dropDatabases.Count > 0 ? dropDatabases[0] : null;
        BuildEnemyOptions(enemies);

        Dictionary<string, List<DropSourceRecord>> dropSourcesByCode = BuildDropSourceIndex(_dropDatabases, _dropEntries);

        HashSet<string> itemCodes = BuildItemCodeSet(itemDatabases);
        _itemCodes.UnionWith(itemCodes);

        Dictionary<string, List<ItemRow>> rowsByCode = new Dictionary<string, List<ItemRow>>(StringComparer.Ordinal);
        Dictionary<string, List<ItemRow>> soulRowsByForm = new Dictionary<string, List<ItemRow>>(StringComparer.Ordinal);
        BuildRows(itemDatabases, dropSourcesByCode, rowsByCode, soulRowsByForm);

        AddDuplicateItemCodeWarnings(rowsByCode);
        AddDuplicateSoulFormWarnings(soulRowsByForm);
        AddDeadDropWarnings(_dropEntries);
    }

    private void BuildEnemyOptions(List<EnemyData> enemies)
    {
        _enemyOptions.Clear();
        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyData enemy = enemies[i];
            if (enemy != null)
                _enemyOptions.Add(new EnemyOption(enemy, GetEnemyDisplayName(enemy)));
        }

        _enemyOptionLabels = new string[_enemyOptions.Count];
        for (int i = 0; i < _enemyOptions.Count; i++)
            _enemyOptionLabels[i] = _enemyOptions[i].Label;
    }

    private void BuildRows(
        List<ItemDatabase> itemDatabases,
        Dictionary<string, List<DropSourceRecord>> dropSourcesByCode,
        Dictionary<string, List<ItemRow>> rowsByCode,
        Dictionary<string, List<ItemRow>> soulRowsByForm)
    {
        for (int dbIndex = 0; dbIndex < itemDatabases.Count; dbIndex++)
        {
            ItemDatabase database = itemDatabases[dbIndex];
            if (database == null)
                continue;

            SerializedObject databaseObject = new SerializedObject(database);
            SerializedProperty items = databaseObject.FindProperty("items");
            if (items == null || !items.isArray)
                continue;

            for (int itemIndex = 0; itemIndex < items.arraySize; itemIndex++)
            {
                SerializedProperty item = items.GetArrayElementAtIndex(itemIndex);
                ItemRow row = CreateRow(database, itemIndex, item, dropSourcesByCode);
                AddRowWarnings(row);
                AddToCodeMaps(row, rowsByCode, soulRowsByForm);
                _rows.Add(row);
            }
        }
    }

    private ItemRow CreateRow(
        ItemDatabase database,
        int itemIndex,
        SerializedProperty item,
        Dictionary<string, List<DropSourceRecord>> dropSourcesByCode)
    {
        ItemRow row = new ItemRow(database, itemIndex);
        RefreshRowFromProperty(row, item);
        RefreshRowDropSources(row, dropSourcesByCode);
        return row;
    }

    private void RefreshRowFromProperty(ItemRow row, SerializedProperty item)
    {
        if (row == null || item == null)
            return;

        row.ItemCode = GetString(item.FindPropertyRelative("itemCode"));
        row.DisplayName = GetString(item.FindPropertyRelative("displayName"));
        row.Icon = GetObject<Sprite>(item.FindPropertyRelative("icon"));
        row.Description = GetString(item.FindPropertyRelative("description"));
        row.ItemType = GetItemType(item.FindPropertyRelative("itemType"));
        row.Stackable = GetBool(item.FindPropertyRelative("stackable"));
        row.MaxStack = GetInt(item.FindPropertyRelative("maxStack"));
        row.RemoveOnFloorTransition = GetBool(item.FindPropertyRelative("removeOnFloorTransition"));
        row.RemoveOnDungeonExit = GetBool(item.FindPropertyRelative("removeOnDungeonExit"));
        row.SoulFormId = GetEnumName(item.FindPropertyRelative("soulFormId"));
        row.Engraving = GetObject<EngravingData>(item.FindPropertyRelative("engraving"));
        row.SalvageItemCode = GetString(item.FindPropertyRelative("salvageItemCode"));
        row.SalvageMinAmount = GetInt(item.FindPropertyRelative("salvageMinAmount"));
        row.SalvageMaxAmount = GetInt(item.FindPropertyRelative("salvageMaxAmount"));
        row.TypeSummary = BuildTypeSummary(row, item);
    }

    private void RefreshRowDropSources(ItemRow row, Dictionary<string, List<DropSourceRecord>> dropSourcesByCode)
    {
        row.DropSources.Clear();
        if (!string.IsNullOrWhiteSpace(row.ItemCode) && dropSourcesByCode.TryGetValue(row.ItemCode, out List<DropSourceRecord> sources))
            row.DropSources.AddRange(sources);

        row.DropSummary = BuildDropSummary(row.DropSources);
    }

    private void RebuildDropCachesForAllRows()
    {
        _dropEntries.Clear();
        Dictionary<string, List<DropSourceRecord>> dropSourcesByCode = BuildDropSourceIndex(_dropDatabases, _dropEntries);
        for (int i = 0; i < _rows.Count; i++)
            RefreshRowDropSources(_rows[i], dropSourcesByCode);
    }

    private void RebuildItemCodeSetFromRows()
    {
        _itemCodes.Clear();
        for (int i = 0; i < _rows.Count; i++)
        {
            string code = _rows[i].ItemCode;
            if (!string.IsNullOrWhiteSpace(code))
                _itemCodes.Add(code);
        }
    }

    private void AddRowWarnings(ItemRow row)
    {
        string location = FormatItemLocation(row);

        if (string.IsNullOrWhiteSpace(row.ItemCode))
            AddWarning(row, WarningSeverity.Error, "[Error] itemCode 공백: " + location, row.Database);
        else if (row.ItemCode.Trim() != row.ItemCode)
            AddWarning(row, WarningSeverity.Warning, "[Warn] itemCode 앞뒤 공백: " + location + " '" + row.ItemCode + "'", row.Database);

        if (row.ItemType == ItemType.Engraving && row.Engraving == null)
            AddWarning(row, WarningSeverity.Error, "[Error] Engraving 타입인데 engraving 참조 null: " + location + " '" + GetDisplayCode(row.ItemCode) + "'", row.Database);

        if (!string.IsNullOrWhiteSpace(row.SalvageItemCode) && !_itemCodes.Contains(row.SalvageItemCode))
            AddWarning(row, WarningSeverity.Error, "[Error] salvageItemCode 죽은 코드: " + location + " -> '" + row.SalvageItemCode + "'", row.Database);

        if (!row.Stackable && row.MaxStack > 1)
            AddWarning(row, WarningSeverity.Warning, "[Warn] stackable=false인데 maxStack>1: " + location + " maxStack=" + row.MaxStack, row.Database);

        if (row.ItemType == ItemType.Equipment)
            AddWarning(row, WarningSeverity.Warning, "[Warn] Equipment 타입 사용: " + location + " '" + GetDisplayCode(row.ItemCode) + "'", row.Database);

        if (string.IsNullOrWhiteSpace(row.Description))
            AddWarning(row, WarningSeverity.Info, "[Info] description 공백: " + location + " '" + GetDisplayCode(row.ItemCode) + "'", row.Database);

        if (row.Icon == null)
            AddWarning(row, WarningSeverity.Info, "[Info] icon null: " + location + " '" + GetDisplayCode(row.ItemCode) + "'", row.Database);

        if (row.DropSources.Count == 0 && !ShouldSuppressUnregisteredDrop(row))
            AddWarning(row, WarningSeverity.Info, "[Info] 드랍 미등록: " + location + " '" + GetDisplayCode(row.ItemCode) + "'", row.Database);
    }

    private void AddToCodeMaps(
        ItemRow row,
        Dictionary<string, List<ItemRow>> rowsByCode,
        Dictionary<string, List<ItemRow>> soulRowsByForm)
    {
        if (!string.IsNullOrWhiteSpace(row.ItemCode))
            AddToLookup(rowsByCode, row.ItemCode, row);

        if (row.ItemType == ItemType.Soul && !string.IsNullOrWhiteSpace(row.SoulFormId))
            AddToLookup(soulRowsByForm, row.SoulFormId, row);
    }

    private void AddDuplicateItemCodeWarnings(Dictionary<string, List<ItemRow>> rowsByCode)
    {
        foreach (KeyValuePair<string, List<ItemRow>> pair in rowsByCode)
        {
            if (pair.Value.Count < 2)
                continue;

            string locations = FormatItemLocations(pair.Value);
            for (int i = 0; i < pair.Value.Count; i++)
            {
                ItemRow row = pair.Value[i];
                AddWarning(row, WarningSeverity.Error, "[Error] itemCode 중복: '" + pair.Key + "' -> " + locations, row.Database);
            }
        }
    }

    private void AddDuplicateSoulFormWarnings(Dictionary<string, List<ItemRow>> soulRowsByForm)
    {
        foreach (KeyValuePair<string, List<ItemRow>> pair in soulRowsByForm)
        {
            if (pair.Value.Count < 2)
                continue;

            string locations = FormatItemLocations(pair.Value);
            for (int i = 0; i < pair.Value.Count; i++)
            {
                ItemRow row = pair.Value[i];
                AddWarning(row, WarningSeverity.Error, "[Error] Soul 타입 soulFormId 중복: '" + pair.Key + "' -> " + locations, row.Database);
            }
        }
    }

    private void AddDeadDropWarnings(List<DropEntryRecord> dropEntries)
    {
        for (int i = 0; i < dropEntries.Count; i++)
        {
            DropEntryRecord drop = dropEntries[i];
            if (!string.IsNullOrWhiteSpace(drop.ItemCode) && _itemCodes.Contains(drop.ItemCode))
                continue;

            AddWarning(null, WarningSeverity.Error, "[Error] 드랍DB itemCode 죽은 코드: " + drop.Location + " -> '" + GetDisplayCode(drop.ItemCode) + "'", drop.Database);
        }
    }

    private void AddWarning(ItemRow row, WarningSeverity severity, string message, Object target)
    {
        DashboardWarning warning = new DashboardWarning(severity, message, target);
        if (row != null)
            row.Warnings.Add(warning);

        _warnings.Add(warning);
    }

    private bool ShouldShowRow(ItemRow row)
    {
        if (_typeFilterIndex > 0 && row.ItemType != (ItemType)(_typeFilterIndex - 1))
            return false;

        string search = _searchText ?? string.Empty;
        if (string.IsNullOrEmpty(search))
            return true;

        return Contains(row.ItemCode, search) || Contains(row.DisplayName, search);
    }

    private bool ShouldShowWarning(DashboardWarning warning)
    {
        return _showInfoWarnings || warning.Severity != WarningSeverity.Info;
    }

    private int GetVisibleRowCount()
    {
        int count = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (ShouldShowRow(_rows[i]))
                count++;
        }

        return count;
    }

    private int GetVisibleWarningCount()
    {
        int count = 0;
        for (int i = 0; i < _warnings.Count; i++)
        {
            if (ShouldShowWarning(_warnings[i]))
                count++;
        }

        return count;
    }

    private int CountDropReferences(string itemCode)
    {
        int count = 0;
        for (int i = 0; i < _dropEntries.Count; i++)
        {
            if (string.Equals(_dropEntries[i].ItemCode, itemCode, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private int CountSalvageReferences(ItemRow targetRow, string itemCode)
    {
        int count = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            ItemRow row = _rows[i];
            if (row == targetRow)
                continue;

            if (string.Equals(row.SalvageItemCode, itemCode, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private bool TryGetRowItemProperty(ItemRow row, out SerializedObject databaseObject, out SerializedProperty item)
    {
        databaseObject = null;
        item = null;
        if (row == null || row.Database == null)
            return false;

        databaseObject = new SerializedObject(row.Database);
        databaseObject.Update();

        item = GetItemProperty(databaseObject, row.Index);
        if (item == null)
            return false;

        string currentCode = GetString(item.FindPropertyRelative("itemCode"));
        return string.Equals(currentCode, row.ItemCode, StringComparison.Ordinal);
    }

    private static SerializedProperty GetItemProperty(SerializedObject databaseObject, int index)
    {
        if (databaseObject == null)
            return null;

        SerializedProperty items = databaseObject.FindProperty("items");
        if (items == null || !items.isArray || index < 0 || index >= items.arraySize)
            return null;

        return items.GetArrayElementAtIndex(index);
    }

    private bool ApplyAndMark(SerializedObject serializedObject)
    {
        bool applied = serializedObject.ApplyModifiedProperties();
        if (applied)
            _hasAssetChanges = true;
        return applied;
    }

    private void SetOperationFeedback(string message, MessageType type)
    {
        _operationFeedback = message;
        _operationFeedbackType = type;
    }

    private void ClearOperationFeedback()
    {
        _operationFeedback = string.Empty;
        _operationFeedbackType = MessageType.Info;
    }

    private static bool Contains(string source, string search)
    {
        return !string.IsNullOrEmpty(source) &&
               source.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ShouldSuppressUnregisteredDrop(ItemRow row)
    {
        return row.ItemType == ItemType.Key ||
               string.Equals(row.ItemCode, ItemCodes.RunCore, StringComparison.Ordinal);
    }

    private static string BuildTypeSummary(ItemRow row, SerializedProperty item)
    {
        switch (row.ItemType)
        {
            case ItemType.Soul:
                return row.SoulFormId + "→" + EmptyAsDash(row.SalvageItemCode) + " " + FormatAmount(row.SalvageMinAmount, row.SalvageMaxAmount);

            case ItemType.Relic:
                return BuildEffectSummary(item.FindPropertyRelative("passiveEffects"));

            case ItemType.Consumable:
                return BuildEffectSummary(item.FindPropertyRelative("useEffects"));

            case ItemType.Engraving:
                return row.Engraving != null ? BuildEngravingSummary(row.Engraving) : "참조 없음";

            default:
                return "-";
        }
    }

    private static string BuildEffectSummary(SerializedProperty effects)
    {
        if (effects == null || !effects.isArray || effects.arraySize == 0)
            return "-";

        Dictionary<ItemEffectType, int> totals = new Dictionary<ItemEffectType, int>();
        for (int i = 0; i < effects.arraySize; i++)
        {
            SerializedProperty effect = effects.GetArrayElementAtIndex(i);
            ItemEffectType type = GetItemEffectType(effect.FindPropertyRelative("type"));
            int value = GetInt(effect.FindPropertyRelative("value"));
            if (type == ItemEffectType.None || value == 0)
                continue;

            totals.TryGetValue(type, out int total);
            totals[type] = total + value;
        }

        if (totals.Count == 0)
            return "-";

        List<string> parts = new List<string>(totals.Count);
        AppendEffectPart(parts, totals, ItemEffectType.HealHp);
        AppendEffectPart(parts, totals, ItemEffectType.AttackBonus);
        AppendEffectPart(parts, totals, ItemEffectType.DefenseBonus);
        AppendEffectPart(parts, totals, ItemEffectType.MaxHpBonus);
        AppendEffectPart(parts, totals, ItemEffectType.MoveSpeedBonus);

        foreach (KeyValuePair<ItemEffectType, int> pair in totals)
        {
            if (IsKnownEffectType(pair.Key))
                continue;

            parts.Add(FormatEffect(pair.Key, pair.Value));
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "-";
    }

    private static void AppendEffectPart(List<string> parts, Dictionary<ItemEffectType, int> totals, ItemEffectType type)
    {
        if (!totals.TryGetValue(type, out int value))
            return;

        parts.Add(FormatEffect(type, value));
    }

    private static string FormatEffect(ItemEffectType type, int value)
    {
        string sign = value >= 0 ? "+" : string.Empty;
        string suffix = type == ItemEffectType.MoveSpeedBonus ? "%" : string.Empty;
        return GetEffectLabel(type) + sign + value + suffix;
    }

    private static string GetEffectLabel(ItemEffectType type)
    {
        switch (type)
        {
            case ItemEffectType.HealHp:
                return "Heal";
            case ItemEffectType.AttackBonus:
                return "Atk";
            case ItemEffectType.DefenseBonus:
                return "Def";
            case ItemEffectType.MaxHpBonus:
                return "HP";
            case ItemEffectType.MoveSpeedBonus:
                return "Move";
            default:
                return type.ToString();
        }
    }

    private static bool IsKnownEffectType(ItemEffectType type)
    {
        return type == ItemEffectType.HealHp ||
               type == ItemEffectType.AttackBonus ||
               type == ItemEffectType.DefenseBonus ||
               type == ItemEffectType.MaxHpBonus ||
               type == ItemEffectType.MoveSpeedBonus;
    }

    private static string BuildEngravingSummary(EngravingData engraving)
    {
        return "[" + engraving.grade + "] " + engraving.owningForm;
    }

    private static Dictionary<string, List<DropSourceRecord>> BuildDropSourceIndex(
        List<EnemyDropDatabase> databases,
        List<DropEntryRecord> dropEntries)
    {
        Dictionary<string, List<DropSourceRecord>> sourcesByCode = new Dictionary<string, List<DropSourceRecord>>(StringComparer.Ordinal);

        for (int dbIndex = 0; dbIndex < databases.Count; dbIndex++)
        {
            EnemyDropDatabase database = databases[dbIndex];
            if (database == null)
                continue;

            SerializedObject databaseObject = new SerializedObject(database);
            SerializedProperty groups = databaseObject.FindProperty("groups");
            if (groups == null || !groups.isArray)
                continue;

            for (int groupIndex = 0; groupIndex < groups.arraySize; groupIndex++)
            {
                SerializedProperty group = groups.GetArrayElementAtIndex(groupIndex);
                EnemyData enemy = GetObject<EnemyData>(group.FindPropertyRelative("enemy"));
                string enemyName = GetGroupEnemyName(group, groupIndex);
                ReadDropEntries(database, groupIndex, enemy, enemyName, group.FindPropertyRelative("drops"), sourcesByCode, dropEntries);
                ReadChoiceEntries(database, groupIndex, enemy, enemyName, group.FindPropertyRelative("choiceGroups"), sourcesByCode, dropEntries);
            }
        }

        return sourcesByCode;
    }

    private static void ReadDropEntries(
        EnemyDropDatabase database,
        int groupIndex,
        EnemyData enemy,
        string enemyName,
        SerializedProperty drops,
        Dictionary<string, List<DropSourceRecord>> sourcesByCode,
        List<DropEntryRecord> dropEntries)
    {
        if (drops == null || !drops.isArray)
            return;

        for (int dropIndex = 0; dropIndex < drops.arraySize; dropIndex++)
        {
            SerializedProperty drop = drops.GetArrayElementAtIndex(dropIndex);
            string itemCode = GetString(drop.FindPropertyRelative("itemCode"));
            int minAmount = GetInt(drop.FindPropertyRelative("minAmount"));
            int maxAmount = GetInt(drop.FindPropertyRelative("maxAmount"));
            float chance = GetFloat(drop.FindPropertyRelative("chance"));
            string summary = enemyName + " " + FormatChance(chance);
            string location = "EnemyDropDatabase '" + database.name + "' group " + groupIndex + " drop " + dropIndex;

            DropSourceRecord source = new DropSourceRecord(
                database,
                itemCode,
                summary,
                enemy,
                enemyName,
                DropEntryKind.Drop,
                groupIndex,
                -1,
                dropIndex,
                minAmount,
                maxAmount,
                chance,
                0f,
                0f);
            AddToLookup(sourcesByCode, itemCode, source);
            dropEntries.Add(new DropEntryRecord(database, itemCode, location));
        }
    }

    private static void ReadChoiceEntries(
        EnemyDropDatabase database,
        int groupIndex,
        EnemyData enemy,
        string enemyName,
        SerializedProperty choiceGroups,
        Dictionary<string, List<DropSourceRecord>> sourcesByCode,
        List<DropEntryRecord> dropEntries)
    {
        if (choiceGroups == null || !choiceGroups.isArray)
            return;

        for (int choiceGroupIndex = 0; choiceGroupIndex < choiceGroups.arraySize; choiceGroupIndex++)
        {
            SerializedProperty choiceGroup = choiceGroups.GetArrayElementAtIndex(choiceGroupIndex);
            float chance = GetFloat(choiceGroup.FindPropertyRelative("chance"));
            SerializedProperty choices = choiceGroup.FindPropertyRelative("choices");
            if (choices == null || !choices.isArray)
                continue;

            for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
            {
                SerializedProperty choice = choices.GetArrayElementAtIndex(choiceIndex);
                string itemCode = GetString(choice.FindPropertyRelative("itemCode"));
                int minAmount = GetInt(choice.FindPropertyRelative("minAmount"));
                int maxAmount = GetInt(choice.FindPropertyRelative("maxAmount"));
                float weight = GetFloat(choice.FindPropertyRelative("weight"));
                string summary = enemyName + " 택1 " + FormatChance(chance);
                string location = "EnemyDropDatabase '" + database.name + "' group " + groupIndex +
                                  " choice group " + choiceGroupIndex + " choice " + choiceIndex;

                DropSourceRecord source = new DropSourceRecord(
                    database,
                    itemCode,
                    summary,
                    enemy,
                    enemyName,
                    DropEntryKind.Choice,
                    groupIndex,
                    choiceGroupIndex,
                    choiceIndex,
                    minAmount,
                    maxAmount,
                    0f,
                    chance,
                    weight);
                AddToLookup(sourcesByCode, itemCode, source);
                dropEntries.Add(new DropEntryRecord(database, itemCode, location));
            }
        }
    }

    private static string GetGroupEnemyName(SerializedProperty group, int groupIndex)
    {
        EnemyData enemy = GetObject<EnemyData>(group.FindPropertyRelative("enemy"));
        if (enemy == null)
            return "group " + groupIndex;

        return GetEnemyDisplayName(enemy);
    }

    private static string GetEnemyDisplayName(EnemyData enemy)
    {
        return enemy != null && !string.IsNullOrWhiteSpace(enemy.enemyName) ? enemy.enemyName : enemy != null ? enemy.name : "<null>";
    }

    private static HashSet<string> BuildItemCodeSet(List<ItemDatabase> databases)
    {
        HashSet<string> codes = new HashSet<string>(StringComparer.Ordinal);

        for (int dbIndex = 0; dbIndex < databases.Count; dbIndex++)
        {
            ItemDatabase database = databases[dbIndex];
            if (database == null)
                continue;

            SerializedObject databaseObject = new SerializedObject(database);
            SerializedProperty items = databaseObject.FindProperty("items");
            if (items == null || !items.isArray)
                continue;

            for (int itemIndex = 0; itemIndex < items.arraySize; itemIndex++)
            {
                SerializedProperty item = items.GetArrayElementAtIndex(itemIndex);
                string code = GetString(item.FindPropertyRelative("itemCode"));
                if (!string.IsNullOrWhiteSpace(code))
                    codes.Add(code);
            }
        }

        return codes;
    }

    private static string BuildDropSummary(List<DropSourceRecord> sources)
    {
        List<string> parts = new List<string>(sources.Count);
        for (int i = 0; i < sources.Count; i++)
            parts.Add(sources[i].Summary);

        return parts.Count > 0 ? string.Join("; ", parts) : "-";
    }

    private static List<T> LoadAssets<T>(string filter) where T : Object
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

    private static void AddToLookup<TKey, TValue>(Dictionary<TKey, List<TValue>> lookup, TKey key, TValue value)
    {
        if (!lookup.TryGetValue(key, out List<TValue> values))
        {
            values = new List<TValue>();
            lookup.Add(key, values);
        }

        values.Add(value);
    }

    private static void DrawSpriteCell(Sprite sprite)
    {
        Rect rect = GUILayoutUtility.GetRect(IconColumnWidth, IconSize, GUILayout.Width(IconColumnWidth), GUILayout.Height(IconSize));
        if (sprite == null || sprite.texture == null || (sprite.packed && sprite.packingMode == SpritePackingMode.Tight))
        {
            GUI.Label(rect, "-", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        Rect drawRect = new Rect(rect.x + (IconColumnWidth - IconSize) * 0.5f, rect.y, IconSize, IconSize);
        Texture texture = sprite.texture;
        Rect textureRect = sprite.textureRect;
        Rect textureCoords = new Rect(
            textureRect.x / texture.width,
            textureRect.y / texture.height,
            textureRect.width / texture.width,
            textureRect.height / texture.height);

        GUI.DrawTextureWithTexCoords(drawRect, texture, textureCoords, true);
    }

    private static GUIContent GetSeverityIcon(WarningSeverity severity, string tooltip)
    {
        GUIContent content;
        switch (severity)
        {
            case WarningSeverity.Error:
                content = EditorGUIUtility.IconContent("console.erroricon");
                break;
            case WarningSeverity.Warning:
                content = EditorGUIUtility.IconContent("console.warnicon");
                break;
            default:
                content = EditorGUIUtility.IconContent("console.infoicon");
                break;
        }

        return new GUIContent(content.image, tooltip);
    }

    private static void Header(string text, float width)
    {
        GUILayout.Label(text, EditorStyles.boldLabel, GUILayout.Width(width));
    }

    private static void Cell(string text, float width)
    {
        GUILayout.Label(text, GUILayout.Width(width));
    }

    private static GUIStyle GetToolbarSearchStyle()
    {
        GUIStyle style = GUI.skin.FindStyle("ToolbarSeachTextField");
        if (style == null)
            style = GUI.skin.FindStyle("ToolbarSearchTextField");

        return style ?? EditorStyles.textField;
    }

    private static GUIStyle GetToolbarPopupStyle()
    {
        GUIStyle style = GUI.skin.FindStyle("ToolbarPopup");
        return style ?? EditorStyles.popup;
    }

    private static string[] BuildTypeFilterOptions()
    {
        string[] names = Enum.GetNames(typeof(ItemType));
        string[] options = new string[names.Length + 1];
        options[0] = "All";
        for (int i = 0; i < names.Length; i++)
            options[i + 1] = names[i];

        return options;
    }

    private static string FormatItemLocation(ItemRow row)
    {
        return row.Database.name + "[" + row.Index + "]";
    }

    private static string FormatItemLocations(List<ItemRow> rows)
    {
        string result = string.Empty;
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0)
                result += ", ";

            result += FormatItemLocation(rows[i]) + " '" + GetDisplayCode(rows[i].ItemCode) + "'";
        }

        return result;
    }

    private static string FormatAmount(int minAmount, int maxAmount)
    {
        return minAmount == maxAmount
            ? "x" + minAmount
            : "x" + minAmount + "~" + maxAmount;
    }

    private static string FormatChance(float chance)
    {
        float percent = chance * 100f;
        float rounded = Mathf.Round(percent);
        if (Mathf.Abs(percent - rounded) < 0.05f)
            return ((int)rounded).ToString(CultureInfo.InvariantCulture) + "%";

        return percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static string GetDisplayCode(string code)
    {
        return string.IsNullOrWhiteSpace(code) ? "<empty>" : code;
    }

    private static string EmptyAsDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static void InitializeDropGroup(SerializedProperty group, EnemyData enemy)
    {
        if (group == null)
            return;

        SerializedProperty enemyProperty = group.FindPropertyRelative("enemy");
        SerializedProperty drops = group.FindPropertyRelative("drops");
        SerializedProperty choiceGroups = group.FindPropertyRelative("choiceGroups");

        if (enemyProperty != null)
            enemyProperty.objectReferenceValue = enemy;
        if (drops != null && drops.isArray)
            drops.arraySize = 0;
        if (choiceGroups != null && choiceGroups.isArray)
            choiceGroups.arraySize = 0;
    }

    private static void InitializeDrop(SerializedProperty drop, string itemCode)
    {
        if (drop == null)
            return;

        SetString(drop.FindPropertyRelative("itemCode"), itemCode);
        SetInt(drop.FindPropertyRelative("minAmount"), 1);
        SetInt(drop.FindPropertyRelative("maxAmount"), 1);
        SetFloat(drop.FindPropertyRelative("chance"), 1f);
    }

    private static void DeleteArrayElement(SerializedProperty array, int index)
    {
        int previousSize = array.arraySize;
        array.DeleteArrayElementAtIndex(index);
        if (array.arraySize == previousSize)
            array.DeleteArrayElementAtIndex(index);
    }

    private static T GetObject<T>(SerializedProperty property) where T : Object
    {
        return property != null ? property.objectReferenceValue as T : null;
    }

    private static string GetString(SerializedProperty property)
    {
        return property != null ? property.stringValue : string.Empty;
    }

    private static int GetInt(SerializedProperty property)
    {
        return property != null ? property.intValue : 0;
    }

    private static float GetFloat(SerializedProperty property)
    {
        return property != null ? property.floatValue : 0f;
    }

    private static bool GetBool(SerializedProperty property)
    {
        return property != null && property.boolValue;
    }

    private static ItemType GetItemType(SerializedProperty property)
    {
        return property != null ? (ItemType)property.enumValueIndex : ItemType.Key;
    }

    private static ItemEffectType GetItemEffectType(SerializedProperty property)
    {
        return property != null ? (ItemEffectType)property.enumValueIndex : ItemEffectType.None;
    }

    private static string GetEnumName(SerializedProperty property)
    {
        if (property == null || property.propertyType != SerializedPropertyType.Enum)
            return string.Empty;

        int index = property.enumValueIndex;
        return index >= 0 && index < property.enumNames.Length ? property.enumNames[index] : index.ToString(CultureInfo.InvariantCulture);
    }

    private static void SetString(SerializedProperty property, string value)
    {
        if (property != null)
            property.stringValue = value;
    }

    private static void SetInt(SerializedProperty property, int value)
    {
        if (property != null)
            property.intValue = value;
    }

    private static void SetFloat(SerializedProperty property, float value)
    {
        if (property != null)
            property.floatValue = value;
    }

    private static void SetEnum(SerializedProperty property, int value)
    {
        if (property != null)
            property.enumValueIndex = value;
    }

    private enum WarningSeverity
    {
        Error,
        Warning,
        Info
    }

    private enum DropEntryKind
    {
        Drop,
        Choice
    }

    private sealed class ItemRow
    {
        public readonly ItemDatabase Database;
        public readonly int Index;
        public readonly List<DashboardWarning> Warnings = new List<DashboardWarning>(4);
        public readonly List<DropSourceRecord> DropSources = new List<DropSourceRecord>(2);
        public bool Foldout;
        public int AddDropEnemyIndex;
        public string ItemCode;
        public string DisplayName;
        public Sprite Icon;
        public string Description;
        public ItemType ItemType;
        public bool Stackable;
        public int MaxStack;
        public bool RemoveOnFloorTransition;
        public bool RemoveOnDungeonExit;
        public string SoulFormId;
        public EngravingData Engraving;
        public string SalvageItemCode;
        public int SalvageMinAmount;
        public int SalvageMaxAmount;
        public string TypeSummary;
        public string DropSummary;

        public ItemRow(ItemDatabase database, int index)
        {
            Database = database;
            Index = index;
        }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public string StackText => Stackable ? "x" + MaxStack : "-";

        public string ExpireText
        {
            get
            {
                if (RemoveOnFloorTransition && RemoveOnDungeonExit)
                    return "층+던전";

                if (RemoveOnFloorTransition)
                    return "층";

                if (RemoveOnDungeonExit)
                    return "던전";

                return "-";
            }
        }

        public int GetVisibleWarningCount(bool showInfo)
        {
            int count = 0;
            for (int i = 0; i < Warnings.Count; i++)
            {
                if (showInfo || Warnings[i].Severity != WarningSeverity.Info)
                    count++;
            }

            return count;
        }

        public WarningSeverity GetHighestSeverity(bool showInfo)
        {
            WarningSeverity highest = WarningSeverity.Info;
            for (int i = 0; i < Warnings.Count; i++)
            {
                DashboardWarning warning = Warnings[i];
                if (!showInfo && warning.Severity == WarningSeverity.Info)
                    continue;

                if (warning.Severity < highest)
                    highest = warning.Severity;
            }

            return highest;
        }

        public string GetWarningTooltip(bool showInfo)
        {
            List<string> messages = new List<string>(Warnings.Count);
            for (int i = 0; i < Warnings.Count; i++)
            {
                DashboardWarning warning = Warnings[i];
                if (!showInfo && warning.Severity == WarningSeverity.Info)
                    continue;

                messages.Add(warning.Message);
            }

            return string.Join("\n", messages);
        }
    }

    private sealed class DropSourceRecord
    {
        public readonly EnemyDropDatabase Database;
        public readonly string ItemCode;
        public readonly string Summary;
        public readonly EnemyData Enemy;
        public readonly string EnemyName;
        public readonly DropEntryKind Kind;
        public readonly int GroupIndex;
        public readonly int ChoiceGroupIndex;
        public readonly int EntryIndex;
        public readonly int MinAmount;
        public readonly int MaxAmount;
        public readonly float Chance;
        public readonly float GroupChance;
        public readonly float Weight;

        public DropSourceRecord(
            EnemyDropDatabase database,
            string itemCode,
            string summary,
            EnemyData enemy,
            string enemyName,
            DropEntryKind kind,
            int groupIndex,
            int choiceGroupIndex,
            int entryIndex,
            int minAmount,
            int maxAmount,
            float chance,
            float groupChance,
            float weight)
        {
            Database = database;
            ItemCode = itemCode;
            Summary = summary;
            Enemy = enemy;
            EnemyName = enemyName;
            Kind = kind;
            GroupIndex = groupIndex;
            ChoiceGroupIndex = choiceGroupIndex;
            EntryIndex = entryIndex;
            MinAmount = minAmount;
            MaxAmount = maxAmount;
            Chance = chance;
            GroupChance = groupChance;
            Weight = weight;
        }
    }

    private sealed class DropEntryRecord
    {
        public readonly EnemyDropDatabase Database;
        public readonly string ItemCode;
        public readonly string Location;

        public DropEntryRecord(EnemyDropDatabase database, string itemCode, string location)
        {
            Database = database;
            ItemCode = itemCode;
            Location = location;
        }
    }

    private sealed class EnemyOption
    {
        public readonly EnemyData Enemy;
        public readonly string Label;

        public EnemyOption(EnemyData enemy, string label)
        {
            Enemy = enemy;
            Label = label;
        }
    }

    private sealed class DashboardWarning
    {
        public readonly WarningSeverity Severity;
        public readonly string Message;
        public readonly Object Target;

        public DashboardWarning(WarningSeverity severity, string message, Object target)
        {
            Severity = severity;
            Message = message;
            Target = target;
        }
    }
}
