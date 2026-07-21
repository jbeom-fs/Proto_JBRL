using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
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
    private const float DeleteWidth = 52f;
    private const float WarningPanelHeight = 180f;
    private const float DropEnemyWidth = 140f;
    private const float DropKindWidth = 42f;
    private const float DropAmountWidth = 58f;
    private const float DropGroupChanceWidth = 86f;
    private const float DropChanceWidth = 76f;
    private const float DropRemoveWidth = 24f;
    private const string ShowInfoWarningsKey = "JBRogLike.ItemDashboard.ShowInfoWarnings";
    private const string WarningsPanelHeightKey = "JBRogLike.ItemDashboard.WarningsPanelHeight";
    private const float MinWarningsPanelHeight = 60f;
    private const float WarningsPanelMaxPadding = 220f;

    private static readonly string[] s_TypeFilterOptions = BuildTypeFilterOptions();
    private static readonly string[] s_SkillExecutionTypeNames = Enum.GetNames(typeof(SkillExecutionType));

    private readonly List<ItemRow> _rows = new List<ItemRow>(64);
    private readonly List<DashboardWarning> _warnings = new List<DashboardWarning>(64);
    private readonly List<ItemDatabase> _itemDatabases = new List<ItemDatabase>(4);
    private readonly List<EnemyDropDatabase> _dropDatabases = new List<EnemyDropDatabase>(4);
    private readonly List<DropEntryRecord> _dropEntries = new List<DropEntryRecord>(64);
    private readonly List<EnemyOption> _enemyOptions = new List<EnemyOption>(32);
    private readonly HashSet<string> _itemCodes = new HashSet<string>(StringComparer.Ordinal);
    private string[] _enemyOptionLabels = Array.Empty<string>();
    private Vector2 _rowScrollPosition;
    private Vector2 _warningScrollPosition;
    private float _warningsPanelHeight = WarningPanelHeight;
    private bool _hasScanned;
    private bool _hasAssetChanges;
    private bool _isScopedSaveQueued;
    private bool _showNewItemForm;
    private bool _showInfoWarnings = true;
    private int _typeFilterIndex;
    private string _searchText = string.Empty;
    private string _lastScanLabel = "-";
    private ItemDatabase _primaryItemDatabase;
    private EnemyDropDatabase _primaryDropDatabase;
    private string _operationFeedback = string.Empty;
    private MessageType _operationFeedbackType = MessageType.Info;
    private string _newItemCode = string.Empty;
    private string _newItemDisplayName = string.Empty;
    private ItemType _newItemType = ItemType.Material;
    private bool _newItemCreateDropStub;
    private int _newItemDropEnemyIndex;
    private string _newItemFeedback = string.Empty;
    private MessageType _newItemFeedbackType = MessageType.Info;

    [MenuItem("JBRogLike/Item Dashboard")]
    public static void Open()
    {
        GetWindow<ItemDashboardWindow>("Item Dashboard");
    }

    private void OnEnable()
    {
        minSize = new Vector2(1280f, 560f);
        _showInfoWarnings = EditorPrefs.GetBool(ShowInfoWarningsKey, true);
        _warningsPanelHeight = EditorPrefs.GetFloat(WarningsPanelHeightKey, WarningPanelHeight);
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        EditorApplication.delayCall -= SaveScopedAssetsAfterFocusFlush;
        _isScopedSaveQueued = false;
    }

    private void OnUndoRedoPerformed()
    {
        if (!_hasScanned)
            return;

        // Undo 대상 판별보다 전체 재스캔이 싸서 필터링하지 않음.
        Scan();
        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();
        ClampWarningsPanelHeight();

        if (_showNewItemForm)
            DrawNewItemPanel();

        if (!_hasScanned)
        {
            EditorGUILayout.HelpBox("Click Scan to build the Item Dashboard.", MessageType.Info);
            return;
        }

        DrawSummary();
        DrawRowsPanel();
        DrawPanelSplitter();
        DrawWarningsPanel();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            Scan();
        _showNewItemForm = GUILayout.Toggle(_showNewItemForm, "New Item", EditorStyles.toolbarButton, GUILayout.Width(90f));

        _typeFilterIndex = EditorGUILayout.Popup(_typeFilterIndex, s_TypeFilterOptions, GetToolbarPopupStyle(), GUILayout.Width(128f));
        _searchText = GUILayout.TextField(_searchText ?? string.Empty, GetToolbarSearchStyle(), GUILayout.Width(220f));

        EditorGUI.BeginChangeCheck();
        _showInfoWarnings = GUILayout.Toggle(_showInfoWarnings, "Info 경고", EditorStyles.toolbarButton, GUILayout.Width(86f));
        if (EditorGUI.EndChangeCheck())
            EditorPrefs.SetBool(ShowInfoWarningsKey, _showInfoWarnings);

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save Assets", EditorStyles.toolbarButton, GUILayout.Width(96f)))
            SaveScopedAssets();
        GUILayout.Label("Last scan: " + _lastScanLabel, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // SerializedObject/Undo paths stay separate. Global SaveAssets is forbidden; save only dirty scanned databases.
    private void SaveScopedAssets()
    {
        if (_isScopedSaveQueued)
            return;

        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;
        _isScopedSaveQueued = true;
        EditorApplication.delayCall += SaveScopedAssetsAfterFocusFlush;
        Repaint();
    }

    private void SaveScopedAssetsAfterFocusFlush()
    {
        _isScopedSaveQueued = false;
        SaveScopedAssetsNow();
    }

    private void SaveScopedAssetsNow()
    {
        if (!_hasScanned)
        {
            Debug.LogWarning("[ItemDashboardWindow] Scan first; no scoped assets were saved.");
            return;
        }

        var assets = new HashSet<Object>();
        AddMainAssets(assets, _itemDatabases);
        AddMainAssets(assets, _dropDatabases);
        AddMainAsset(assets, _primaryItemDatabase);
        AddMainAsset(assets, _primaryDropDatabase);

        int savedCount = SaveDirtyAssets(assets, out int dirtyCount);
        if (savedCount == dirtyCount)
            _hasAssetChanges = false;
        Debug.Log(
            "[ItemDashboardWindow] Scoped save: assets=" + assets.Count +
            ", dirty=" + dirtyCount + ", saved=" + savedCount + ".");
        Repaint();
    }

    private static void AddMainAssets<T>(HashSet<Object> assets, List<T> source) where T : Object
    {
        for (int i = 0; i < source.Count; i++)
            AddMainAsset(assets, source[i]);
    }

    private static void AddMainAsset(HashSet<Object> assets, Object asset)
    {
        if (asset == null)
            return;

        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path))
            return;

        Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
        if (mainAsset != null)
            assets.Add(mainAsset);
    }

    private static int SaveDirtyAssets(HashSet<Object> assets, out int dirtyCount)
    {
        dirtyCount = 0;
        int savedCount = 0;
        foreach (Object asset in assets)
        {
            if (asset == null || !EditorUtility.IsDirty(asset))
                continue;

            dirtyCount++;
            AssetDatabase.SaveAssetIfDirty(asset);
            if (!EditorUtility.IsDirty(asset))
                savedCount++;
        }

        return savedCount;
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

    private void DrawNewItemPanel()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("New Item", EditorStyles.boldLabel);

        string targetDatabaseLabel = _primaryItemDatabase != null ? _primaryItemDatabase.name : "ItemDatabase 없음";
        EditorGUILayout.LabelField("대상 DB", targetDatabaseLabel);

        EditorGUI.BeginChangeCheck();
        _newItemCode = EditorGUILayout.TextField("itemCode", _newItemCode);
        _newItemDisplayName = EditorGUILayout.TextField("displayName", _newItemDisplayName);
        _newItemType = (ItemType)EditorGUILayout.EnumPopup("itemType", _newItemType);
        _newItemCreateDropStub = EditorGUILayout.Toggle("드랍 스텁 등록", _newItemCreateDropStub);

        if (_newItemCreateDropStub)
        {
            if (_enemyOptions.Count > 0)
            {
                _newItemDropEnemyIndex = Mathf.Clamp(_newItemDropEnemyIndex, 0, _enemyOptions.Count - 1);
                _newItemDropEnemyIndex = EditorGUILayout.Popup("EnemyData", _newItemDropEnemyIndex, _enemyOptionLabels);
            }
            else
            {
                EditorGUILayout.LabelField("EnemyData", "스캔된 EnemyData 없음");
            }
        }

        if (EditorGUI.EndChangeCheck())
            _newItemFeedback = string.Empty;

        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        CollectNewItemValidation(errors, warnings, out string itemCode);
        DrawValidationMessages(errors, MessageType.Error);
        DrawValidationMessages(warnings, MessageType.Warning);

        if (!string.IsNullOrWhiteSpace(_newItemFeedback))
            EditorGUILayout.HelpBox(_newItemFeedback, _newItemFeedbackType);

        EditorGUI.BeginDisabledGroup(errors.Count > 0);
        if (GUILayout.Button("생성", GUILayout.Width(96f)))
            ExecuteNewItemCreation(itemCode);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.LabelField("리스트 엔트리 추가는 Undo 가능. 저장은 상단 Save Assets.", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }

    private void CollectNewItemValidation(List<string> errors, List<string> warnings, out string itemCode)
    {
        itemCode = _newItemCode ?? string.Empty;

        if (!_hasScanned)
            errors.Add("Scan 먼저 실행.");

        if (_hasScanned && _primaryItemDatabase == null)
            errors.Add("ItemDatabase asset not found.");

        if (string.IsNullOrWhiteSpace(itemCode))
        {
            errors.Add("itemCode 입력 필요.");
        }
        else
        {
            if (itemCode.Trim() != itemCode)
                errors.Add("itemCode 앞뒤 공백 제거 필요.");

            if (_itemCodes.Contains(itemCode))
                errors.Add("itemCode 중복: " + itemCode);
        }

        if (_newItemCreateDropStub)
        {
            if (_primaryDropDatabase == null)
                errors.Add("드랍 스텁 등록 불가: EnemyDropDatabase asset not found.");

            if (_enemyOptions.Count == 0)
                errors.Add("드랍 스텁 등록 불가: EnemyData asset not found.");
        }

        if (string.IsNullOrWhiteSpace(_newItemDisplayName))
            warnings.Add("표시명 미입력 — 생성 후 편집 가능.");
    }

    private static void DrawValidationMessages(List<string> messages, MessageType type)
    {
        for (int i = 0; i < messages.Count; i++)
            EditorGUILayout.HelpBox(messages[i], type);
    }

    private void ExecuteNewItemCreation(string itemCode)
    {
        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        CollectNewItemValidation(errors, warnings, out itemCode);
        if (errors.Count > 0)
        {
            _newItemFeedback = string.Join("\n", errors);
            _newItemFeedbackType = MessageType.Error;
            return;
        }

        SerializedObject databaseObject = new SerializedObject(_primaryItemDatabase);
        databaseObject.Update();

        SerializedProperty items = databaseObject.FindProperty("items");
        if (items == null || !items.isArray)
        {
            _newItemFeedback = "ItemDatabase.items를 찾을 수 없음.";
            _newItemFeedbackType = MessageType.Error;
            return;
        }

        int index = items.arraySize;
        items.InsertArrayElementAtIndex(index);
        SerializedProperty item = items.GetArrayElementAtIndex(index);
        ResetItemEntry(item);
        SetString(item.FindPropertyRelative("itemCode"), itemCode);
        SetString(item.FindPropertyRelative("displayName"), _newItemDisplayName ?? string.Empty);
        SetEnum(item.FindPropertyRelative("itemType"), (int)_newItemType);
        ApplyNewItemTypeDefaults(item, _newItemType);

        if (!ApplyAndMark(databaseObject))
        {
            _newItemFeedback = "아이템 생성 변경 적용 실패.";
            _newItemFeedbackType = MessageType.Error;
            return;
        }

        bool dropRequested = _newItemCreateDropStub;
        bool dropCreated = false;
        string dropError = string.Empty;
        string dropEnemyName = string.Empty;
        if (dropRequested)
        {
            EnemyData enemy = _enemyOptions[Mathf.Clamp(_newItemDropEnemyIndex, 0, _enemyOptions.Count - 1)].Enemy;
            dropEnemyName = GetEnemyDisplayName(enemy);
            dropCreated = TryAppendDropEntry(_primaryDropDatabase, enemy, itemCode, out dropError);
        }

        string feedback = "아이템 생성 완료: " + itemCode;
        MessageType feedbackType = MessageType.Info;
        if (dropRequested)
        {
            if (dropCreated)
            {
                feedback += "\n드랍 스텁 생성 완료: " + dropEnemyName;
            }
            else
            {
                feedback += "\n아이템 생성됨, 드랍 스텁 실패: " + dropError;
                feedbackType = MessageType.Warning;
            }
        }
        if (warnings.Count > 0)
        {
            feedback += "\n" + string.Join("\n", warnings);
            if (feedbackType == MessageType.Info)
                feedbackType = MessageType.Warning;
        }

        ResetNewItemForm();
        Scan();
        _hasAssetChanges = true;
        _showNewItemForm = true;
        _newItemFeedback = feedback;
        _newItemFeedbackType = feedbackType;
    }

    private void ResetNewItemForm()
    {
        _newItemCode = string.Empty;
        _newItemDisplayName = string.Empty;
        _newItemType = ItemType.Material;
        _newItemCreateDropStub = false;
        _newItemDropEnemyIndex = 0;
    }

    private void DrawRowsPanel()
    {
        _rowScrollPosition = EditorGUILayout.BeginScrollView(_rowScrollPosition, true, true, GUILayout.ExpandHeight(true));
        if (_rows.Count == 0)
        {
            EditorGUILayout.HelpBox("No ItemDatabase entries found.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

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
        Header("", DeleteWidth);
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
        DrawDeleteCell(row);

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

    private void DrawDeleteCell(ItemRow row)
    {
        if (GUILayout.Button("삭제", GUILayout.Width(DeleteWidth)))
            ConfirmAndDeleteItem(row);
    }

    private void ConfirmAndDeleteItem(ItemRow row)
    {
        DeleteAnalysis analysis = BuildDeleteAnalysis(row);
        if (analysis == null)
            return;

        if (analysis.IsBlocked)
        {
            SetOperationFeedback("코드 상수 참조 아이템 — 삭제 차단: " + analysis.ItemCode, MessageType.Error);
            return;
        }

        string body = BuildDeleteDialogBody(analysis);
        if (!EditorUtility.DisplayDialog("아이템 삭제 확인", body, "삭제", "취소"))
            return;

        ExecuteItemDeletion(analysis);
    }

    private DeleteAnalysis BuildDeleteAnalysis(ItemRow row)
    {
        if (!TryGetRowItemProperty(row, out _, out SerializedProperty item))
        {
            SetOperationFeedback("항목 위치 변경됨. Rescan 권장", MessageType.Warning);
            return null;
        }

        string itemCode = GetString(item.FindPropertyRelative("itemCode"));
        string displayName = GetString(item.FindPropertyRelative("displayName"));
        ItemType itemType = GetItemType(item.FindPropertyRelative("itemType"));
        EngravingData engraving = GetObject<EngravingData>(item.FindPropertyRelative("engraving"));

        DeleteAnalysis analysis = new DeleteAnalysis(row.Database, row.Index, itemCode, displayName, itemType, engraving);
        analysis.IsBlocked = BuildBlockedItemCodeSet().Contains(itemCode);
        analysis.DropEntryCount = CountLiveDropEntries(itemCode);
        analysis.SalvageReferenceCount = CountLiveSalvageReferences(row.Database, row.Index, itemCode);
        return analysis;
    }

    private static string BuildDeleteDialogBody(DeleteAnalysis analysis)
    {
        string displayName = !string.IsNullOrWhiteSpace(analysis.DisplayName) ? analysis.DisplayName : analysis.ItemCode;
        string body =
            displayName + " 삭제\n\n" +
            "드랍 엔트리 " + analysis.DropEntryCount + "건 같이 제거 / DB 엔트리 1건 제거\n";

        if (analysis.SalvageReferenceCount > 0)
            body += "⚠ 타 아이템 salvage 참조 " + analysis.SalvageReferenceCount + "건 — 삭제 후 죽은 코드로 남음(수동 정리 필요)\n";

        if (analysis.ItemType == ItemType.Engraving)
            body += "각인 에셋은 유지됨(고아화) — Validator에서 재등록 가능\n";

        body += "\n전부 Undo 가능(Ctrl+Z 1회)";
        return body;
    }

    private void ExecuteItemDeletion(DeleteAnalysis analysis)
    {
        List<string> failures = new List<string>();

        Undo.SetCurrentGroupName("Delete Item Dashboard Item");
        int undoGroup = Undo.GetCurrentGroup();

        int removedDropEntries = RemoveDropEntriesForItemCode(analysis.ItemCode, failures);
        bool itemRemoved = RemoveItemEntry(analysis, failures);

        Undo.CollapseUndoOperations(undoGroup);

        Scan();
        _hasAssetChanges = true;
        _operationFeedback = BuildItemDeletionFeedback(analysis, removedDropEntries, itemRemoved, failures);
        _operationFeedbackType = failures.Count > 0 || !itemRemoved ? MessageType.Warning : MessageType.Info;
    }

    private string BuildItemDeletionFeedback(DeleteAnalysis analysis, int removedDropEntries, bool itemRemoved, List<string> failures)
    {
        string feedback = itemRemoved
            ? "삭제 완료: " + analysis.ItemCode + " / 드랍 엔트리 " + removedDropEntries + "건 제거"
            : "삭제 실패: " + analysis.ItemCode + " / 드랍 엔트리 " + removedDropEntries + "건 제거";

        if (analysis.SalvageReferenceCount > 0)
            feedback += "\nsalvage 참조 " + analysis.SalvageReferenceCount + "건 잔존 — Rescan 경고 확인 필요";

        if (failures.Count > 0)
            feedback += "\n일부 제거 실패: " + string.Join(" / ", failures);

        return feedback;
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
        int nextMaxStack = maxStack != null ? EditorGUILayout.IntField("maxStack", maxStack.intValue) : 1;
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
                DrawBehaviorEffectArray(row, serializedObject, item.FindPropertyRelative("behaviorEffects"));
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
        int min = salvageMinAmount != null ? EditorGUILayout.IntField("salvageMin", salvageMinAmount.intValue) : 1;
        int max = salvageMaxAmount != null ? EditorGUILayout.IntField("salvageMax", salvageMaxAmount.intValue) : min;
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
            int nextValue = value != null ? EditorGUILayout.IntField(value.intValue, GUILayout.Width(80f)) : 0;
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

    private void DrawBehaviorEffectArray(
        ItemRow row,
        SerializedObject serializedObject,
        SerializedProperty behaviors)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("behaviorEffects[]", EditorStyles.miniBoldLabel);

        if (behaviors == null || !behaviors.isArray)
        {
            EditorGUILayout.HelpBox("behaviorEffects[] missing.", MessageType.Error);
            return;
        }

        for (int i = 0; i < behaviors.arraySize; i++)
        {
            SerializedProperty behavior = behaviors.GetArrayElementAtIndex(i);
            SerializedProperty trigger = behavior.FindPropertyRelative("trigger");
            SerializedProperty action = behavior.FindPropertyRelative("action");
            SerializedProperty skillTypeFilter = behavior.FindPropertyRelative("skillTypeFilter");
            SerializedProperty value = behavior.FindPropertyRelative("value");
            SerializedProperty duration = behavior.FindPropertyRelative("duration");
            SerializedProperty procSkill = behavior.FindPropertyRelative("procSkill");
            SerializedProperty procOrigin = behavior.FindPropertyRelative("procOrigin");
            SerializedProperty procDirection = behavior.FindPropertyRelative("procDirection");
            SerializedProperty procSpawnRadius = behavior.FindPropertyRelative("procSpawnRadius");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Behavior " + i, EditorStyles.miniBoldLabel);
            bool delete = GUILayout.Button("-", GUILayout.Width(24f));
            EditorGUILayout.EndHorizontal();

            if (delete)
            {
                DeleteArrayElement(behaviors, i);
                bool deleted = ApplyItemPropertyChange(row, serializedObject);
                EditorGUILayout.EndVertical();
                if (deleted)
                    GUIUtility.ExitGUI();
                continue;
            }

            EditorGUI.BeginChangeCheck();
            int nextTrigger = trigger != null
                ? EditorGUILayout.Popup("trigger", trigger.enumValueIndex, trigger.enumDisplayNames)
                : 0;
            int nextAction = action != null
                ? EditorGUILayout.Popup("action", action.enumValueIndex, action.enumDisplayNames)
                : 0;
            int nextFilter = skillTypeFilter != null ? skillTypeFilter.intValue : 0;
            if ((BehaviorTrigger)nextTrigger == BehaviorTrigger.OnSkillUsed)
                nextFilter = EditorGUILayout.MaskField("skillTypeFilter", nextFilter, s_SkillExecutionTypeNames);
            BehaviorAction selectedAction = (BehaviorAction)nextAction;
            int nextValue = value != null ? value.intValue : 0;
            if (selectedAction != BehaviorAction.CastSkill)
                nextValue = EditorGUILayout.IntField("value", nextValue);
            SkillData nextProcSkill = procSkill != null ? procSkill.objectReferenceValue as SkillData : null;
            int nextProcOrigin = procOrigin != null ? procOrigin.enumValueIndex : 0;
            int nextProcDirection = procDirection != null ? procDirection.enumValueIndex : 0;
            float nextProcSpawnRadius = procSpawnRadius != null ? procSpawnRadius.floatValue : 0f;
            if (selectedAction == BehaviorAction.CastSkill)
            {
                nextProcSkill = (SkillData)EditorGUILayout.ObjectField(
                    "procSkill", nextProcSkill, typeof(SkillData), false);
                nextProcOrigin = EditorGUILayout.Popup(
                    "procOrigin", nextProcOrigin, procOrigin.enumDisplayNames);
                nextProcDirection = EditorGUILayout.Popup(
                    "procDirection", nextProcDirection, procDirection.enumDisplayNames);
                if ((ProcOriginMode)nextProcOrigin == ProcOriginMode.RandomInRadius)
                    nextProcSpawnRadius = EditorGUILayout.FloatField("procSpawnRadius", nextProcSpawnRadius);
            }
            float nextDuration = duration != null ? duration.floatValue : 0f;
            if (IsAttackAilmentAction(selectedAction))
                nextDuration = EditorGUILayout.FloatField("duration", nextDuration);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndVertical();

            if (!changed)
                continue;

            SetEnum(trigger, nextTrigger);
            SetEnum(action, nextAction);
            SetInt(skillTypeFilter, nextFilter);
            if (selectedAction != BehaviorAction.CastSkill)
                SetInt(value, nextValue);
            SetFloat(duration, nextDuration);
            SetObject(procSkill, nextProcSkill);
            SetEnum(procOrigin, nextProcOrigin);
            SetEnum(procDirection, nextProcDirection);
            SetFloat(procSpawnRadius, nextProcSpawnRadius);
            ApplyItemPropertyChange(row, serializedObject);
        }

        bool added = false;
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16f);
        if (GUILayout.Button("+ behavior", GUILayout.Width(100f)))
        {
            int index = behaviors.arraySize;
            behaviors.InsertArrayElementAtIndex(index);
            SerializedProperty behavior = behaviors.GetArrayElementAtIndex(index);
            SetEnum(behavior.FindPropertyRelative("trigger"), (int)BehaviorTrigger.OnKill);
            SetEnum(behavior.FindPropertyRelative("action"), (int)BehaviorAction.Heal);
            SetInt(behavior.FindPropertyRelative("skillTypeFilter"), 0);
            SetInt(behavior.FindPropertyRelative("value"), 0);
            SetFloat(behavior.FindPropertyRelative("duration"), 0f);
            SetObject(behavior.FindPropertyRelative("procSkill"), null);
            SetEnum(behavior.FindPropertyRelative("procOrigin"), (int)ProcOriginMode.CasterPosition);
            SetEnum(behavior.FindPropertyRelative("procDirection"), (int)ProcDirectionMode.Aim);
            SetFloat(behavior.FindPropertyRelative("procSpawnRadius"), 0f);
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
            DrawDropEntryHeader();
            for (int i = 0; i < row.DropSources.Count; i++)
                DrawDropEntryRow(row, row.DropSources[i]);
        }

        DrawAddDropEntry(row);
        EditorGUILayout.EndVertical();
    }

    private static void DrawDropEntryHeader()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("적", EditorStyles.miniBoldLabel, GUILayout.Width(DropEnemyWidth));
        GUILayout.Label("종류", EditorStyles.miniBoldLabel, GUILayout.Width(DropKindWidth));
        GUILayout.Label("min", EditorStyles.miniBoldLabel, GUILayout.Width(DropAmountWidth));
        GUILayout.Label("max", EditorStyles.miniBoldLabel, GUILayout.Width(DropAmountWidth));
        GUILayout.Label("그룹확률", EditorStyles.miniBoldLabel, GUILayout.Width(DropGroupChanceWidth));
        GUILayout.Label("확률/가중치", EditorStyles.miniBoldLabel, GUILayout.Width(DropChanceWidth));
        GUILayout.Label(GUIContent.none, GUILayout.Width(DropRemoveWidth));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawDropEntryRow(ItemRow row, DropSourceRecord source)
    {
        EditorGUILayout.BeginHorizontal();

        if (source.Enemy != null)
        {
            if (GUILayout.Button(source.EnemyName, GUILayout.Width(DropEnemyWidth)))
                EditorGUIUtility.PingObject(source.Enemy);
        }
        else
        {
            GUILayout.Label(source.EnemyName, GUILayout.Width(DropEnemyWidth));
        }

        GUILayout.Label(source.Kind == DropEntryKind.Drop ? "drop" : "택1", GUILayout.Width(DropKindWidth));

        EditorGUI.BeginChangeCheck();
        int min = EditorGUILayout.IntField(source.MinAmount, GUILayout.Width(DropAmountWidth));
        int max = EditorGUILayout.IntField(source.MaxAmount, GUILayout.Width(DropAmountWidth));
        bool amountChanged = EditorGUI.EndChangeCheck();

        if (source.Kind == DropEntryKind.Drop)
        {
            GUILayout.Label("-", GUILayout.Width(DropGroupChanceWidth));
            EditorGUI.BeginChangeCheck();
            float chance = EditorGUILayout.FloatField(source.Chance, GUILayout.Width(DropChanceWidth));
            bool chanceChanged = EditorGUI.EndChangeCheck();
            if (amountChanged || chanceChanged)
                ApplyDropEntryValues(row, source, min, max, chance);
        }
        else
        {
            GUILayout.Label("그룹 " + FormatChance(source.GroupChance), GUILayout.Width(DropGroupChanceWidth));
            EditorGUI.BeginChangeCheck();
            float weight = EditorGUILayout.FloatField(source.Weight, GUILayout.Width(DropChanceWidth));
            bool weightChanged = EditorGUI.EndChangeCheck();
            if (amountChanged || weightChanged)
                ApplyDropEntryValues(row, source, min, max, weight);
        }

        bool removed = GUILayout.Button("-", GUILayout.Width(DropRemoveWidth)) && RemoveDropEntry(row, source);

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

        if (!TryAppendDropEntry(_primaryDropDatabase, enemy, row.ItemCode, out string error))
        {
            SetOperationFeedback(error, MessageType.Error);
            return false;
        }

        RebuildDropCachesForAllRows();
        SetOperationFeedback("드랍 추가 완료: " + GetEnemyDisplayName(enemy) + " -> " + row.ItemCode, MessageType.Info);
        return true;
    }

    private bool TryAppendDropEntry(EnemyDropDatabase database, EnemyData enemy, string itemCode, out string error)
    {
        error = string.Empty;
        if (database == null)
        {
            error = "EnemyDropDatabase asset not found.";
            return false;
        }

        if (enemy == null)
        {
            error = "EnemyData가 null입니다.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(itemCode))
        {
            error = "itemCode 공백.";
            return false;
        }

        SerializedObject databaseObject = new SerializedObject(database);
        databaseObject.Update();

        SerializedProperty groups = databaseObject.FindProperty("groups");
        if (groups == null || !groups.isArray)
        {
            error = "EnemyDropDatabase.groups를 찾을 수 없음.";
            return false;
        }

        SerializedProperty group = FindOrCreateDropGroup(groups, enemy);
        SerializedProperty drops = group != null ? group.FindPropertyRelative("drops") : null;
        if (drops == null || !drops.isArray)
        {
            error = "EnemyDropDatabase drops[]를 찾을 수 없음.";
            return false;
        }

        int index = drops.arraySize;
        drops.InsertArrayElementAtIndex(index);
        SerializedProperty drop = drops.GetArrayElementAtIndex(index);
        InitializeDrop(drop, itemCode);

        if (!ApplyAndMark(databaseObject))
        {
            error = "드랍 추가 변경 적용 실패.";
            return false;
        }

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

    private void DrawPanelSplitter()
    {
        Rect splitterRect = GUILayoutUtility.GetRect(0f, 5f, GUILayout.ExpandWidth(true));
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
                    _warningsPanelHeight -= evt.delta.y;
                    ClampWarningsPanelHeight();
                    evt.Use();
                    Repaint();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    evt.Use();
                    EditorPrefs.SetFloat(WarningsPanelHeightKey, _warningsPanelHeight);
                }
                break;
        }
    }

    private void ClampWarningsPanelHeight()
    {
        float maxHeight = Mathf.Max(MinWarningsPanelHeight, position.height - WarningsPanelMaxPadding);
        _warningsPanelHeight = Mathf.Clamp(_warningsPanelHeight, MinWarningsPanelHeight, maxHeight);
    }

    private void DrawWarningsPanel()
    {
        EditorGUILayout.LabelField("Warnings", EditorStyles.boldLabel);

        _warningScrollPosition = EditorGUILayout.BeginScrollView(_warningScrollPosition, false, true, GUILayout.Height(_warningsPanelHeight));
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
        _itemDatabases.Clear();
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

        _itemDatabases.AddRange(itemDatabases);
        _primaryItemDatabase = itemDatabases.Count > 0 ? itemDatabases[0] : null;
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
        RefreshBehaviorWarningState(row, item.FindPropertyRelative("behaviorEffects"));
        row.TypeSummary = BuildTypeSummary(row, item);
    }

    private static void RefreshBehaviorWarningState(ItemRow row, SerializedProperty behaviors)
    {
        row.BehaviorEffectCount = behaviors != null && behaviors.isArray ? behaviors.arraySize : 0;
        row.HasUnfilteredOnSkillUsedBehavior = false;
        row.HasNonPositiveBehaviorValue = false;
        row.HasInvalidBehaviorCombination = false;
        row.HasNonPositiveAttackAilmentDuration = false;
        row.HasMissingProcSkill = false;
        row.HasUnsupportedProcSkill = false;
        row.HasOnSkillUsedHitPosition = false;
        row.HasNonPositiveProcSpawnRadius = false;
        row.HasOnSkillUsedContextDirection = false;

        for (int i = 0; i < row.BehaviorEffectCount; i++)
        {
            SerializedProperty behavior = behaviors.GetArrayElementAtIndex(i);
            BehaviorTrigger trigger = (BehaviorTrigger)GetInt(behavior.FindPropertyRelative("trigger"));
            BehaviorAction action = (BehaviorAction)GetInt(behavior.FindPropertyRelative("action"));
            int skillTypeFilter = GetInt(behavior.FindPropertyRelative("skillTypeFilter"));
            int value = GetInt(behavior.FindPropertyRelative("value"));
            float duration = GetFloat(behavior.FindPropertyRelative("duration"));
            SkillData procSkill = GetObject<SkillData>(behavior.FindPropertyRelative("procSkill"));
            ProcOriginMode procOrigin = (ProcOriginMode)GetInt(behavior.FindPropertyRelative("procOrigin"));
            ProcDirectionMode procDirection = (ProcDirectionMode)GetInt(behavior.FindPropertyRelative("procDirection"));
            float procSpawnRadius = GetFloat(behavior.FindPropertyRelative("procSpawnRadius"));

            if (trigger == BehaviorTrigger.OnSkillUsed && skillTypeFilter == 0)
                row.HasUnfilteredOnSkillUsedBehavior = true;
            if (action != BehaviorAction.CastSkill && value <= 0)
                row.HasNonPositiveBehaviorValue = true;
            if (!IsValidBehaviorCombination(trigger, action))
                row.HasInvalidBehaviorCombination = true;
            if (IsAttackAilmentAction(action) && duration <= 0f)
                row.HasNonPositiveAttackAilmentDuration = true;
            if (action == BehaviorAction.CastSkill)
            {
                if (procSkill == null)
                    row.HasMissingProcSkill = true;
                else if (!IsProcExecutionTypeSupported(procSkill.executionType))
                    row.HasUnsupportedProcSkill = true;
                if ((trigger == BehaviorTrigger.OnSkillUsed || trigger == BehaviorTrigger.OnSkillCanceled) &&
                    procOrigin == ProcOriginMode.HitPosition)
                    row.HasOnSkillUsedHitPosition = true;
                if (procOrigin == ProcOriginMode.RandomInRadius && procSpawnRadius <= 0f)
                    row.HasNonPositiveProcSpawnRadius = true;
                if ((trigger == BehaviorTrigger.OnSkillUsed || trigger == BehaviorTrigger.OnSkillCanceled) &&
                    procDirection == ProcDirectionMode.Context)
                    row.HasOnSkillUsedContextDirection = true;
            }
        }
    }

    private static bool IsAttackAilmentAction(BehaviorAction action)
    {
        return action == BehaviorAction.AttackPoison || action == BehaviorAction.AttackBleed;
    }

    private static bool IsValidBehaviorCombination(BehaviorTrigger trigger, BehaviorAction action)
    {
        if (trigger == BehaviorTrigger.Passive)
            return IsAttackAilmentAction(action);

        return (trigger == BehaviorTrigger.OnKill ||
                trigger == BehaviorTrigger.OnSkillUsed ||
                trigger == BehaviorTrigger.OnSkillCanceled) &&
               (action == BehaviorAction.Heal || action == BehaviorAction.CastSkill);
    }

    private static bool IsProcExecutionTypeSupported(SkillExecutionType executionType)
    {
        return executionType == SkillExecutionType.AreaOverTime ||
               executionType == SkillExecutionType.InstantArea ||
               executionType == SkillExecutionType.Projectile ||
               executionType == SkillExecutionType.Buff;
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

        if (row.HasUnfilteredOnSkillUsedBehavior)
            AddWarning(row, WarningSeverity.Error, "[Error] OnSkillUsed behavior의 skillTypeFilter가 0: " + location, row.Database);

        if (row.HasNonPositiveBehaviorValue)
            AddWarning(row, WarningSeverity.Warning, "[Warn] behaviorEffects value가 0 이하: " + location, row.Database);

        if (row.HasInvalidBehaviorCombination)
            AddWarning(row, WarningSeverity.Error, "[Error] behaviorEffects trigger/action 조합이 유효하지 않음: " + location, row.Database);

        if (row.HasNonPositiveAttackAilmentDuration)
            AddWarning(row, WarningSeverity.Warning, "[Warn] 공격 ailment behavior의 duration이 0 이하: " + location, row.Database);

        if (row.HasMissingProcSkill)
            AddWarning(row, WarningSeverity.Error, "[Error] CastSkill behavior의 procSkill 참조가 null: " + location, row.Database);

        if (row.HasUnsupportedProcSkill)
            AddWarning(row, WarningSeverity.Error, "[Error] CastSkill behavior의 procSkill 실행 타입이 proc 화이트리스트 밖: " + location, row.Database);

        if (row.HasOnSkillUsedHitPosition)
            AddWarning(row, WarningSeverity.Error, "[Error] OnSkillUsed/OnSkillCanceled×CastSkill은 HitPosition 문맥을 사용할 수 없음: " + location, row.Database);

        if (row.HasNonPositiveProcSpawnRadius)
            AddWarning(row, WarningSeverity.Warning, "[Warn] RandomInRadius behavior의 procSpawnRadius가 0 이하: " + location, row.Database);

        if (row.HasOnSkillUsedContextDirection)
            AddWarning(row, WarningSeverity.Info, "[Info] OnSkillUsed/OnSkillCanceled×Context 방향은 Aim과 동일하게 동작: " + location, row.Database);

        if (row.BehaviorEffectCount > 0 && row.ItemType != ItemType.Relic)
            AddWarning(row, WarningSeverity.Warning, "[Warn] Relic이 아닌 아이템의 behaviorEffects는 런타임에서 미소비: " + location, row.Database);

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

    private static HashSet<string> BuildBlockedItemCodeSet()
    {
        HashSet<string> codes = new HashSet<string>(StringComparer.Ordinal);
        FieldInfo[] fields = typeof(ItemCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            if (!field.IsLiteral || field.FieldType != typeof(string))
                continue;

            string value = field.GetRawConstantValue() as string;
            if (!string.IsNullOrWhiteSpace(value))
                codes.Add(value);
        }

        // Runtime serialized defaults: CurrencyCounterUI.currencyItemCode, RestAreaShopUIController.currencyItemCode.
        codes.Add("Currency");
        // Runtime lookup/drop key: PlayerController/EnemyController/RoomSpawner use DeterministicSeedUtility.EliteKeyDomain.
        codes.Add(DeterministicSeedUtility.EliteKeyDomain);
        return codes;
    }

    private static int CountLiveDropEntries(string itemCode)
    {
        int count = 0;
        List<EnemyDropDatabase> databases = LoadAssets<EnemyDropDatabase>("t:EnemyDropDatabase");
        for (int i = 0; i < databases.Count; i++)
            count += CountDropEntriesInDatabase(databases[i], itemCode);

        return count;
    }

    private static int CountDropEntriesInDatabase(EnemyDropDatabase database, string itemCode)
    {
        if (database == null)
            return 0;

        SerializedObject databaseObject = new SerializedObject(database);
        SerializedProperty groups = databaseObject.FindProperty("groups");
        if (groups == null || !groups.isArray)
            return 0;

        int count = 0;
        for (int groupIndex = 0; groupIndex < groups.arraySize; groupIndex++)
        {
            SerializedProperty group = groups.GetArrayElementAtIndex(groupIndex);
            count += CountDropEntriesInArray(group.FindPropertyRelative("drops"), itemCode);

            SerializedProperty choiceGroups = group.FindPropertyRelative("choiceGroups");
            if (choiceGroups == null || !choiceGroups.isArray)
                continue;

            for (int choiceGroupIndex = 0; choiceGroupIndex < choiceGroups.arraySize; choiceGroupIndex++)
            {
                SerializedProperty choiceGroup = choiceGroups.GetArrayElementAtIndex(choiceGroupIndex);
                count += CountDropEntriesInArray(choiceGroup.FindPropertyRelative("choices"), itemCode);
            }
        }

        return count;
    }

    private static int CountDropEntriesInArray(SerializedProperty array, string itemCode)
    {
        if (array == null || !array.isArray)
            return 0;

        int count = 0;
        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty entry = array.GetArrayElementAtIndex(i);
            if (string.Equals(GetString(entry.FindPropertyRelative("itemCode")), itemCode, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private static int CountLiveSalvageReferences(ItemDatabase targetDatabase, int targetIndex, string itemCode)
    {
        int count = 0;
        List<ItemDatabase> databases = LoadAssets<ItemDatabase>("t:ItemDatabase");
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
                if (database == targetDatabase && itemIndex == targetIndex)
                    continue;

                SerializedProperty item = items.GetArrayElementAtIndex(itemIndex);
                string salvageItemCode = GetString(item.FindPropertyRelative("salvageItemCode"));
                if (string.Equals(salvageItemCode, itemCode, StringComparison.Ordinal))
                    count++;
            }
        }

        return count;
    }

    private int RemoveDropEntriesForItemCode(string itemCode, List<string> failures)
    {
        int removed = 0;
        List<EnemyDropDatabase> databases = LoadAssets<EnemyDropDatabase>("t:EnemyDropDatabase");
        for (int i = 0; i < databases.Count; i++)
            removed += RemoveDropEntriesFromDatabase(databases[i], itemCode, failures);

        return removed;
    }

    private int RemoveDropEntriesFromDatabase(EnemyDropDatabase database, string itemCode, List<string> failures)
    {
        if (database == null)
            return 0;

        SerializedObject databaseObject = new SerializedObject(database);
        databaseObject.Update();
        SerializedProperty groups = databaseObject.FindProperty("groups");
        if (groups == null || !groups.isArray)
        {
            failures.Add(database.name + ".groups 없음");
            return 0;
        }

        int removed = 0;
        for (int groupIndex = 0; groupIndex < groups.arraySize; groupIndex++)
        {
            SerializedProperty group = groups.GetArrayElementAtIndex(groupIndex);
            removed += RemoveDropEntriesFromArray(group.FindPropertyRelative("drops"), itemCode);

            SerializedProperty choiceGroups = group.FindPropertyRelative("choiceGroups");
            if (choiceGroups == null || !choiceGroups.isArray)
                continue;

            for (int choiceGroupIndex = 0; choiceGroupIndex < choiceGroups.arraySize; choiceGroupIndex++)
            {
                SerializedProperty choiceGroup = choiceGroups.GetArrayElementAtIndex(choiceGroupIndex);
                removed += RemoveDropEntriesFromArray(choiceGroup.FindPropertyRelative("choices"), itemCode);
            }
        }

        if (removed > 0 && !ApplyAndMark(databaseObject))
            failures.Add(database.name + " 드랍 엔트리 제거 적용 실패");

        return removed;
    }

    private static int RemoveDropEntriesFromArray(SerializedProperty array, string itemCode)
    {
        if (array == null || !array.isArray)
            return 0;

        int removed = 0;
        for (int i = array.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty entry = array.GetArrayElementAtIndex(i);
            if (!string.Equals(GetString(entry.FindPropertyRelative("itemCode")), itemCode, StringComparison.Ordinal))
                continue;

            DeleteArrayElement(array, i);
            removed++;
        }

        return removed;
    }

    private bool RemoveItemEntry(DeleteAnalysis analysis, List<string> failures)
    {
        SerializedObject databaseObject = new SerializedObject(analysis.Database);
        databaseObject.Update();

        SerializedProperty items = databaseObject.FindProperty("items");
        if (items == null || !items.isArray)
        {
            failures.Add(analysis.Database.name + ".items 없음");
            return false;
        }

        if (analysis.Index < 0 || analysis.Index >= items.arraySize)
        {
            failures.Add("items index stale. Rescan 권장");
            return false;
        }

        SerializedProperty item = items.GetArrayElementAtIndex(analysis.Index);
        string currentCode = GetString(item.FindPropertyRelative("itemCode"));
        if (!string.Equals(currentCode, analysis.ItemCode, StringComparison.Ordinal))
        {
            failures.Add("항목 위치 변경됨. Rescan 권장");
            return false;
        }

        DeleteArrayElement(items, analysis.Index);
        if (!ApplyAndMark(databaseObject))
        {
            failures.Add(analysis.Database.name + " 아이템 제거 적용 실패");
            return false;
        }

        return true;
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

    private static void ResetItemEntry(SerializedProperty item)
    {
        if (item == null)
            return;

        SetString(item.FindPropertyRelative("itemCode"), string.Empty);
        SetString(item.FindPropertyRelative("displayName"), string.Empty);
        SetObject(item.FindPropertyRelative("icon"), null);
        SetString(item.FindPropertyRelative("description"), string.Empty);
        SetEnum(item.FindPropertyRelative("itemType"), 0);
        SetBool(item.FindPropertyRelative("stackable"), false);
        SetInt(item.FindPropertyRelative("maxStack"), 1);
        SetBool(item.FindPropertyRelative("removeOnFloorTransition"), false);
        SetBool(item.FindPropertyRelative("removeOnDungeonExit"), false);
        SetArraySize(item.FindPropertyRelative("useEffects"), 0);
        SetArraySize(item.FindPropertyRelative("passiveEffects"), 0);
        SetArraySize(item.FindPropertyRelative("behaviorEffects"), 0);
        SetEnum(item.FindPropertyRelative("soulFormId"), 0);
        SetObject(item.FindPropertyRelative("engraving"), null);
        SetString(item.FindPropertyRelative("salvageItemCode"), string.Empty);
        SetInt(item.FindPropertyRelative("salvageMinAmount"), 1);
        SetInt(item.FindPropertyRelative("salvageMaxAmount"), 1);
    }

    private static void ApplyNewItemTypeDefaults(SerializedProperty item, ItemType itemType)
    {
        if (item == null)
            return;

        switch (itemType)
        {
            case ItemType.Currency:
                SetBool(item.FindPropertyRelative("stackable"), true);
                SetInt(item.FindPropertyRelative("maxStack"), 9999);
                SetBool(item.FindPropertyRelative("removeOnDungeonExit"), true);
                break;

            case ItemType.Relic:
                SetBool(item.FindPropertyRelative("removeOnDungeonExit"), true);
                break;
        }
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

    private static void SetBool(SerializedProperty property, bool value)
    {
        if (property != null)
            property.boolValue = value;
    }

    private static void SetEnum(SerializedProperty property, int value)
    {
        if (property != null)
            property.enumValueIndex = value;
    }

    private static void SetObject(SerializedProperty property, Object value)
    {
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static void SetArraySize(SerializedProperty property, int size)
    {
        if (property != null && property.isArray)
            property.arraySize = size;
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
        public int BehaviorEffectCount;
        public bool HasUnfilteredOnSkillUsedBehavior;
        public bool HasNonPositiveBehaviorValue;
        public bool HasInvalidBehaviorCombination;
        public bool HasNonPositiveAttackAilmentDuration;
        public bool HasMissingProcSkill;
        public bool HasUnsupportedProcSkill;
        public bool HasOnSkillUsedHitPosition;
        public bool HasNonPositiveProcSpawnRadius;
        public bool HasOnSkillUsedContextDirection;
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

    private sealed class DeleteAnalysis
    {
        public readonly ItemDatabase Database;
        public readonly int Index;
        public readonly string ItemCode;
        public readonly string DisplayName;
        public readonly ItemType ItemType;
        public readonly EngravingData Engraving;
        public bool IsBlocked;
        public int DropEntryCount;
        public int SalvageReferenceCount;

        public DeleteAnalysis(
            ItemDatabase database,
            int index,
            string itemCode,
            string displayName,
            ItemType itemType,
            EngravingData engraving)
        {
            Database = database;
            Index = index;
            ItemCode = itemCode;
            DisplayName = displayName;
            ItemType = itemType;
            Engraving = engraving;
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
