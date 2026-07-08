using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class EnemyDashboardWindow : EditorWindow
{
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
    private const float WarningPanelHeight = 180f;
    private const string DefaultDropItemCode = "Currency";

    private readonly List<EnemyRow> _rows = new List<EnemyRow>(32);
    private readonly List<DashboardWarning> _warnings = new List<DashboardWarning>(64);
    private readonly HashSet<string> _itemCodes = new HashSet<string>(StringComparer.Ordinal);
    private Vector2 _rowScrollPosition;
    private Vector2 _warningScrollPosition;
    private bool _hasScanned;
    private bool _hasPoolScene;
    private bool _hasAssetChanges;
    private string _lastScanLabel = "-";
    private EnemyDropDatabase _primaryDropDatabase;

    [MenuItem("JBRogLike/Enemy Dashboard")]
    public static void Open()
    {
        GetWindow<EnemyDashboardWindow>("Enemy Dashboard");
    }

    private void OnEnable()
    {
        minSize = new Vector2(1180f, 520f);
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (!_hasScanned)
        {
            EditorGUILayout.HelpBox("Click Scan to build the Enemy Dashboard.", MessageType.Info);
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

        GUILayout.FlexibleSpace();
        GUILayout.Label("Last scan: " + _lastScanLabel, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
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
    }

    private void DrawRowsPanel()
    {
        if (_rows.Count == 0)
        {
            EditorGUILayout.HelpBox("No EnemyData assets found.", MessageType.Info);
            return;
        }

        _rowScrollPosition = EditorGUILayout.BeginScrollView(_rowScrollPosition, true, true, GUILayout.MinHeight(240f));
        DrawHeader();
        for (int i = 0; i < _rows.Count; i++)
            DrawRow(_rows[i]);
        EditorGUILayout.EndScrollView();
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

        EditorGUILayout.EndHorizontal();

        if (row.DropFoldout)
            DrawDropEditor(row);

        EditorGUILayout.EndVertical();
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
        nextMaxHp = EditorGUILayout.DelayedIntField(nextMaxHp, GUILayout.Width(StatWidth));
        nextAttack = EditorGUILayout.DelayedIntField(nextAttack, GUILayout.Width(StatWidth));
        nextDefense = EditorGUILayout.DelayedIntField(nextDefense, GUILayout.Width(StatWidth));
        nextExpReward = EditorGUILayout.DelayedIntField(nextExpReward, GUILayout.Width(StatWidth));
        nextMoveSpeed = EditorGUILayout.DelayedFloatField(nextMoveSpeed, GUILayout.Width(MoveWidth));
        nextMinFloor = EditorGUILayout.DelayedIntField(nextMinFloor, GUILayout.Width(FloorEditWidth));
        nextMaxFloor = EditorGUILayout.DelayedIntField(nextMaxFloor, GUILayout.Width(FloorEditWidth));
        nextSpawnCost = EditorGUILayout.DelayedIntField(nextSpawnCost, GUILayout.Width(CostWidth));

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

        if (changed)
            ApplyAndMark(databaseObject);
    }

    private bool DrawDrops(SerializedProperty drops)
    {
        bool changed = false;
        EditorGUILayout.LabelField("drops[]", EditorStyles.miniBoldLabel);

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
            changed |= DrawFloatProperty(chance, "chance", 0f, 1f, 76f);
            if (GUILayout.Button("-", GUILayout.Width(24f)))
            {
                choiceGroups.DeleteArrayElementAtIndex(groupIndex);
                changed = true;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
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
        int value = EditorGUILayout.DelayedIntField(property.intValue, GUILayout.Width(width));
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
        float value = EditorGUILayout.DelayedFloatField(property.floatValue, GUILayout.Width(width));
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
        if (_primaryDropDatabase == null || row.Data == null)
            return;

        SerializedObject databaseObject = new SerializedObject(_primaryDropDatabase);
        databaseObject.Update();

        SerializedProperty groups = databaseObject.FindProperty("groups");
        if (groups == null || !groups.isArray)
            return;

        int index = groups.arraySize;
        groups.InsertArrayElementAtIndex(index);
        SerializedProperty group = groups.GetArrayElementAtIndex(index);
        InitializeDropGroup(group, row.Data);

        if (ApplyAndMark(databaseObject))
        {
            DropGroupRecord record = new DropGroupRecord(_primaryDropDatabase, index);
            row.DropGroups.Add(record);
            row.DropSummary = "-";
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

    private static void InitializeDrop(SerializedProperty drop)
    {
        if (drop == null)
            return;

        SetString(drop.FindPropertyRelative("itemCode"), DefaultDropItemCode);
        SetInt(drop.FindPropertyRelative("minAmount"), 1);
        SetInt(drop.FindPropertyRelative("maxAmount"), 1);
        SetFloat(drop.FindPropertyRelative("chance"), 1f);
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

    private void DrawWarningsPanel()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Warnings", EditorStyles.boldLabel);

        _warningScrollPosition = EditorGUILayout.BeginScrollView(_warningScrollPosition, false, true, GUILayout.MaxHeight(WarningPanelHeight));
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
        _hasScanned = true;
        _hasAssetChanges = false;
        _lastScanLabel = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        List<EnemyData> enemies = LoadAssets<EnemyData>("t:EnemyData");
        List<EnemyDropDatabase> dropDatabases = LoadAssets<EnemyDropDatabase>("t:EnemyDropDatabase");
        List<ItemDatabase> itemDatabases = LoadAssets<ItemDatabase>("t:ItemDatabase");
        List<BossEncounterTable> bossTables = LoadAssets<BossEncounterTable>("t:BossEncounterTable");

        _primaryDropDatabase = dropDatabases.Count > 0 ? dropDatabases[0] : null;

        Dictionary<EnemyData, EnemyController> prefabMap = BuildPrefabMap(out _hasPoolScene);
        Dictionary<EnemyData, List<DropGroupRecord>> dropGroups = BuildDropGroups(dropDatabases);
        HashSet<string> itemCodes = BuildItemCodeSet(itemDatabases);
        _itemCodes.UnionWith(itemCodes);
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
            }

            if (enemy.HasInvalidFloorRange())
                AddWarning(row, WarningSeverity.Error, "[Error] 층범위 오류: " + row.DisplayName + " (" + enemy.MinFloor + "~" + enemy.MaxFloor + ")", enemy);

            if (enemy.IsElite && enemy.ElitePatternSet == null)
                AddWarning(row, WarningSeverity.Warning, "[Warn] isElite인데 ElitePatternSet null: " + row.DisplayName, enemy);

            _rows.Add(row);
        }
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
