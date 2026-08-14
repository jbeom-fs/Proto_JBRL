using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class EnemyDashboardWindow : EditorWindow
{
    private enum DashboardTab
    {
        PerEnemy,
        Rank
    }

    private const float FoldoutWidth = 20f;
    private const float WarningWidth = 28f;
    private const float NameWidth = 170f;
    private const float StatWidth = 48f;
    private const float MoveWidth = 62f;
    private const float FloorEditWidth = 52f;
    private const float CostWidth = 72f;
    private const float TypeWidth = 190f;
    private const float PrefabWidth = 170f;
    private const float BossWidth = 58f;
    private const float DropWidth = 440f;
    private const float DeleteWidth = 52f;
    private const float WarningPanelHeight = 180f;
    private const string WarningsPanelHeightKey = "JBRogLike.EnemyDashboard.WarningsPanelHeight";
    private const float MinWarningsPanelHeight = 60f;
    private const float WarningsPanelMaxPadding = 220f;
    private const string DefaultDropItemCode = "Currency";
    private const string NewEnemyAssetFolder = "Assets/Scriptable/Enemy";

    private static readonly DropQueryCategory[] s_QueryCategoryValues =
        (DropQueryCategory[])Enum.GetValues(typeof(DropQueryCategory));
    private static readonly string[] s_QueryCategoryNames =
        Array.ConvertAll(s_QueryCategoryValues, value => value.ToString());
    private static readonly DropFormScope[] s_FormScopeValues =
        (DropFormScope[])Enum.GetValues(typeof(DropFormScope));
    private static readonly string[] s_FormScopeNames = Array.ConvertAll(s_FormScopeValues, value => value.ToString());
    private static readonly PlayerFormId[] s_FormValues = (PlayerFormId[])Enum.GetValues(typeof(PlayerFormId));
    private static readonly string[] s_FormNames = Array.ConvertAll(s_FormValues, value => value.ToString());

    private readonly List<EnemyRow> _rows = new List<EnemyRow>(32);
    private readonly List<DashboardWarning> _warnings = new List<DashboardWarning>(64);
    private readonly HashSet<string> _itemCodes = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<EnemyDropDatabase> _scannedDropDatabases = new List<EnemyDropDatabase>(4);
    private readonly List<BossEncounterTable> _scannedBossEncounterTables = new List<BossEncounterTable>(2);
    private readonly List<EnemyController> _scannedSpawnPrefabs = new List<EnemyController>(32);
    private readonly List<ItemData> _queryItems = new List<ItemData>(64);
    private DashboardTab _tab = DashboardTab.PerEnemy;
    private Vector2 _rowScrollPosition;
    private Vector2 _rankScroll;
    private Vector2 _warningScrollPosition;
    private float _warningsPanelHeight = WarningPanelHeight;
    private bool _hasScanned;
    private bool _hasPoolScene;
    private bool _hasAssetChanges;
    private bool _saveActionPending;
    private Action _queuedSaveAction;
    private bool _showNewEnemyForm;
    private string _lastScanLabel = "-";
    private EnemyDropDatabase _primaryDropDatabase;
    private BossEncounterTable _primaryBossEncounterTable;
    private bool _bossTableLookupAttempted;
    private EnemyData _newEnemyTemplate;
    private string _newEnemyName = string.Empty;
    private int _newEnemyPrefabIndex;
    private bool _newEnemyCreateAsBoss;
    private int _newBossFloor = 1;
    private string _newBossAreaDestinationId = string.Empty;
    private string _newBossAreaId = string.Empty;
    private bool _newBossIsFinal;
    private string _newEnemyFeedback = string.Empty;
    private MessageType _newEnemyFeedbackType = MessageType.Info;
    private string _operationFeedback = string.Empty;
    private MessageType _operationFeedbackType = MessageType.Info;

    [MenuItem("JBRogLike/Enemy Dashboard")]
    public static void Open()
    {
        GetWindow<EnemyDashboardWindow>("Enemy Dashboard");
    }

    private void OnEnable()
    {
        minSize = new Vector2(1180f, 520f);
        _warningsPanelHeight = EditorPrefs.GetFloat(WarningsPanelHeightKey, WarningPanelHeight);
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        EditorApplication.delayCall -= ExecuteQueuedSaveAction;
        _saveActionPending = false;
        _queuedSaveAction = null;
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
        _tab = (DashboardTab)GUILayout.Toolbar((int)_tab, new[] { "개인 드롭", "등급 드롭" });
        ClampWarningsPanelHeight();

        if (_showNewEnemyForm)
            DrawNewEnemyPanel();

        if (!_hasScanned)
        {
            EditorGUILayout.HelpBox("Click Scan to build the Enemy Dashboard.", MessageType.Info);
            return;
        }

        DrawSummary();
        if (_tab == DashboardTab.PerEnemy)
            DrawRowsPanel();
        else
            DrawRankDropsPanel();
        DrawPanelSplitter();
        DrawWarningsPanel();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            Scan();
        _showNewEnemyForm = GUILayout.Toggle(_showNewEnemyForm, "New Enemy", EditorStyles.toolbarButton, GUILayout.Width(96f));

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save Assets", EditorStyles.toolbarButton, GUILayout.Width(96f)))
            SaveScopedAssets();

        bool activeSceneDirty = IsActiveSceneDirty();
        if (activeSceneDirty)
            GUILayout.Label("씬 변경은 별도 저장 필요", EditorStyles.miniBoldLabel);

        EditorGUI.BeginDisabledGroup(!activeSceneDirty);
        if (GUILayout.Button("Save Scene", EditorStyles.toolbarButton, GUILayout.Width(90f)))
            SaveActiveScene();
        EditorGUI.EndDisabledGroup();

        GUILayout.Label("Last scan: " + _lastScanLabel, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // SerializedObject/Undo paths stay separate. Global SaveAssets is forbidden; save only dirty scanned assets.
    private void SaveScopedAssets()
    {
        QueueSaveAfterFocusFlush(SaveScopedAssetsCore);
    }

    private void SaveScopedAssetsCore()
    {
        if (!_hasScanned)
        {
            Debug.LogWarning("[EnemyDashboardWindow] Scan first; no scoped assets were saved.");
            return;
        }

        var assets = new HashSet<Object>();
        CollectScopedAssets(assets);
        int savedCount = SaveDirtyAssets(assets, out int dirtyCount);
        if (savedCount == dirtyCount)
            _hasAssetChanges = false;

        Debug.Log(
            "[EnemyDashboardWindow] Scoped save: assets=" + assets.Count +
            ", dirty=" + dirtyCount + ", saved=" + savedCount + ".");
        Repaint();
    }

    private void CollectScopedAssets(HashSet<Object> assets)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            EnemyRow row = _rows[i];
            if (row == null)
                continue;

            AddMainAsset(assets, row.Data);
            AddMainAsset(assets, row.Prefab);
            for (int groupIndex = 0; groupIndex < row.DropGroups.Count; groupIndex++)
                AddMainAsset(assets, row.DropGroups[groupIndex].Database);
        }

        AddMainAssets(assets, _scannedDropDatabases);
        AddMainAssets(assets, _scannedBossEncounterTables);
        AddMainAssets(assets, _scannedSpawnPrefabs);
        AddMainAsset(assets, _primaryDropDatabase);
        AddMainAsset(assets, _primaryBossEncounterTable);
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

    private static bool IsActiveSceneDirty()
    {
        Scene scene = SceneManager.GetActiveScene();
        return scene.IsValid() && scene.isDirty;
    }

    private void SaveActiveScene()
    {
        QueueSaveAfterFocusFlush(SaveActiveSceneCore);
    }

    private void SaveActiveSceneCore()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isDirty)
            return;

        bool saveScene = EditorUtility.DisplayDialog(
            "씬 저장 확인",
            "현재 활성 씬에 저장되지 않은 변경이 있습니다.\n" +
            "이 씬의 무관한 미저장 변경도 함께 저장됩니다.\n\n" +
            "지금 Save Scene을 실행할까요?",
            "Save Scene",
            "나중에");
        if (!saveScene)
            return;

        bool saved = EditorSceneManager.SaveScene(scene);
        Debug.Log(
            "[EnemyDashboardWindow] Scene save: " +
            (saved ? "saved" : "failed") + ".");
        Repaint();
    }

    private void QueueSaveAfterFocusFlush(Action saveAction)
    {
        if (_saveActionPending)
            return;

        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;

        _saveActionPending = true;
        _queuedSaveAction = saveAction;
        EditorApplication.delayCall += ExecuteQueuedSaveAction;
        Repaint();
    }

    private void ExecuteQueuedSaveAction()
    {
        EditorApplication.delayCall -= ExecuteQueuedSaveAction;

        Action saveAction = _queuedSaveAction;
        _queuedSaveAction = null;
        _saveActionPending = false;

        if (this == null)
            return;

        saveAction?.Invoke();
    }

    private void DrawNewEnemyPanel()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("New Enemy", EditorStyles.boldLabel);

        EnemyPoolManager manager = Object.FindAnyObjectByType<EnemyPoolManager>();
        if (!CanCreateInPoolScene(manager))
        {
            EditorGUILayout.HelpBox("Main 씬을 열어야 생성 가능", MessageType.Warning);
            DrawNewEnemyUndoNotice();
            EditorGUILayout.EndVertical();
            return;
        }

        List<EnemyController> prefabOptions = BuildUniquePoolPrefabs(manager);
        EditorGUI.BeginChangeCheck();
        _newEnemyTemplate = (EnemyData)EditorGUILayout.ObjectField("템플릿", _newEnemyTemplate, typeof(EnemyData), false);
        _newEnemyName = EditorGUILayout.TextField("이름", _newEnemyName);
        bool createAsBoss = EditorGUILayout.Toggle("보스로 생성", _newEnemyCreateAsBoss);
        if (createAsBoss && !_newEnemyCreateAsBoss)
            CopyBossDefaultsToForm();
        _newEnemyCreateAsBoss = createAsBoss;

        if (prefabOptions.Count > 0)
        {
            _newEnemyPrefabIndex = Mathf.Clamp(_newEnemyPrefabIndex, 0, prefabOptions.Count - 1);
            _newEnemyPrefabIndex = EditorGUILayout.Popup("프리팹", _newEnemyPrefabIndex, BuildPrefabOptionLabels(prefabOptions));
        }
        else
        {
            _newEnemyPrefabIndex = 0;
            EditorGUILayout.LabelField("프리팹", "EnemyPoolManager entries에 사용 가능한 프리팹 없음");
        }

        if (_newEnemyCreateAsBoss)
            DrawBossCreationFields();

        if (EditorGUI.EndChangeCheck())
            _newEnemyFeedback = string.Empty;

        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        CollectNewEnemyValidation(manager, prefabOptions, errors, warnings, out _, out _);
        DrawValidationMessages(errors, MessageType.Error);
        DrawValidationMessages(warnings, MessageType.Warning);

        if (!string.IsNullOrWhiteSpace(_newEnemyFeedback))
            EditorGUILayout.HelpBox(_newEnemyFeedback, _newEnemyFeedbackType);

        EditorGUI.BeginDisabledGroup(errors.Count > 0);
        if (GUILayout.Button("생성", GUILayout.Width(96f)))
            ExecuteNewEnemyCreation(manager, prefabOptions);
        EditorGUI.EndDisabledGroup();

        DrawNewEnemyUndoNotice();
        EditorGUILayout.EndVertical();
    }

    private void DrawBossCreationFields()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        _newBossFloor = EditorGUILayout.IntField("floor", Mathf.Max(1, _newBossFloor));
        _newBossAreaDestinationId = EditorGUILayout.TextField("bossAreaDestinationId", _newBossAreaDestinationId);
        _newBossAreaId = EditorGUILayout.TextField("areaId", _newBossAreaId);
        _newBossIsFinal = EditorGUILayout.Toggle("isFinal", _newBossIsFinal);
        EditorGUILayout.EndVertical();
    }

    private void ExecuteNewEnemyCreation(EnemyPoolManager manager, List<EnemyController> prefabOptions)
    {
        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        CollectNewEnemyValidation(manager, prefabOptions, errors, warnings, out string enemyName, out string targetPath);
        if (errors.Count > 0)
        {
            _newEnemyFeedback = string.Join("\n", errors);
            _newEnemyFeedbackType = MessageType.Error;
            return;
        }

        EnemyDropDatabase dropDatabase = ResolvePrimaryDropDatabase();
        if (dropDatabase == null)
        {
            _newEnemyFeedback = "EnemyDropDatabase asset not found.";
            _newEnemyFeedbackType = MessageType.Error;
            return;
        }

        Undo.SetCurrentGroupName("Create Enemy Dashboard Enemy");
        int undoGroup = Undo.GetCurrentGroup();
        bool createAsBoss = _newEnemyCreateAsBoss;
        EnemyController prefab = prefabOptions[_newEnemyPrefabIndex];
        string templatePath = AssetDatabase.GetAssetPath(_newEnemyTemplate);
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            _newEnemyFeedback = "템플릿 에셋 경로를 찾을 수 없음.";
            _newEnemyFeedbackType = MessageType.Error;
            return;
        }

        if (!AssetDatabase.CopyAsset(templatePath, targetPath))
        {
            _newEnemyFeedback = "EnemyData 에셋 복제 실패: " + targetPath;
            _newEnemyFeedbackType = MessageType.Error;
            return;
        }

        AssetDatabase.ImportAsset(targetPath);
        EnemyData createdEnemy = AssetDatabase.LoadAssetAtPath<EnemyData>(targetPath);
        if (createdEnemy == null)
        {
            AssetDatabase.DeleteAsset(targetPath);
            _newEnemyFeedback = "복제된 EnemyData 로드 실패. 생성 에셋 롤백.";
            _newEnemyFeedbackType = MessageType.Error;
            return;
        }

        if (!TrySetCreatedEnemyName(createdEnemy, enemyName, out string error))
        {
            AssetDatabase.DeleteAsset(targetPath);
            _newEnemyFeedback = error + " 생성 에셋 롤백.";
            _newEnemyFeedbackType = MessageType.Error;
            return;
        }

        if (!TryAppendPoolEntry(manager, createdEnemy, prefab, out int poolIndex, out error))
        {
            AssetDatabase.DeleteAsset(targetPath);
            _newEnemyFeedback = error + " 생성 에셋 롤백.";
            _newEnemyFeedbackType = MessageType.Error;
            return;
        }

        RoomSpawner roomSpawner = Object.FindAnyObjectByType<RoomSpawner>();
        int spawnTableIndex = -1;
        string spawnTablePropertyName = string.Empty;
        string spawnTableMessage = string.Empty;
        BossEncounterTable bossTable = null;
        int bossEntryIndex = -1;

        if (createAsBoss)
        {
            if (!TryAppendBossEncounterEntry(createdEnemy, out bossTable, out bossEntryIndex, out error))
            {
                TryRemovePoolEntryAt(manager, poolIndex, out _);
                AssetDatabase.DeleteAsset(targetPath);
                _newEnemyFeedback = error + " 풀/생성 에셋 롤백.";
                _newEnemyFeedbackType = MessageType.Error;
                return;
            }
        }
        else if (!TryAppendSpawnTableEntry(roomSpawner, createdEnemy, out spawnTablePropertyName, out spawnTableIndex, out spawnTableMessage, out error))
        {
            TryRemovePoolEntryAt(manager, poolIndex, out _);
            AssetDatabase.DeleteAsset(targetPath);
            _newEnemyFeedback = error + " 풀/생성 에셋 롤백.";
            _newEnemyFeedbackType = MessageType.Error;
            return;
        }

        if (!TryCreateDropGroup(createdEnemy, out _, out error))
        {
            if (bossEntryIndex >= 0)
                TryRemoveBossEncounterEntryAt(bossTable, bossEntryIndex, out _);
            if (spawnTableIndex >= 0)
                TryRemoveRoomSpawnerTableEntryAt(roomSpawner, spawnTablePropertyName, spawnTableIndex, out _);
            TryRemovePoolEntryAt(manager, poolIndex, out _);
            AssetDatabase.DeleteAsset(targetPath);
            _newEnemyFeedback = error + " 스폰/보스/풀/생성 에셋 롤백.";
            _newEnemyFeedbackType = MessageType.Error;
            return;
        }

        Undo.CollapseUndoOperations(undoGroup);
        Scan();
        _newEnemyTemplate = null;
        _newEnemyName = string.Empty;
        _newEnemyPrefabIndex = 0;
        _newEnemyCreateAsBoss = false;
        _newBossFloor = 1;
        _newBossAreaDestinationId = string.Empty;
        _newBossAreaId = string.Empty;
        _newBossIsFinal = false;
        _newEnemyFeedback = "생성 완료: " + enemyName + ". Undo 대신 에셋 삭제+Rescan 권장. 씬 저장은 상단 Save Scene.";
        if (!string.IsNullOrWhiteSpace(spawnTableMessage))
            _newEnemyFeedback += "\n" + spawnTableMessage;
        if (warnings.Count > 0)
            _newEnemyFeedback += "\n" + string.Join("\n", warnings);
        _newEnemyFeedbackType = warnings.Count > 0 ? MessageType.Warning : MessageType.Info;
        EditorGUIUtility.PingObject(createdEnemy);
        Selection.activeObject = createdEnemy;
    }

    private void CollectNewEnemyValidation(
        EnemyPoolManager manager,
        List<EnemyController> prefabOptions,
        List<string> errors,
        List<string> warnings,
        out string enemyName,
        out string targetPath)
    {
        enemyName = (_newEnemyName ?? string.Empty).Trim();
        targetPath = GetNewEnemyAssetPath(enemyName);

        if (_newEnemyTemplate == null)
            errors.Add("템플릿 선택 필요.");

        if (!CanCreateInPoolScene(manager))
            errors.Add("Main 씬을 열어야 생성 가능.");

        if (prefabOptions == null || prefabOptions.Count == 0)
            errors.Add("EnemyPoolManager entries에 사용 가능한 프리팹 없음.");
        else if (_newEnemyPrefabIndex < 0 || _newEnemyPrefabIndex >= prefabOptions.Count || prefabOptions[_newEnemyPrefabIndex] == null)
            errors.Add("프리팹 선택 필요.");

        RoomSpawner roomSpawner = Object.FindAnyObjectByType<RoomSpawner>();
        if (!_newEnemyCreateAsBoss && roomSpawner == null)
            errors.Add("RoomSpawner가 씬에 없어 스폰 테이블 등록 불가.");

        if (_newEnemyCreateAsBoss)
        {
            BossEncounterTable bossTable = ResolvePrimaryBossEncounterTable();
            if (bossTable == null)
                errors.Add("BossEncounterTable 에셋 없음.");
            if (_newBossFloor < 1)
                errors.Add("boss floor는 1 이상 필요.");
            else if (bossTable != null && HasBossFloor(bossTable, _newBossFloor))
                errors.Add("BossEncounterTable floor 중복: " + _newBossFloor);
        }

        if (string.IsNullOrWhiteSpace(enemyName))
        {
            errors.Add("이름 입력 필요.");
            return;
        }

        if ((_newEnemyName ?? string.Empty) != enemyName)
            errors.Add("이름 앞뒤 공백 제거 필요.");

        if (HasInvalidFileNameCharacter(enemyName, out char invalidCharacter))
            errors.Add("이름에 파일명 부적합 문자 포함: " + invalidCharacter);

        if (!Directory.Exists(GetProjectRelativeFullPath(NewEnemyAssetFolder)))
            errors.Add("EnemyData 폴더 없음: " + NewEnemyAssetFolder);

        if (File.Exists(GetProjectRelativeFullPath(targetPath)))
            errors.Add("이미 존재하는 EnemyData 에셋: " + targetPath);

        if (TryFindScannedDuplicateEnemyName(enemyName, targetPath, out string duplicatePath))
            warnings.Add("동명 EnemyData 에셋 다른 경로에 존재: " + duplicatePath);
    }

    private static void DrawValidationMessages(List<string> messages, MessageType type)
    {
        for (int i = 0; i < messages.Count; i++)
            EditorGUILayout.HelpBox(messages[i], type);
    }

    private static void DrawNewEnemyUndoNotice()
    {
        EditorGUILayout.LabelField("에셋 생성은 Undo 불가. 생성 직후 Undo 대신 에셋 삭제+Rescan 권장. 씬 저장은 상단 Save Scene.", EditorStyles.miniLabel);
    }

    private static List<EnemyController> BuildUniquePoolPrefabs(EnemyPoolManager manager)
    {
        List<EnemyController> prefabs = new List<EnemyController>();
        if (manager == null)
            return prefabs;

        HashSet<EnemyController> seen = new HashSet<EnemyController>();
        SerializedObject managerObject = new SerializedObject(manager);
        SerializedProperty entries = managerObject.FindProperty("entries");
        if (entries == null || !entries.isArray)
            return prefabs;

        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            EnemyController prefab = GetObject<EnemyController>(entry.FindPropertyRelative("prefab"));
            if (prefab != null && seen.Add(prefab))
                prefabs.Add(prefab);
        }

        return prefabs;
    }

    private static string[] BuildPrefabOptionLabels(List<EnemyController> prefabOptions)
    {
        string[] labels = new string[prefabOptions.Count];
        for (int i = 0; i < prefabOptions.Count; i++)
            labels[i] = prefabOptions[i] != null ? prefabOptions[i].name : "<null>";

        return labels;
    }

    private static bool TrySetCreatedEnemyName(EnemyData enemy, string enemyName, out string error)
    {
        error = string.Empty;
        if (enemy == null)
        {
            error = "복제된 EnemyData가 null입니다.";
            return false;
        }

        SerializedObject enemyObject = new SerializedObject(enemy);
        enemyObject.Update();
        SerializedProperty enemyNameProperty = enemyObject.FindProperty(nameof(EnemyData.enemyName));
        if (enemyNameProperty == null)
        {
            error = "EnemyData.enemyName 필드를 찾을 수 없음.";
            return false;
        }

        SerializedProperty objectNameProperty = enemyObject.FindProperty("m_Name");
        if (objectNameProperty != null)
            objectNameProperty.stringValue = enemyName;
        enemyNameProperty.stringValue = enemyName;
        enemyObject.ApplyModifiedProperties();
        return true;
    }

    private void CopyBossDefaultsToForm()
    {
        BossEncounterTable table = ResolvePrimaryBossEncounterTable();
        if (table == null || table.Entries == null || table.Entries.Count == 0)
            return;

        BossEncounterEntry entry = table.Entries[0];
        if (entry == null)
            return;

        _newBossAreaDestinationId = entry.BossAreaDestinationId ?? string.Empty;
        _newBossAreaId = entry.AreaId ?? string.Empty;
        if (_newBossFloor < 1)
            _newBossFloor = 1;
    }

    private static bool CanCreateInPoolScene(EnemyPoolManager manager)
    {
        return manager != null && string.Equals(manager.gameObject.scene.name, "Main", StringComparison.Ordinal);
    }

    private static bool TryAppendPoolEntry(
        EnemyPoolManager manager,
        EnemyData enemy,
        EnemyController prefab,
        out int index,
        out string error)
    {
        index = -1;
        error = string.Empty;

        if (manager == null)
        {
            error = "EnemyPoolManager 없음.";
            return false;
        }

        SerializedObject managerObject = new SerializedObject(manager);
        managerObject.Update();
        SerializedProperty entries = managerObject.FindProperty("entries");
        if (entries == null || !entries.isArray)
        {
            error = "EnemyPoolManager.entries를 찾을 수 없음.";
            return false;
        }

        index = entries.arraySize;
        entries.InsertArrayElementAtIndex(index);
        SerializedProperty entry = entries.GetArrayElementAtIndex(index);
        SerializedProperty dataProperty = entry.FindPropertyRelative("data");
        SerializedProperty prefabProperty = entry.FindPropertyRelative("prefab");
        SerializedProperty preloadCountProperty = entry.FindPropertyRelative("preloadCount");
        if (dataProperty == null || prefabProperty == null || preloadCountProperty == null)
        {
            error = "EnemyPoolManager entry 필드(data/prefab/preloadCount)를 찾을 수 없음.";
            return false;
        }

        dataProperty.objectReferenceValue = enemy;
        prefabProperty.objectReferenceValue = prefab;
        preloadCountProperty.intValue = 0;

        if (!managerObject.ApplyModifiedProperties())
        {
            error = "풀 엔트리 변경 적용 실패.";
            return false;
        }

        return true;
    }

    private static bool TryRemovePoolEntryAt(EnemyPoolManager manager, int index, out string error)
    {
        error = string.Empty;
        if (manager == null || index < 0)
        {
            error = "롤백할 풀 엔트리 없음.";
            return false;
        }

        SerializedObject managerObject = new SerializedObject(manager);
        managerObject.Update();
        SerializedProperty entries = managerObject.FindProperty("entries");
        if (entries == null || !entries.isArray || index >= entries.arraySize)
        {
            error = "롤백 대상 풀 엔트리 인덱스가 유효하지 않음.";
            return false;
        }

        int previousSize = entries.arraySize;
        entries.DeleteArrayElementAtIndex(index);
        if (entries.arraySize == previousSize)
            entries.DeleteArrayElementAtIndex(index);

        managerObject.ApplyModifiedProperties();
        return true;
    }

    private static bool TryAppendSpawnTableEntry(
        RoomSpawner spawner,
        EnemyData enemy,
        out string propertyName,
        out int index,
        out string message,
        out string error)
    {
        propertyName = string.Empty;
        index = -1;
        message = string.Empty;
        error = string.Empty;

        if (spawner == null)
        {
            error = "RoomSpawner가 씬에 없음.";
            return false;
        }

        propertyName = enemy != null && enemy.IsElite ? "eliteRoomEnemyTable" : "enemyTable";
        SerializedObject spawnerObject = new SerializedObject(spawner);
        spawnerObject.Update();
        SerializedProperty table = spawnerObject.FindProperty(propertyName);
        if (table == null || !table.isArray)
        {
            error = "RoomSpawner." + propertyName + "를 찾을 수 없음.";
            return false;
        }

        if (propertyName == "enemyTable" && table.arraySize == 0)
        {
            message = "풀 폴백 모드 — 스폰 테이블 등록 생략";
            return true;
        }

        for (int i = 0; i < table.arraySize; i++)
        {
            if (GetObject<EnemyData>(table.GetArrayElementAtIndex(i)) == enemy)
            {
                message = "스폰 테이블에 이미 등록됨: " + propertyName;
                return true;
            }
        }

        index = table.arraySize;
        table.InsertArrayElementAtIndex(index);
        SerializedProperty element = table.GetArrayElementAtIndex(index);
        element.objectReferenceValue = enemy;

        if (!spawnerObject.ApplyModifiedProperties())
        {
            error = "RoomSpawner." + propertyName + " 변경 적용 실패.";
            return false;
        }

        message = "스폰 테이블 등록: " + propertyName;
        return true;
    }

    private static bool TryRemoveRoomSpawnerTableEntryAt(RoomSpawner spawner, string propertyName, int index, out string error)
    {
        error = string.Empty;
        if (spawner == null || string.IsNullOrWhiteSpace(propertyName) || index < 0)
        {
            error = "롤백할 스폰 테이블 엔트리 없음.";
            return false;
        }

        SerializedObject spawnerObject = new SerializedObject(spawner);
        spawnerObject.Update();
        SerializedProperty table = spawnerObject.FindProperty(propertyName);
        if (table == null || !table.isArray || index >= table.arraySize)
        {
            error = "롤백 대상 스폰 테이블 인덱스가 유효하지 않음.";
            return false;
        }

        DeleteArrayElement(table, index);
        spawnerObject.ApplyModifiedProperties();
        return true;
    }

    private bool TryAppendBossEncounterEntry(
        EnemyData boss,
        out BossEncounterTable table,
        out int index,
        out string error)
    {
        table = ResolvePrimaryBossEncounterTable();
        index = -1;
        error = string.Empty;

        if (table == null)
        {
            error = "BossEncounterTable 에셋 없음.";
            return false;
        }

        SerializedObject tableObject = new SerializedObject(table);
        tableObject.Update();
        SerializedProperty entries = tableObject.FindProperty("entries");
        if (entries == null || !entries.isArray)
        {
            error = "BossEncounterTable.entries를 찾을 수 없음.";
            return false;
        }

        index = entries.arraySize;
        entries.InsertArrayElementAtIndex(index);
        SerializedProperty entry = entries.GetArrayElementAtIndex(index);
        SerializedProperty floor = entry.FindPropertyRelative("floor");
        SerializedProperty bossProperty = entry.FindPropertyRelative("boss");
        SerializedProperty bossAreaDestinationId = entry.FindPropertyRelative("bossAreaDestinationId");
        SerializedProperty areaId = entry.FindPropertyRelative("areaId");
        SerializedProperty isFinal = entry.FindPropertyRelative("isFinal");
        if (floor == null || bossProperty == null || bossAreaDestinationId == null || areaId == null || isFinal == null)
        {
            error = "BossEncounterTable entry 필드(floor/boss/bossAreaDestinationId/areaId/isFinal)를 찾을 수 없음.";
            return false;
        }

        floor.intValue = Mathf.Max(1, _newBossFloor);
        bossProperty.objectReferenceValue = boss;
        bossAreaDestinationId.stringValue = _newBossAreaDestinationId ?? string.Empty;
        areaId.stringValue = _newBossAreaId ?? string.Empty;
        isFinal.boolValue = _newBossIsFinal;

        if (!tableObject.ApplyModifiedProperties())
        {
            error = "BossEncounterTable 변경 적용 실패.";
            return false;
        }

        return true;
    }

    private static bool TryRemoveBossEncounterEntryAt(BossEncounterTable table, int index, out string error)
    {
        error = string.Empty;
        if (table == null || index < 0)
        {
            error = "롤백할 보스 엔트리 없음.";
            return false;
        }

        SerializedObject tableObject = new SerializedObject(table);
        tableObject.Update();
        SerializedProperty entries = tableObject.FindProperty("entries");
        if (entries == null || !entries.isArray || index >= entries.arraySize)
        {
            error = "롤백 대상 보스 엔트리 인덱스가 유효하지 않음.";
            return false;
        }

        DeleteArrayElement(entries, index);
        tableObject.ApplyModifiedProperties();
        return true;
    }

    private EnemyDropDatabase ResolvePrimaryDropDatabase()
    {
        if (_primaryDropDatabase != null)
            return _primaryDropDatabase;

        List<EnemyDropDatabase> databases = LoadAssets<EnemyDropDatabase>("t:EnemyDropDatabase");
        _primaryDropDatabase = databases.Count > 0 ? databases[0] : null;
        return _primaryDropDatabase;
    }

    private BossEncounterTable ResolvePrimaryBossEncounterTable()
    {
        if (_primaryBossEncounterTable != null)
            return _primaryBossEncounterTable;

        if (_bossTableLookupAttempted)
            return null;

        _bossTableLookupAttempted = true;
        List<BossEncounterTable> tables = LoadAssets<BossEncounterTable>("t:BossEncounterTable");
        _primaryBossEncounterTable = tables.Count > 0 ? tables[0] : null;
        return _primaryBossEncounterTable;
    }

    private bool TryFindScannedDuplicateEnemyName(string enemyName, string targetPath, out string duplicatePath)
    {
        duplicatePath = string.Empty;
        if (!_hasScanned)
            return false;

        for (int i = 0; i < _rows.Count; i++)
        {
            EnemyData enemy = _rows[i].Data;
            if (enemy == null)
                continue;

            string path = AssetDatabase.GetAssetPath(enemy);
            if (string.Equals(path, targetPath, StringComparison.Ordinal))
                continue;

            if (string.Equals(enemy.enemyName, enemyName, StringComparison.Ordinal) ||
                string.Equals(enemy.name, enemyName, StringComparison.Ordinal))
            {
                duplicatePath = path;
                return true;
            }
        }

        return false;
    }

    private static bool HasInvalidFileNameCharacter(string value, out char invalidCharacter)
    {
        invalidCharacter = '\0';
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '/' || c == '\\' || Array.IndexOf(invalidCharacters, c) >= 0)
            {
                invalidCharacter = c;
                return true;
            }
        }

        return false;
    }

    private static string GetNewEnemyAssetPath(string enemyName)
    {
        return NewEnemyAssetFolder + "/" + enemyName + ".asset";
    }

    private static string GetProjectRelativeFullPath(string assetPath)
    {
        string normalizedPath = assetPath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(Directory.GetCurrentDirectory(), normalizedPath);
    }

    private void DrawSummary()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Enemies: " + _rows.Count, EditorStyles.boldLabel, GUILayout.Width(120f));
        EditorGUILayout.LabelField("Warnings: " + _warnings.Count, EditorStyles.boldLabel, GUILayout.Width(140f));
        if (!_hasPoolScene)
            EditorGUILayout.LabelField("EnemyPoolManager: 씬 없음", EditorStyles.miniLabel, GUILayout.Width(190f));
        if (_hasAssetChanges)
            EditorGUILayout.LabelField("값 변경됨 — Rescan 권장", EditorStyles.miniBoldLabel, GUILayout.Width(180f));
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrWhiteSpace(_operationFeedback))
            EditorGUILayout.HelpBox(_operationFeedback, _operationFeedbackType);
    }

    private void DrawRowsPanel()
    {
        _rowScrollPosition = EditorGUILayout.BeginScrollView(_rowScrollPosition, true, true, GUILayout.ExpandHeight(true));
        if (_rows.Count == 0)
        {
            EditorGUILayout.HelpBox("No EnemyData assets found.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawHeader();
        for (int i = 0; i < _rows.Count; i++)
            DrawRow(_rows[i]);
        EditorGUILayout.EndScrollView();
    }

    private void DrawRankDropsPanel()
    {
        EnemyDropDatabase database = _primaryDropDatabase;
        if (database == null)
        {
            EditorGUILayout.HelpBox("EnemyDropDatabase asset not found. Scan 먼저.", MessageType.Warning);
            return;
        }

        _rankScroll = EditorGUILayout.BeginScrollView(_rankScroll, true, true, GUILayout.ExpandHeight(true));

        SerializedObject databaseObject = new SerializedObject(database);
        databaseObject.Update();

        bool changed = false;
        changed |= DrawRankGroupSection(databaseObject, "normalRankDrops", "일반 (Normal)");
        changed |= DrawRankGroupSection(databaseObject, "eliteRankDrops", "엘리트 (Elite)");
        changed |= DrawRankGroupSection(databaseObject, "bossRankDrops", "보스 (Boss)");
        if (changed)
            ApplyAndMark(databaseObject);

        EditorGUILayout.EndScrollView();
    }

    private bool DrawRankGroupSection(SerializedObject databaseObject, string propertyName, string header)
    {
        SerializedProperty group = databaseObject.FindProperty(propertyName);
        if (group == null)
        {
            EditorGUILayout.HelpBox(propertyName + " missing.", MessageType.Error);
            return false;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(
            header + " 공유 드롭 (" + databaseObject.targetObject.name + ")",
            EditorStyles.boldLabel);

        bool changed = false;
        changed |= DrawDrops(group.FindPropertyRelative("drops"));
        changed |= DrawChoiceGroups(group.FindPropertyRelative("choiceGroups"));
        changed |= DrawQueries(group.FindPropertyRelative("queries"));

        EditorGUILayout.EndVertical();
        return changed;
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        Header("", FoldoutWidth);
        Header("!", WarningWidth);
        Header("Name", NameWidth);
        Header("HP", StatWidth);
        Header("Atk", StatWidth);
        Header("Def", StatWidth);
        Header("EXP", StatWidth);
        Header("Move", MoveWidth);
        Header("MinF", FloorEditWidth);
        Header("MaxF", FloorEditWidth);
        Header("Cost", CostWidth);
        Header("Type", TypeWidth);
        Header("Prefab", PrefabWidth);
        Header("Boss", BossWidth);
        Header("Drops", DropWidth);
        Header("", DeleteWidth);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRow(EnemyRow row)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        Rect foldoutRect = GUILayoutUtility.GetRect(FoldoutWidth, EditorGUIUtility.singleLineHeight, GUILayout.Width(FoldoutWidth));
        row.DropFoldout = EditorGUI.Foldout(foldoutRect, row.DropFoldout, GUIContent.none, true);

        if (row.Warnings.Count > 0)
            GUILayout.Label(GetSeverityIcon(row.HighestSeverity, row.WarningTooltip), GUILayout.Width(WarningWidth), GUILayout.Height(20f));
        else
            GUILayout.Label(GUIContent.none, GUILayout.Width(WarningWidth), GUILayout.Height(20f));

        if (GUILayout.Button(row.DisplayName, GUILayout.Width(NameWidth)))
            EditorGUIUtility.PingObject(row.Data);

        DrawEnemyDataEditors(row);
        Cell(row.TypeSummary, TypeWidth);
        DrawPrefabCell(row);
        Cell(row.IsBoss ? "Boss" : "-", BossWidth);
        Cell(row.DropSummary, DropWidth);
        DrawDeleteCell(row);

        EditorGUILayout.EndHorizontal();

        if (row.DropFoldout)
            DrawDropEditor(row);

        EditorGUILayout.EndVertical();
    }

    private void DrawDeleteCell(EnemyRow row)
    {
        EnemyPoolManager manager = Object.FindAnyObjectByType<EnemyPoolManager>();
        EditorGUI.BeginDisabledGroup(!CanCreateInPoolScene(manager));
        if (GUILayout.Button("삭제", GUILayout.Width(DeleteWidth)))
            ConfirmAndDeleteEnemy(row.Data);
        EditorGUI.EndDisabledGroup();
    }

    private void ConfirmAndDeleteEnemy(EnemyData enemy)
    {
        if (enemy == null)
            return;

        DeleteAnalysis analysis = BuildDeleteAnalysis(enemy);
        string body = BuildDeleteDialogBody(analysis);
        bool deletePrefab = false;

        if (analysis.CanDeletePrefab)
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "적 삭제 확인",
                body,
                "적만 삭제",
                "취소",
                "적+프리팹 삭제");
            if (choice == 1)
                return;

            deletePrefab = choice == 2;
        }
        else
        {
            if (!EditorUtility.DisplayDialog("적 삭제 확인", body, "삭제", "취소"))
                return;
        }

        ExecuteEnemyDeletion(analysis, deletePrefab);
    }

    private DeleteAnalysis BuildDeleteAnalysis(EnemyData enemy)
    {
        DeleteAnalysis analysis = new DeleteAnalysis(enemy);
        analysis.EnemyAssetPath = AssetDatabase.GetAssetPath(enemy);

        EnemyPoolManager poolManager = Object.FindAnyObjectByType<EnemyPoolManager>();
        analysis.PoolManager = poolManager;
        if (poolManager != null)
            AnalyzePoolEntries(analysis, poolManager);

        RoomSpawner spawner = Object.FindAnyObjectByType<RoomSpawner>();
        analysis.RoomSpawner = spawner;
        if (spawner != null)
            analysis.SpawnTableCount = CountRoomSpawnerReferences(spawner, enemy);

        List<EnemyDropDatabase> dropDatabases = LoadAssets<EnemyDropDatabase>("t:EnemyDropDatabase");
        for (int i = 0; i < dropDatabases.Count; i++)
            analysis.DropGroupCount += CountRelativeReferences(dropDatabases[i], "groups", "enemy", enemy);

        List<BossEncounterTable> bossTables = LoadAssets<BossEncounterTable>("t:BossEncounterTable");
        for (int i = 0; i < bossTables.Count; i++)
            analysis.BossEntryCount += CountRelativeReferences(bossTables[i], "entries", "boss", enemy);

        AnalyzePrefabOwnership(analysis);
        return analysis;
    }

    private static void AnalyzePoolEntries(DeleteAnalysis analysis, EnemyPoolManager poolManager)
    {
        SerializedObject managerObject = new SerializedObject(poolManager);
        SerializedProperty entries = managerObject.FindProperty("entries");
        if (entries == null || !entries.isArray)
            return;

        List<PoolEntrySnapshot> snapshots = new List<PoolEntrySnapshot>(entries.arraySize);
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            EnemyData data = GetObject<EnemyData>(entry.FindPropertyRelative("data"));
            EnemyController prefab = GetObject<EnemyController>(entry.FindPropertyRelative("prefab"));
            snapshots.Add(new PoolEntrySnapshot(data, prefab));

            if (data == analysis.Target)
            {
                analysis.PoolEntryCount++;
                if (prefab != null && !analysis.TargetPrefabs.Contains(prefab))
                    analysis.TargetPrefabs.Add(prefab);
            }
        }

        for (int i = 0; i < snapshots.Count; i++)
        {
            PoolEntrySnapshot snapshot = snapshots[i];
            if (snapshot.Data == analysis.Target || snapshot.Prefab == null)
                continue;

            if (analysis.TargetPrefabs.Contains(snapshot.Prefab))
                analysis.SharedPrefabs.Add(snapshot.Prefab);
        }
    }

    private static void AnalyzePrefabOwnership(DeleteAnalysis analysis)
    {
        for (int i = 0; i < analysis.TargetPrefabs.Count; i++)
        {
            EnemyController prefab = analysis.TargetPrefabs[i];
            if (prefab == null)
                continue;

            SerializedObject prefabObject = new SerializedObject(prefab);
            SerializedProperty data = prefabObject.FindProperty("data");
            if (GetObject<EnemyData>(data) == analysis.Target)
                analysis.OwnedPrefabs.Add(prefab);
        }
    }

    private static int CountRoomSpawnerReferences(RoomSpawner spawner, EnemyData enemy)
    {
        SerializedObject spawnerObject = new SerializedObject(spawner);
        return CountDirectReferences(spawnerObject.FindProperty("enemyTable"), enemy) +
               CountDirectReferences(spawnerObject.FindProperty("eliteRoomEnemyTable"), enemy);
    }

    private static int CountDirectReferences(SerializedProperty array, EnemyData enemy)
    {
        if (array == null || !array.isArray)
            return 0;

        int count = 0;
        for (int i = 0; i < array.arraySize; i++)
            if (GetObject<EnemyData>(array.GetArrayElementAtIndex(i)) == enemy)
                count++;

        return count;
    }

    private static int CountRelativeReferences(Object owner, string arrayName, string relativeName, EnemyData enemy)
    {
        if (owner == null)
            return 0;

        SerializedObject serializedObject = new SerializedObject(owner);
        SerializedProperty array = serializedObject.FindProperty(arrayName);
        if (array == null || !array.isArray)
            return 0;

        int count = 0;
        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty element = array.GetArrayElementAtIndex(i);
            if (GetObject<EnemyData>(element.FindPropertyRelative(relativeName)) == enemy)
                count++;
        }

        return count;
    }

    private static string BuildDeleteDialogBody(DeleteAnalysis analysis)
    {
        string body =
            analysis.DisplayName + " 삭제\n\n" +
            "풀 " + analysis.PoolEntryCount + "건 / " +
            "스폰테이블 " + analysis.SpawnTableCount + "건 / " +
            "드랍 그룹 " + analysis.DropGroupCount + "건 / " +
            "보스 엔트리 " + analysis.BossEntryCount + "건 / " +
            "에셋 " + (string.IsNullOrWhiteSpace(analysis.EnemyAssetPath) ? 0 : 1) + "건\n";

        if (analysis.TargetPrefabs.Count > 0)
            body += "프리팹 후보 " + analysis.TargetPrefabs.Count + "건 / 소유 " + analysis.OwnedPrefabs.Count + "건 / 공유 " + analysis.SharedPrefabs.Count + "건\n";

        if (analysis.SharedPrefabs.Count > 0)
            body += "프리팹 공유 중 — 프리팹은 유지됨\n";
        else if (analysis.TargetPrefabs.Count > 0 && analysis.OwnedPrefabs.Count == 0)
            body += "프리팹 비소유 — 프리팹은 유지됨\n";

        body += "\n에셋/프리팹 삭제는 Undo 불가, 엔트리 제거는 Undo 가능.";
        return body;
    }

    private void ExecuteEnemyDeletion(DeleteAnalysis analysis, bool deletePrefab)
    {
        List<string> failures = new List<string>();
        DeletionCounts removed = new DeletionCounts();

        Undo.SetCurrentGroupName("Delete Enemy Dashboard Enemy");
        int undoGroup = Undo.GetCurrentGroup();

        removed.DropGroups = RemoveEnemyDropGroups(analysis.Target, failures);
        removed.BossEntries = RemoveBossEntries(analysis.Target, failures);
        removed.SpawnEntries = RemoveRoomSpawnerEntries(analysis.RoomSpawner, analysis.Target, failures);
        removed.PoolEntries = RemovePoolEntries(analysis.PoolManager, analysis.Target, failures);

        if (!deletePrefab)
            removed.PrefabDataCleared = ClearOwnedPrefabData(analysis, failures);

        Undo.CollapseUndoOperations(undoGroup);

        bool enemyDeleted = false;
        if (string.IsNullOrWhiteSpace(analysis.EnemyAssetPath))
        {
            failures.Add("EnemyData 에셋 경로 없음");
        }
        else
        {
            enemyDeleted = AssetDatabase.DeleteAsset(analysis.EnemyAssetPath);
            if (!enemyDeleted)
                failures.Add("EnemyData 에셋 삭제 실패: " + analysis.EnemyAssetPath);
        }

        int prefabsDeleted = 0;
        if (deletePrefab && enemyDeleted)
            prefabsDeleted = DeleteOwnedPrefabs(analysis, failures);
        else if (deletePrefab)
            failures.Add("EnemyData 삭제 실패로 프리팹 삭제 생략");

        Scan();
        _operationFeedback = BuildDeletionFeedback(analysis, removed, enemyDeleted, prefabsDeleted, failures);
        _operationFeedbackType = failures.Count > 0 ? MessageType.Warning : MessageType.Info;
    }

    private static int RemoveEnemyDropGroups(EnemyData target, List<string> failures)
    {
        int removed = 0;
        List<EnemyDropDatabase> databases = LoadAssets<EnemyDropDatabase>("t:EnemyDropDatabase");
        for (int i = 0; i < databases.Count; i++)
            removed += RemoveRelativeReferences(databases[i], "groups", "enemy", target, "DropDB groups", failures);

        return removed;
    }

    private static int RemoveBossEntries(EnemyData target, List<string> failures)
    {
        int removed = 0;
        List<BossEncounterTable> tables = LoadAssets<BossEncounterTable>("t:BossEncounterTable");
        for (int i = 0; i < tables.Count; i++)
            removed += RemoveRelativeReferences(tables[i], "entries", "boss", target, "BossEncounterTable entries", failures);

        return removed;
    }

    private static int RemoveRelativeReferences(
        Object owner,
        string arrayName,
        string relativeName,
        EnemyData target,
        string label,
        List<string> failures)
    {
        if (owner == null)
            return 0;

        SerializedObject serializedObject = new SerializedObject(owner);
        serializedObject.Update();
        SerializedProperty array = serializedObject.FindProperty(arrayName);
        if (array == null || !array.isArray)
        {
            failures.Add(label + " 배열 없음: " + owner.name);
            return 0;
        }

        int removed = 0;
        for (int i = array.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty element = array.GetArrayElementAtIndex(i);
            if (GetObject<EnemyData>(element.FindPropertyRelative(relativeName)) != target)
                continue;

            DeleteArrayElement(array, i);
            removed++;
        }

        if (removed > 0 && !serializedObject.ApplyModifiedProperties())
            failures.Add(label + " 제거 적용 실패: " + owner.name);

        return removed;
    }

    private static int RemoveRoomSpawnerEntries(RoomSpawner spawner, EnemyData target, List<string> failures)
    {
        if (spawner == null)
        {
            failures.Add("RoomSpawner 없음");
            return 0;
        }

        SerializedObject spawnerObject = new SerializedObject(spawner);
        spawnerObject.Update();
        int removed = 0;
        removed += RemoveDirectReferences(spawnerObject.FindProperty("enemyTable"), target, "RoomSpawner.enemyTable", failures);
        removed += RemoveDirectReferences(spawnerObject.FindProperty("eliteRoomEnemyTable"), target, "RoomSpawner.eliteRoomEnemyTable", failures);

        if (removed > 0 && !spawnerObject.ApplyModifiedProperties())
            failures.Add("RoomSpawner 스폰 테이블 제거 적용 실패");

        return removed;
    }

    private static int RemovePoolEntries(EnemyPoolManager manager, EnemyData target, List<string> failures)
    {
        if (manager == null)
        {
            failures.Add("EnemyPoolManager 없음");
            return 0;
        }

        SerializedObject managerObject = new SerializedObject(manager);
        managerObject.Update();
        SerializedProperty entries = managerObject.FindProperty("entries");
        if (entries == null || !entries.isArray)
        {
            failures.Add("EnemyPoolManager.entries 없음");
            return 0;
        }

        int removed = 0;
        for (int i = entries.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            if (GetObject<EnemyData>(entry.FindPropertyRelative("data")) != target)
                continue;

            DeleteArrayElement(entries, i);
            removed++;
        }

        if (removed > 0 && !managerObject.ApplyModifiedProperties())
            failures.Add("EnemyPoolManager.entries 제거 적용 실패");

        return removed;
    }

    private static int RemoveDirectReferences(SerializedProperty array, EnemyData target, string label, List<string> failures)
    {
        if (array == null || !array.isArray)
        {
            failures.Add(label + " 배열 없음");
            return 0;
        }

        int removed = 0;
        for (int i = array.arraySize - 1; i >= 0; i--)
        {
            if (GetObject<EnemyData>(array.GetArrayElementAtIndex(i)) != target)
                continue;

            DeleteArrayElement(array, i);
            removed++;
        }

        return removed;
    }

    private static int ClearOwnedPrefabData(DeleteAnalysis analysis, List<string> failures)
    {
        int cleared = 0;
        for (int i = 0; i < analysis.OwnedPrefabs.Count; i++)
        {
            EnemyController prefab = analysis.OwnedPrefabs[i];
            if (prefab == null)
                continue;

            SerializedObject prefabObject = new SerializedObject(prefab);
            prefabObject.Update();
            SerializedProperty data = prefabObject.FindProperty("data");
            if (data == null)
            {
                failures.Add("프리팹 data 필드 없음: " + prefab.name);
                continue;
            }

            if (GetObject<EnemyData>(data) != analysis.Target)
                continue;

            data.objectReferenceValue = null;
            if (prefabObject.ApplyModifiedProperties())
                cleared++;
            else
                failures.Add("프리팹 data 클리어 실패: " + prefab.name);
        }

        return cleared;
    }

    private static int DeleteOwnedPrefabs(DeleteAnalysis analysis, List<string> failures)
    {
        int deleted = 0;
        for (int i = 0; i < analysis.OwnedPrefabs.Count; i++)
        {
            EnemyController prefab = analysis.OwnedPrefabs[i];
            if (prefab == null)
                continue;

            if (analysis.SharedPrefabs.Contains(prefab))
                continue;

            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrWhiteSpace(path))
            {
                failures.Add("프리팹 경로 없음: " + prefab.name);
                continue;
            }

            if (AssetDatabase.DeleteAsset(path))
                deleted++;
            else
                failures.Add("프리팹 삭제 실패: " + path);
        }

        return deleted;
    }

    private static string BuildDeletionFeedback(
        DeleteAnalysis analysis,
        DeletionCounts removed,
        bool enemyDeleted,
        int prefabsDeleted,
        List<string> failures)
    {
        string feedback =
            "삭제 완료: " + analysis.DisplayName +
            "\n풀 " + removed.PoolEntries +
            " / 스폰테이블 " + removed.SpawnEntries +
            " / 드랍 그룹 " + removed.DropGroups +
            " / 보스 엔트리 " + removed.BossEntries +
            " / 프리팹 data 클리어 " + removed.PrefabDataCleared +
            " / EnemyData 삭제 " + (enemyDeleted ? "성공" : "실패") +
            " / 프리팹 삭제 " + prefabsDeleted;

        if (failures.Count > 0)
            feedback += "\n일부 제거 실패: " + string.Join("; ", failures) + ". Rescan 후 수동 확인 요망";

        return feedback;
    }

    private void DrawEnemyDataEditors(EnemyRow row)
    {
        SerializedObject enemyObject = new SerializedObject(row.Data);
        enemyObject.Update();

        SerializedProperty maxHp = enemyObject.FindProperty(nameof(EnemyData.maxHp));
        SerializedProperty attack = enemyObject.FindProperty(nameof(EnemyData.attack));
        SerializedProperty defense = enemyObject.FindProperty(nameof(EnemyData.defense));
        SerializedProperty expReward = enemyObject.FindProperty(nameof(EnemyData.expReward));
        SerializedProperty moveSpeed = enemyObject.FindProperty(nameof(EnemyData.moveSpeed));
        SerializedProperty minFloor = enemyObject.FindProperty("minFloor");
        SerializedProperty maxFloor = enemyObject.FindProperty("maxFloor");
        SerializedProperty spawnCost = enemyObject.FindProperty(nameof(EnemyData.spawnCost));

        int nextMaxHp = maxHp != null ? maxHp.intValue : 0;
        int nextAttack = attack != null ? attack.intValue : 0;
        int nextDefense = defense != null ? defense.intValue : 0;
        int nextExpReward = expReward != null ? expReward.intValue : 0;
        float nextMoveSpeed = moveSpeed != null ? moveSpeed.floatValue : 0f;
        int nextMinFloor = minFloor != null ? minFloor.intValue : 1;
        int nextMaxFloor = maxFloor != null ? maxFloor.intValue : nextMinFloor;
        int nextSpawnCost = spawnCost != null ? spawnCost.intValue : 1;

        EditorGUI.BeginChangeCheck();
        nextMaxHp = EditorGUILayout.IntField(nextMaxHp, GUILayout.Width(StatWidth));
        nextAttack = EditorGUILayout.IntField(nextAttack, GUILayout.Width(StatWidth));
        nextDefense = EditorGUILayout.IntField(nextDefense, GUILayout.Width(StatWidth));
        nextExpReward = EditorGUILayout.IntField(nextExpReward, GUILayout.Width(StatWidth));
        nextMoveSpeed = EditorGUILayout.FloatField(nextMoveSpeed, GUILayout.Width(MoveWidth));
        nextMinFloor = EditorGUILayout.IntField(nextMinFloor, GUILayout.Width(FloorEditWidth));
        nextMaxFloor = EditorGUILayout.IntField(nextMaxFloor, GUILayout.Width(FloorEditWidth));
        nextSpawnCost = EditorGUILayout.IntField(nextSpawnCost, GUILayout.Width(CostWidth));

        if (!EditorGUI.EndChangeCheck())
            return;

        nextMaxHp = Mathf.Max(1, nextMaxHp);
        nextAttack = Mathf.Max(0, nextAttack);
        nextDefense = Mathf.Max(0, nextDefense);
        nextExpReward = Mathf.Max(0, nextExpReward);
        nextMoveSpeed = Mathf.Max(0f, nextMoveSpeed);
        nextMinFloor = Mathf.Max(1, nextMinFloor);
        nextMaxFloor = Mathf.Max(nextMinFloor, nextMaxFloor);
        nextSpawnCost = Mathf.Max(1, nextSpawnCost);

        if (maxHp != null) maxHp.intValue = nextMaxHp;
        if (attack != null) attack.intValue = nextAttack;
        if (defense != null) defense.intValue = nextDefense;
        if (expReward != null) expReward.intValue = nextExpReward;
        if (moveSpeed != null) moveSpeed.floatValue = nextMoveSpeed;
        if (minFloor != null) minFloor.intValue = nextMinFloor;
        if (maxFloor != null) maxFloor.intValue = nextMaxFloor;
        if (spawnCost != null) spawnCost.intValue = nextSpawnCost;

        ApplyAndMark(enemyObject);
    }

    private void DrawPrefabCell(EnemyRow row)
    {
        if (!_hasPoolScene)
        {
            Cell("씬 없음", PrefabWidth);
            return;
        }

        if (row.HasPoolEntry && row.Prefab != null)
        {
            if (GUILayout.Button(row.Prefab.name, GUILayout.Width(PrefabWidth)))
                EditorGUIUtility.PingObject(row.Prefab);
            return;
        }

        Cell("-", PrefabWidth);
    }

    private void DrawDropEditor(EnemyRow row)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(FoldoutWidth + WarningWidth);
        EditorGUILayout.BeginVertical();

        if (row.DropGroups.Count == 0)
        {
            DrawCreateDropGroup(row);
        }
        else
        {
            for (int i = 0; i < row.DropGroups.Count; i++)
                DrawDropGroup(row.DropGroups[i], i, row.Data);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawCreateDropGroup(EnemyRow row)
    {
        if (_primaryDropDatabase == null)
        {
            EditorGUILayout.HelpBox("EnemyDropDatabase asset not found.", MessageType.Warning);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("드랍 그룹 없음", GUILayout.Width(120f));
        if (GUILayout.Button("드랍 그룹 생성", GUILayout.Width(140f)))
            CreateDropGroup(row);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawDropGroup(DropGroupRecord record, int displayIndex, EnemyData expectedEnemy)
    {
        if (record.Database == null)
            return;

        SerializedObject databaseObject = new SerializedObject(record.Database);
        databaseObject.Update();

        SerializedProperty groups = databaseObject.FindProperty("groups");
        if (groups == null || !groups.isArray || record.GroupIndex < 0 || record.GroupIndex >= groups.arraySize)
        {
            EditorGUILayout.HelpBox("Drop group index stale. Rescan 권장.", MessageType.Warning);
            return;
        }

        SerializedProperty group = groups.GetArrayElementAtIndex(record.GroupIndex);
        if (GetObject<EnemyData>(group.FindPropertyRelative("enemy")) != expectedEnemy)
        {
            EditorGUILayout.HelpBox("Drop group index stale. Rescan 권장.", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("Drop Group " + displayIndex + " (" + record.Database.name + ")", EditorStyles.boldLabel);

        bool changed = false;
        changed |= DrawDrops(group.FindPropertyRelative("drops"));
        changed |= DrawChoiceGroups(group.FindPropertyRelative("choiceGroups"));
        changed |= DrawQueries(group.FindPropertyRelative("queries"));

        if (changed)
            ApplyAndMark(databaseObject);
    }

    private bool DrawDrops(SerializedProperty drops)
    {
        bool changed = false;
        EditorGUILayout.LabelField("drops[]", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("itemCode", EditorStyles.miniLabel, GUILayout.Width(170f));
        GUILayout.Space(20f);
        GUILayout.Label("min", EditorStyles.miniLabel, GUILayout.Width(58f));
        GUILayout.Label("max", EditorStyles.miniLabel, GUILayout.Width(58f));
        GUILayout.Label("chance", EditorStyles.miniLabel, GUILayout.Width(76f));
        EditorGUILayout.EndHorizontal();

        if (drops == null || !drops.isArray)
        {
            EditorGUILayout.HelpBox("drops[] missing.", MessageType.Error);
            return false;
        }

        for (int i = 0; i < drops.arraySize; i++)
        {
            SerializedProperty drop = drops.GetArrayElementAtIndex(i);
            SerializedProperty itemCode = drop.FindPropertyRelative("itemCode");
            SerializedProperty minAmount = drop.FindPropertyRelative("minAmount");
            SerializedProperty maxAmount = drop.FindPropertyRelative("maxAmount");
            SerializedProperty chance = drop.FindPropertyRelative("chance");

            EditorGUILayout.BeginHorizontal();
            changed |= DrawDropItemCode(itemCode, 170f);
            bool amountChanged = false;
            amountChanged |= DrawIntProperty(minAmount, "min", 1, int.MaxValue, 58f);
            int minValue = minAmount != null ? Mathf.Max(1, minAmount.intValue) : 1;
            amountChanged |= DrawIntProperty(maxAmount, "max", minValue, int.MaxValue, 58f);
            if (amountChanged)
                amountChanged |= NormalizeMinMax(minAmount, maxAmount);
            changed |= amountChanged;
            changed |= DrawFloatProperty(chance, "chance", 0f, 1f, 76f);

            if (GUILayout.Button("-", GUILayout.Width(24f)))
            {
                drops.DeleteArrayElementAtIndex(i);
                changed = true;
                EditorGUILayout.EndHorizontal();
                break;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16f);
        if (GUILayout.Button("+ drop", GUILayout.Width(80f)))
        {
            int index = drops.arraySize;
            drops.InsertArrayElementAtIndex(index);
            InitializeDrop(drops.GetArrayElementAtIndex(index));
            changed = true;
        }
        EditorGUILayout.EndHorizontal();

        return changed;
    }

    private bool DrawChoiceGroups(SerializedProperty choiceGroups)
    {
        bool changed = false;
        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("choiceGroups[]", EditorStyles.miniBoldLabel);

        if (choiceGroups == null || !choiceGroups.isArray)
        {
            EditorGUILayout.HelpBox("choiceGroups[] missing.", MessageType.Error);
            return false;
        }

        for (int groupIndex = 0; groupIndex < choiceGroups.arraySize; groupIndex++)
        {
            SerializedProperty choiceGroup = choiceGroups.GetArrayElementAtIndex(groupIndex);
            SerializedProperty chance = choiceGroup.FindPropertyRelative("chance");
            SerializedProperty choices = choiceGroup.FindPropertyRelative("choices");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("choice group " + groupIndex, GUILayout.Width(110f));
            GUILayout.FlexibleSpace();
            bool removed = GUILayout.Button("-", GUILayout.Width(24f));
            EditorGUILayout.EndHorizontal();
            if (removed)
            {
                choiceGroups.DeleteArrayElementAtIndex(groupIndex);
                changed = true;
                EditorGUILayout.EndVertical();
                break;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("chance", GUILayout.Width(45f));
            changed |= DrawFloatProperty(chance, "chance", 0f, 1f, 76f);
            EditorGUILayout.EndHorizontal();

            changed |= DrawChoices(choices);
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16f);
        if (GUILayout.Button("+ choice group", GUILayout.Width(120f)))
        {
            int index = choiceGroups.arraySize;
            choiceGroups.InsertArrayElementAtIndex(index);
            InitializeChoiceGroup(choiceGroups.GetArrayElementAtIndex(index));
            changed = true;
        }
        EditorGUILayout.EndHorizontal();

        return changed;
    }

    private bool DrawChoices(SerializedProperty choices)
    {
        bool changed = false;

        if (choices == null || !choices.isArray)
        {
            EditorGUILayout.HelpBox("choices[] missing.", MessageType.Error);
            return false;
        }

        for (int i = 0; i < choices.arraySize; i++)
        {
            SerializedProperty choice = choices.GetArrayElementAtIndex(i);
            SerializedProperty itemCode = choice.FindPropertyRelative("itemCode");
            SerializedProperty minAmount = choice.FindPropertyRelative("minAmount");
            SerializedProperty maxAmount = choice.FindPropertyRelative("maxAmount");
            SerializedProperty weight = choice.FindPropertyRelative("weight");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16f);
            changed |= DrawDropItemCode(itemCode, 154f);
            bool amountChanged = false;
            amountChanged |= DrawIntProperty(minAmount, "min", 1, int.MaxValue, 58f);
            int minValue = minAmount != null ? Mathf.Max(1, minAmount.intValue) : 1;
            amountChanged |= DrawIntProperty(maxAmount, "max", minValue, int.MaxValue, 58f);
            if (amountChanged)
                amountChanged |= NormalizeMinMax(minAmount, maxAmount);
            changed |= amountChanged;
            changed |= DrawFloatProperty(weight, "weight", 0f, float.MaxValue, 76f);

            if (GUILayout.Button("-", GUILayout.Width(24f)))
            {
                choices.DeleteArrayElementAtIndex(i);
                changed = true;
                EditorGUILayout.EndHorizontal();
                break;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(32f);
        if (GUILayout.Button("+ choice", GUILayout.Width(90f)))
        {
            int index = choices.arraySize;
            choices.InsertArrayElementAtIndex(index);
            InitializeChoice(choices.GetArrayElementAtIndex(index));
            changed = true;
        }
        EditorGUILayout.EndHorizontal();

        return changed;
    }

    private bool DrawQueries(SerializedProperty queries)
    {
        bool changed = false;
        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("queries[]", EditorStyles.miniBoldLabel);

        if (queries == null || !queries.isArray)
        {
            EditorGUILayout.HelpBox("queries[] missing.", MessageType.Error);
            return false;
        }

        for (int queryIndex = 0; queryIndex < queries.arraySize; queryIndex++)
        {
            SerializedProperty query = queries.GetArrayElementAtIndex(queryIndex);
            SerializedProperty chance = query.FindPropertyRelative("chance");
            SerializedProperty itemType = query.FindPropertyRelative("itemType");
            SerializedProperty formScope = query.FindPropertyRelative("formScope");
            SerializedProperty specificForm = query.FindPropertyRelative("specificForm");
            SerializedProperty tierWeight0 = query.FindPropertyRelative("tierWeight0");
            SerializedProperty tierWeight1 = query.FindPropertyRelative("tierWeight1");
            SerializedProperty tierWeight2 = query.FindPropertyRelative("tierWeight2");
            SerializedProperty rollCountWeights = query.FindPropertyRelative("rollCountWeights");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("query " + queryIndex, GUILayout.Width(58f));
            GUILayout.FlexibleSpace();
            bool removed = GUILayout.Button("-", GUILayout.Width(24f));
            EditorGUILayout.EndHorizontal();
            if (removed)
            {
                queries.DeleteArrayElementAtIndex(queryIndex);
                changed = true;
                EditorGUILayout.EndVertical();
                break;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("chance", GUILayout.Width(45f));
            changed |= DrawFloatProperty(chance, "chance", 0f, 1f, 58f);
            changed |= DrawMappedEnumPopup(itemType, "type", s_QueryCategoryValues, s_QueryCategoryNames, 104f);
            changed |= DrawMappedEnumPopup(formScope, "scope", s_FormScopeValues, s_FormScopeNames, 92f);

            DropFormScope scope = formScope != null
                ? (DropFormScope)formScope.intValue
                : DropFormScope.CurrentForm;
            if (scope == DropFormScope.Specific)
                changed |= DrawMappedEnumPopup(specificForm, "form", s_FormValues, s_FormNames, 92f);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16f);
            GUILayout.Label("tier 0", GUILayout.Width(38f));
            changed |= DrawFloatProperty(tierWeight0, "tier 0", 0f, float.MaxValue, 58f);
            GUILayout.Label("tier 1", GUILayout.Width(38f));
            changed |= DrawFloatProperty(tierWeight1, "tier 1", 0f, float.MaxValue, 58f);
            GUILayout.Label("tier 2", GUILayout.Width(38f));
            changed |= DrawFloatProperty(tierWeight2, "tier 2", 0f, float.MaxValue, 58f);
            EditorGUILayout.EndHorizontal();

            changed |= DrawRollCountWeights(rollCountWeights);

            EnemyDropQuery queryValue = ReadQuery(query);
            if (queryValue.chance <= 0f)
                EditorGUILayout.HelpBox("이 쿼리는 절대 발동하지 않음", MessageType.Warning);

            bool hasAnyTier = DropQueryEditorMatcher.HasAnyTier(queryValue);
            if (!hasAnyTier)
                EditorGUILayout.HelpBox("허용 tier 없음 — 무드랍", MessageType.Warning);

            if (rollCountWeights != null && rollCountWeights.isArray && rollCountWeights.arraySize > 10)
                EditorGUILayout.HelpBox("뽑기 횟수 상한이 과도함", MessageType.Warning);

            if (hasAnyTier && !DropQueryEditorMatcher.HasAnyMatch(_queryItems, queryValue))
                EditorGUILayout.HelpBox("이 쿼리에 매칭되는 아이템 없음", MessageType.Warning);

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16f);
        if (GUILayout.Button("+ query", GUILayout.Width(88f)))
        {
            int index = queries.arraySize;
            queries.InsertArrayElementAtIndex(index);
            InitializeQuery(queries.GetArrayElementAtIndex(index));
            changed = true;
        }
        EditorGUILayout.EndHorizontal();

        return changed;
    }

    private bool DrawRollCountWeights(SerializedProperty weights)
    {
        bool changed = false;

        if (weights == null || !weights.isArray)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16f);
            GUILayout.Label("rolls", GUILayout.Width(36f));
            GUILayout.Label("<missing>");
            EditorGUILayout.EndHorizontal();
            return false;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16f);
        GUILayout.Label("rolls", GUILayout.Width(36f));

        EditorGUI.BeginDisabledGroup(weights.arraySize <= 1);
        if (GUILayout.Button("-", GUILayout.Width(24f)))
        {
            weights.DeleteArrayElementAtIndex(weights.arraySize - 1);
            changed = true;
        }
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("+", GUILayout.Width(24f)))
        {
            int index = weights.arraySize;
            weights.InsertArrayElementAtIndex(index);
            weights.GetArrayElementAtIndex(index).floatValue = 1f;
            changed = true;
        }
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < weights.arraySize; i++)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(32f);
            GUILayout.Label((i + 1).ToString(CultureInfo.InvariantCulture), GUILayout.Width(20f));
            changed |= DrawFloatProperty(weights.GetArrayElementAtIndex(i), "roll", 0f, float.MaxValue, 44f);
            EditorGUILayout.EndHorizontal();
        }

        return changed;
    }

    private static bool DrawMappedEnumPopup<T>(
        SerializedProperty property,
        string label,
        T[] values,
        string[] names,
        float width)
        where T : Enum
    {
        GUILayout.Label(label, GUILayout.Width(34f));
        if (property == null || values == null || values.Length == 0)
        {
            GUILayout.Label("<missing>", GUILayout.Width(width));
            return false;
        }

        int currentIndex = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (Convert.ToInt32(values[i], CultureInfo.InvariantCulture) == property.intValue)
            {
                currentIndex = i;
                break;
            }
        }

        EditorGUI.BeginChangeCheck();
        int nextIndex = EditorGUILayout.Popup(currentIndex, names, GUILayout.Width(width));
        if (!EditorGUI.EndChangeCheck())
            return false;

        property.intValue = Convert.ToInt32(values[nextIndex], CultureInfo.InvariantCulture);
        return true;
    }

    private static EnemyDropQuery ReadQuery(SerializedProperty query)
    {
        return new EnemyDropQuery
        {
            chance = GetFloat(query?.FindPropertyRelative("chance")),
            itemType = (DropQueryCategory)GetInt(query?.FindPropertyRelative("itemType")),
            formScope = (DropFormScope)GetInt(query?.FindPropertyRelative("formScope")),
            specificForm = (PlayerFormId)GetInt(query?.FindPropertyRelative("specificForm")),
            tierWeight0 = GetFloat(query?.FindPropertyRelative("tierWeight0")),
            tierWeight1 = GetFloat(query?.FindPropertyRelative("tierWeight1")),
            tierWeight2 = GetFloat(query?.FindPropertyRelative("tierWeight2"))
        };
    }

    private bool DrawDropItemCode(SerializedProperty property, float width)
    {
        if (property == null)
        {
            GUILayout.Label("<missing>", GUILayout.Width(width));
            GUILayout.Label(GUIContent.none, GUILayout.Width(20f), GUILayout.Height(18f));
            return false;
        }

        EditorGUI.BeginChangeCheck();
        string value = EditorGUILayout.TextField(property.stringValue, GUILayout.Width(width));
        bool changed = EditorGUI.EndChangeCheck();
        if (changed)
        {
            property.stringValue = value;
        }

        string itemCode = property.stringValue;
        GUIContent validationContent = IsKnownItemCode(itemCode)
            ? GUIContent.none
            : GetSeverityIcon(WarningSeverity.Error, "itemCode 없음: " + itemCode);
        GUILayout.Label(validationContent, GUILayout.Width(20f), GUILayout.Height(18f));

        return changed;
    }

    private bool DrawIntProperty(SerializedProperty property, string label, int min, int max, float width)
    {
        if (property == null)
        {
            GUILayout.Label("<missing>", GUILayout.Width(width));
            return false;
        }

        EditorGUI.BeginChangeCheck();
        int value = EditorGUILayout.IntField(property.intValue, GUILayout.Width(width));
        if (!EditorGUI.EndChangeCheck())
            return false;

        property.intValue = Mathf.Clamp(value, min, max);
        return true;
    }

    private bool DrawFloatProperty(SerializedProperty property, string label, float min, float max, float width)
    {
        if (property == null)
        {
            GUILayout.Label("<missing>", GUILayout.Width(width));
            return false;
        }

        EditorGUI.BeginChangeCheck();
        float value = EditorGUILayout.FloatField(property.floatValue, GUILayout.Width(width));
        if (!EditorGUI.EndChangeCheck())
            return false;

        property.floatValue = Mathf.Clamp(value, min, max);
        return true;
    }

    private static bool NormalizeMinMax(SerializedProperty minAmount, SerializedProperty maxAmount)
    {
        bool changed = false;

        if (minAmount != null && minAmount.intValue < 1)
        {
            minAmount.intValue = 1;
            changed = true;
        }

        int minValue = minAmount != null ? minAmount.intValue : 1;
        if (maxAmount != null && maxAmount.intValue < minValue)
        {
            maxAmount.intValue = minValue;
            changed = true;
        }

        return changed;
    }

    private void CreateDropGroup(EnemyRow row)
    {
        if (row == null || row.Data == null)
            return;

        if (TryCreateDropGroup(row.Data, out DropGroupRecord record, out string error))
        {
            row.DropGroups.Add(record);
            row.DropSummary = "-";
            return;
        }

        ShowNotification(new GUIContent(error));
    }

    private bool TryCreateDropGroup(EnemyData enemy, out DropGroupRecord record, out string error)
    {
        record = null;
        error = string.Empty;

        EnemyDropDatabase database = ResolvePrimaryDropDatabase();
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

        SerializedObject databaseObject = new SerializedObject(database);
        databaseObject.Update();

        SerializedProperty groups = databaseObject.FindProperty("groups");
        if (groups == null || !groups.isArray)
        {
            error = "EnemyDropDatabase.groups를 찾을 수 없습니다.";
            return false;
        }

        int index = groups.arraySize;
        groups.InsertArrayElementAtIndex(index);
        SerializedProperty group = groups.GetArrayElementAtIndex(index);
        InitializeDropGroup(group, enemy);

        if (!ApplyAndMark(databaseObject))
        {
            error = "드랍 그룹 생성 변경 적용 실패.";
            return false;
        }

        record = new DropGroupRecord(database, index);
        return true;
    }

    private static void InitializeDropGroup(SerializedProperty group, EnemyData enemy)
    {
        if (group == null)
            return;

        SerializedProperty enemyProperty = group.FindPropertyRelative("enemy");
        SerializedProperty drops = group.FindPropertyRelative("drops");
        SerializedProperty choiceGroups = group.FindPropertyRelative("choiceGroups");
        SerializedProperty queries = group.FindPropertyRelative("queries");

        if (enemyProperty != null)
            enemyProperty.objectReferenceValue = enemy;
        if (drops != null && drops.isArray)
            drops.arraySize = 0;
        if (choiceGroups != null && choiceGroups.isArray)
            choiceGroups.arraySize = 0;
        if (queries != null && queries.isArray)
            queries.arraySize = 0;
    }

    private static void InitializeDrop(SerializedProperty drop)
    {
        if (drop == null)
            return;

        SetString(drop.FindPropertyRelative("itemCode"), DefaultDropItemCode);
        SetInt(drop.FindPropertyRelative("minAmount"), 1);
        SetInt(drop.FindPropertyRelative("maxAmount"), 1);
        SetFloat(drop.FindPropertyRelative("chance"), 1f);
    }

    private static void InitializeQuery(SerializedProperty query)
    {
        if (query == null)
            return;

        SetFloat(query.FindPropertyRelative("chance"), 1f);
        SetInt(query.FindPropertyRelative("itemType"), (int)DropQueryCategory.Material);
        SetInt(query.FindPropertyRelative("formScope"), (int)DropFormScope.CurrentForm);
        SetInt(query.FindPropertyRelative("specificForm"), (int)PlayerFormId.Normal);
        SetFloat(query.FindPropertyRelative("tierWeight0"), 1f);
        SetFloat(query.FindPropertyRelative("tierWeight1"), 0f);
        SetFloat(query.FindPropertyRelative("tierWeight2"), 0f);

        SerializedProperty rollCountWeights = query.FindPropertyRelative("rollCountWeights");
        if (rollCountWeights != null && rollCountWeights.isArray)
        {
            rollCountWeights.arraySize = 1;
            rollCountWeights.GetArrayElementAtIndex(0).floatValue = 1f;
        }
    }

    private static void InitializeChoiceGroup(SerializedProperty choiceGroup)
    {
        if (choiceGroup == null)
            return;

        SetFloat(choiceGroup.FindPropertyRelative("chance"), 1f);
        SerializedProperty choices = choiceGroup.FindPropertyRelative("choices");
        if (choices != null && choices.isArray)
            choices.arraySize = 0;
    }

    private static void InitializeChoice(SerializedProperty choice)
    {
        if (choice == null)
            return;

        SetString(choice.FindPropertyRelative("itemCode"), DefaultDropItemCode);
        SetInt(choice.FindPropertyRelative("minAmount"), 1);
        SetInt(choice.FindPropertyRelative("maxAmount"), 1);
        SetFloat(choice.FindPropertyRelative("weight"), 1f);
    }

    private bool ApplyAndMark(SerializedObject serializedObject)
    {
        bool applied = serializedObject.ApplyModifiedProperties();
        if (applied)
            _hasAssetChanges = true;
        return applied;
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
        if (_warnings.Count == 0)
        {
            EditorGUILayout.HelpBox("No warnings.", MessageType.Info);
        }
        else
        {
            for (int i = 0; i < _warnings.Count; i++)
                DrawWarning(_warnings[i]);
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
        _itemCodes.Clear();
        _scannedDropDatabases.Clear();
        _scannedBossEncounterTables.Clear();
        _scannedSpawnPrefabs.Clear();
        _queryItems.Clear();
        _hasScanned = true;
        _hasAssetChanges = false;
        _lastScanLabel = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        List<EnemyData> enemies = LoadAssets<EnemyData>("t:EnemyData");
        List<EnemyDropDatabase> dropDatabases = LoadAssets<EnemyDropDatabase>("t:EnemyDropDatabase");
        List<ItemDatabase> itemDatabases = LoadAssets<ItemDatabase>("t:ItemDatabase");
        List<BossEncounterTable> bossTables = LoadAssets<BossEncounterTable>("t:BossEncounterTable");

        _scannedDropDatabases.AddRange(dropDatabases);
        _scannedBossEncounterTables.AddRange(bossTables);
        _primaryDropDatabase = dropDatabases.Count > 0 ? dropDatabases[0] : null;
        _primaryBossEncounterTable = bossTables.Count > 0 ? bossTables[0] : null;
        _bossTableLookupAttempted = true;

        Dictionary<EnemyData, EnemyController> prefabMap = BuildPrefabMap(out _hasPoolScene);
        if (_hasPoolScene)
            _scannedSpawnPrefabs.AddRange(
                BuildUniquePoolPrefabs(Object.FindAnyObjectByType<EnemyPoolManager>()));
        SpawnTableState spawnTableState = BuildSpawnTableState();
        Dictionary<EnemyData, List<DropGroupRecord>> dropGroups = BuildDropGroups(dropDatabases);
        HashSet<string> itemCodes = BuildItemCodeSet(itemDatabases);
        _itemCodes.UnionWith(itemCodes);
        for (int i = 0; i < itemDatabases.Count; i++)
            itemDatabases[i]?.GetAllItems(_queryItems);
        HashSet<EnemyData> bossEnemies = BuildBossSet(bossTables);

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyData enemy = enemies[i];
            if (enemy == null)
                continue;

            EnemyRow row = new EnemyRow(enemy);
            row.TypeSummary = BuildTypeSummary(enemy);
            row.IsBoss = bossEnemies.Contains(enemy);

            if (_hasPoolScene && prefabMap.TryGetValue(enemy, out EnemyController prefab))
            {
                row.HasPoolEntry = true;
                row.Prefab = prefab;
            }

            if (dropGroups.TryGetValue(enemy, out List<DropGroupRecord> groups))
            {
                row.DropGroups.AddRange(groups);
                row.DropSummary = BuildDropSummary(groups);
                AddDropWarnings(row, groups, itemCodes);
            }
            else
            {
                row.DropSummary = "-";
                AddWarning(row, WarningSeverity.Info, "[Info] 드랍 그룹 없음: " + row.DisplayName, enemy);
            }

            if (_hasPoolScene)
            {
                if (!row.HasPoolEntry)
                    AddWarning(row, WarningSeverity.Error, "[Error] 풀 매핑 없음: " + row.DisplayName, enemy);
                else if (row.Prefab == null)
                    AddWarning(row, WarningSeverity.Error, "[Error] 풀 프리팹 null: " + row.DisplayName, enemy);

                if (enemy.PatternSet != null && row.Prefab != null &&
                    row.Prefab.GetComponent<EnemyPatternRunner>() == null)
                {
                    AddWarning(
                        row,
                        WarningSeverity.Warning,
                        "[Warn] PatternSet은 있는데 프리팹에 EnemyPatternRunner 없음: " + row.DisplayName,
                        enemy);
                }
            }

            if (enemy.HasInvalidFloorRange())
                AddWarning(row, WarningSeverity.Error, "[Error] 층범위 오류: " + row.DisplayName + " (" + enemy.MinFloor + "~" + enemy.MaxFloor + ")", enemy);

            AddSpawnTableWarnings(row, spawnTableState);

            _rows.Add(row);
        }
    }

    private static SpawnTableState BuildSpawnTableState()
    {
        SpawnTableState state = new SpawnTableState();
        RoomSpawner spawner = Object.FindAnyObjectByType<RoomSpawner>();
        state.HasRoomSpawner = spawner != null;
        if (spawner == null)
            return state;

        SerializedObject spawnerObject = new SerializedObject(spawner);
        ReadSpawnTable(spawnerObject.FindProperty("enemyTable"), state.NormalEnemies, out state.NormalTableHasEntries);
        bool unused;
        ReadSpawnTable(spawnerObject.FindProperty("eliteRoomEnemyTable"), state.EliteEnemies, out unused);
        return state;
    }

    private static void ReadSpawnTable(SerializedProperty table, HashSet<EnemyData> results, out bool hasEntries)
    {
        hasEntries = table != null && table.isArray && table.arraySize > 0;
        if (!hasEntries)
            return;

        for (int i = 0; i < table.arraySize; i++)
        {
            EnemyData enemy = GetObject<EnemyData>(table.GetArrayElementAtIndex(i));
            if (enemy != null)
                results.Add(enemy);
        }
    }

    private void AddSpawnTableWarnings(EnemyRow row, SpawnTableState state)
    {
        if (!state.HasRoomSpawner || row == null || row.Data == null || row.IsBoss)
            return;

        if ((row.Data.allowedRegions & SpawnRegion.Dungeon) == 0)
            return;

        if (row.Data.IsElite)
        {
            if (!state.EliteEnemies.Contains(row.Data))
                AddWarning(row, WarningSeverity.Warning, "[Warn] 스폰 테이블(elite) 미등록: " + row.DisplayName, row.Data);
            return;
        }

        if (state.NormalTableHasEntries && !state.NormalEnemies.Contains(row.Data))
            AddWarning(row, WarningSeverity.Warning, "[Warn] 스폰 테이블(enemyTable) 미등록: " + row.DisplayName, row.Data);
    }

    private static Dictionary<EnemyData, EnemyController> BuildPrefabMap(out bool hasPoolScene)
    {
        Dictionary<EnemyData, EnemyController> map = new Dictionary<EnemyData, EnemyController>();
        EnemyPoolManager manager = Object.FindAnyObjectByType<EnemyPoolManager>();
        hasPoolScene = manager != null;
        if (!hasPoolScene)
            return map;

        SerializedObject managerObject = new SerializedObject(manager);
        SerializedProperty entries = managerObject.FindProperty("entries");
        if (entries == null || !entries.isArray)
            return map;

        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            EnemyData data = GetObject<EnemyData>(entry.FindPropertyRelative("data"));
            EnemyController prefab = GetObject<EnemyController>(entry.FindPropertyRelative("prefab"));
            if (data == null || map.ContainsKey(data))
                continue;

            map.Add(data, prefab);
        }

        return map;
    }

    private static Dictionary<EnemyData, List<DropGroupRecord>> BuildDropGroups(List<EnemyDropDatabase> databases)
    {
        Dictionary<EnemyData, List<DropGroupRecord>> groupsByEnemy = new Dictionary<EnemyData, List<DropGroupRecord>>();

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
                if (enemy == null)
                    continue;

                DropGroupRecord record = new DropGroupRecord(database, groupIndex);
                ReadDrops(group.FindPropertyRelative("drops"), record);
                ReadChoiceGroups(group.FindPropertyRelative("choiceGroups"), record);
                AddToLookup(groupsByEnemy, enemy, record);
            }
        }

        return groupsByEnemy;
    }

    private static void ReadDrops(SerializedProperty drops, DropGroupRecord record)
    {
        if (drops == null || !drops.isArray)
            return;

        for (int i = 0; i < drops.arraySize; i++)
        {
            SerializedProperty drop = drops.GetArrayElementAtIndex(i);
            string itemCode = GetString(drop.FindPropertyRelative("itemCode"));
            int minAmount = GetInt(drop.FindPropertyRelative("minAmount"));
            int maxAmount = GetInt(drop.FindPropertyRelative("maxAmount"));
            float chance = GetFloat(drop.FindPropertyRelative("chance"));

            record.ItemCodes.Add(itemCode);
            record.SummaryParts.Add(itemCode + " " + FormatAmount(minAmount, maxAmount) + " (" + FormatChance(chance) + ")");
        }
    }

    private static void ReadChoiceGroups(SerializedProperty choiceGroups, DropGroupRecord record)
    {
        if (choiceGroups == null || !choiceGroups.isArray)
            return;

        for (int groupIndex = 0; groupIndex < choiceGroups.arraySize; groupIndex++)
        {
            SerializedProperty choiceGroup = choiceGroups.GetArrayElementAtIndex(groupIndex);
            float chance = GetFloat(choiceGroup.FindPropertyRelative("chance"));
            SerializedProperty choices = choiceGroup.FindPropertyRelative("choices");
            List<string> choiceParts = new List<string>();

            if (choices != null && choices.isArray)
            {
                for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
                {
                    SerializedProperty choice = choices.GetArrayElementAtIndex(choiceIndex);
                    string itemCode = GetString(choice.FindPropertyRelative("itemCode"));
                    float weight = GetFloat(choice.FindPropertyRelative("weight"));

                    record.ItemCodes.Add(itemCode);
                    choiceParts.Add(itemCode + "(" + FormatWeight(weight) + ")");
                }
            }

            record.SummaryParts.Add("[택1 " + FormatChance(chance) + "] " + string.Join(" / ", choiceParts));
        }
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

    private static HashSet<EnemyData> BuildBossSet(List<BossEncounterTable> bossTables)
    {
        HashSet<EnemyData> bosses = new HashSet<EnemyData>();

        for (int tableIndex = 0; tableIndex < bossTables.Count; tableIndex++)
        {
            BossEncounterTable table = bossTables[tableIndex];
            if (table == null || table.Entries == null)
                continue;

            foreach (BossEncounterEntry entry in table.Entries)
            {
                if (entry != null && entry.Boss != null)
                    bosses.Add(entry.Boss);
            }
        }

        return bosses;
    }

    private void AddDropWarnings(EnemyRow row, List<DropGroupRecord> groups, HashSet<string> itemCodes)
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            DropGroupRecord group = groups[groupIndex];
            for (int codeIndex = 0; codeIndex < group.ItemCodes.Count; codeIndex++)
            {
                string itemCode = group.ItemCodes[codeIndex];
                string key = string.IsNullOrWhiteSpace(itemCode) ? "<empty>" : itemCode;
                if (!seen.Add(key))
                    continue;

                if (string.IsNullOrWhiteSpace(itemCode) || !itemCodes.Contains(itemCode))
                {
                    AddWarning(
                        row,
                        WarningSeverity.Error,
                        "[Error] 드랍 itemCode가 ItemDatabase에 없음: " + row.DisplayName + " -> " + key,
                        group.Database);
                }
            }
        }
    }

    private void AddWarning(EnemyRow row, WarningSeverity severity, string message, Object target)
    {
        DashboardWarning warning = new DashboardWarning(severity, message, target);
        row.Warnings.Add(warning);
        _warnings.Add(warning);
    }

    private static string BuildTypeSummary(EnemyData enemy)
    {
        List<string> parts = new List<string>(3) { enemy.behaviorType.ToString() };
        if (enemy.specialAttackType != EnemySpecialAttackType.None)
            parts.Add(enemy.specialAttackType.ToString());
        if (enemy.IsElite)
            parts.Add("Elite");

        return string.Join(" + ", parts);
    }

    private static string BuildDropSummary(List<DropGroupRecord> groups)
    {
        List<string> parts = new List<string>();
        for (int i = 0; i < groups.Count; i++)
            parts.AddRange(groups[i].SummaryParts);

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

    private static string FormatWeight(float weight)
    {
        return weight.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static bool HasBossFloor(BossEncounterTable table, int floor)
    {
        if (table == null || table.Entries == null)
            return false;

        for (int i = 0; i < table.Entries.Count; i++)
        {
            BossEncounterEntry entry = table.Entries[i];
            if (entry != null && entry.Floor == floor)
                return true;
        }

        return false;
    }

    private bool IsKnownItemCode(string itemCode)
    {
        return !string.IsNullOrWhiteSpace(itemCode) && _itemCodes.Contains(itemCode);
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

    private static void DeleteArrayElement(SerializedProperty array, int index)
    {
        int previousSize = array.arraySize;
        array.DeleteArrayElementAtIndex(index);
        if (array.arraySize == previousSize)
            array.DeleteArrayElementAtIndex(index);
    }

    private enum WarningSeverity
    {
        Error,
        Warning,
        Info
    }

    private sealed class EnemyRow
    {
        public readonly EnemyData Data;
        public readonly string DisplayName;
        public readonly List<DashboardWarning> Warnings = new List<DashboardWarning>(4);
        public readonly List<DropGroupRecord> DropGroups = new List<DropGroupRecord>(2);
        public EnemyController Prefab;
        public bool HasPoolEntry;
        public bool IsBoss;
        public bool DropFoldout;
        public string TypeSummary;
        public string DropSummary;

        public EnemyRow(EnemyData data)
        {
            Data = data;
            DisplayName = !string.IsNullOrWhiteSpace(data.enemyName) ? data.enemyName : data.name;
        }

        public WarningSeverity HighestSeverity
        {
            get
            {
                WarningSeverity highest = WarningSeverity.Info;
                for (int i = 0; i < Warnings.Count; i++)
                    if (Warnings[i].Severity < highest)
                        highest = Warnings[i].Severity;

                return highest;
            }
        }

        public string WarningTooltip
        {
            get
            {
                List<string> messages = new List<string>(Warnings.Count);
                for (int i = 0; i < Warnings.Count; i++)
                    messages.Add(Warnings[i].Message);

                return string.Join("\n", messages);
            }
        }
    }

    private sealed class DropGroupRecord
    {
        public readonly EnemyDropDatabase Database;
        public readonly int GroupIndex;
        public readonly List<string> SummaryParts = new List<string>(4);
        public readonly List<string> ItemCodes = new List<string>(4);

        public DropGroupRecord(EnemyDropDatabase database, int groupIndex)
        {
            Database = database;
            GroupIndex = groupIndex;
        }
    }

    private sealed class SpawnTableState
    {
        public readonly HashSet<EnemyData> NormalEnemies = new HashSet<EnemyData>();
        public readonly HashSet<EnemyData> EliteEnemies = new HashSet<EnemyData>();
        public bool HasRoomSpawner;
        public bool NormalTableHasEntries;
    }

    private readonly struct PoolEntrySnapshot
    {
        public readonly EnemyData Data;
        public readonly EnemyController Prefab;

        public PoolEntrySnapshot(EnemyData data, EnemyController prefab)
        {
            Data = data;
            Prefab = prefab;
        }
    }

    private sealed class DeleteAnalysis
    {
        public readonly EnemyData Target;
        public readonly string DisplayName;
        public readonly List<EnemyController> TargetPrefabs = new List<EnemyController>();
        public readonly List<EnemyController> OwnedPrefabs = new List<EnemyController>();
        public readonly HashSet<EnemyController> SharedPrefabs = new HashSet<EnemyController>();
        public EnemyPoolManager PoolManager;
        public RoomSpawner RoomSpawner;
        public string EnemyAssetPath;
        public int PoolEntryCount;
        public int SpawnTableCount;
        public int DropGroupCount;
        public int BossEntryCount;

        public DeleteAnalysis(EnemyData target)
        {
            Target = target;
            DisplayName = target != null && !string.IsNullOrWhiteSpace(target.enemyName)
                ? target.enemyName
                : target != null ? target.name : "<null>";
        }

        public bool CanDeletePrefab => TargetPrefabs.Count == 1 && OwnedPrefabs.Count == 1 && SharedPrefabs.Count == 0;
    }

    private struct DeletionCounts
    {
        public int DropGroups;
        public int BossEntries;
        public int SpawnEntries;
        public int PoolEntries;
        public int PrefabDataCleared;
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
