using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class EnemyDashboardWindow : EditorWindow
{
    private const float WarningWidth = 28f;
    private const float NameWidth = 170f;
    private const float StatWidth = 48f;
    private const float FloorWidth = 82f;
    private const float CostWidth = 72f;
    private const float TypeWidth = 190f;
    private const float PrefabWidth = 170f;
    private const float BossWidth = 58f;
    private const float DropWidth = 440f;

    private readonly List<EnemyRow> _rows = new List<EnemyRow>(32);
    private readonly List<DashboardWarning> _warnings = new List<DashboardWarning>(64);
    private Vector2 _scrollPosition;
    private bool _hasScanned;
    private bool _hasPoolScene;
    private string _lastScanLabel = "-";

    [MenuItem("JBRogLike/Enemy Dashboard")]
    public static void Open()
    {
        GetWindow<EnemyDashboardWindow>("Enemy Dashboard");
    }

    private void OnEnable()
    {
        minSize = new Vector2(1100f, 420f);
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
        DrawHeader();
        DrawRows();
        DrawWarnings();
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
            EditorGUILayout.LabelField("EnemyPoolManager: 씬 없음", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        Header("!", WarningWidth);
        Header("Name", NameWidth);
        Header("HP", StatWidth);
        Header("Atk", StatWidth);
        Header("Def", StatWidth);
        Header("EXP", StatWidth);
        Header("Floor", FloorWidth);
        Header("Cost", CostWidth);
        Header("Type", TypeWidth);
        Header("Prefab", PrefabWidth);
        Header("Boss", BossWidth);
        Header("Drops", DropWidth);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRows()
    {
        if (_rows.Count == 0)
        {
            EditorGUILayout.HelpBox("No EnemyData assets found.", MessageType.Info);
            return;
        }

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, true, true);
        for (int i = 0; i < _rows.Count; i++)
            DrawRow(_rows[i]);
        EditorGUILayout.EndScrollView();
    }

    private void DrawRow(EnemyRow row)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

        if (row.Warnings.Count > 0)
            GUILayout.Label(GetSeverityIcon(row.HighestSeverity, row.WarningTooltip), GUILayout.Width(WarningWidth), GUILayout.Height(20f));
        else
            GUILayout.Label(GUIContent.none, GUILayout.Width(WarningWidth), GUILayout.Height(20f));

        if (GUILayout.Button(row.DisplayName, GUILayout.Width(NameWidth)))
            EditorGUIUtility.PingObject(row.Data);

        Cell(row.Data.maxHp.ToString(CultureInfo.InvariantCulture), StatWidth);
        Cell(row.Data.attack.ToString(CultureInfo.InvariantCulture), StatWidth);
        Cell(row.Data.defense.ToString(CultureInfo.InvariantCulture), StatWidth);
        Cell(row.Data.expReward.ToString(CultureInfo.InvariantCulture), StatWidth);
        Cell(row.Data.MinFloor + "~" + row.Data.MaxFloor, FloorWidth);
        Cell(row.Data.spawnCost.ToString(CultureInfo.InvariantCulture), CostWidth);
        Cell(row.TypeSummary, TypeWidth);

        if (!_hasPoolScene)
            Cell("씬 없음", PrefabWidth);
        else if (row.Prefab != null)
        {
            if (GUILayout.Button(row.Prefab.name, GUILayout.Width(PrefabWidth)))
                EditorGUIUtility.PingObject(row.Prefab);
        }
        else
            Cell("-", PrefabWidth);

        Cell(row.IsBoss ? "Boss" : "-", BossWidth);
        Cell(row.DropSummary, DropWidth);

        EditorGUILayout.EndHorizontal();
    }

    private void DrawWarnings()
    {
        if (_warnings.Count == 0)
        {
            EditorGUILayout.HelpBox("No warnings.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Warnings", EditorStyles.boldLabel);
        for (int i = 0; i < _warnings.Count; i++)
        {
            DashboardWarning warning = _warnings[i];
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(GetSeverityIcon(warning.Severity, warning.Message), GUILayout.Width(24f), GUILayout.Height(20f));
            EditorGUILayout.LabelField(warning.Message, EditorStyles.wordWrappedLabel);
            if (warning.Target != null && GUILayout.Button("Ping", GUILayout.Width(48f)))
                EditorGUIUtility.PingObject(warning.Target);
            EditorGUILayout.EndHorizontal();
        }
    }

    private void Scan()
    {
        _rows.Clear();
        _warnings.Clear();
        _hasScanned = true;
        _lastScanLabel = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        List<EnemyData> enemies = LoadAssets<EnemyData>("t:EnemyData");
        List<EnemyDropDatabase> dropDatabases = LoadAssets<EnemyDropDatabase>("t:EnemyDropDatabase");
        List<ItemDatabase> itemDatabases = LoadAssets<ItemDatabase>("t:ItemDatabase");
        List<BossEncounterTable> bossTables = LoadAssets<BossEncounterTable>("t:BossEncounterTable");

        Dictionary<EnemyData, EnemyController> prefabMap = BuildPrefabMap(out _hasPoolScene);
        Dictionary<EnemyData, List<DropGroupRecord>> dropGroups = BuildDropGroups(dropDatabases);
        HashSet<string> itemCodes = BuildItemCodeSet(itemDatabases);
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
                row.Prefab = prefab;

            if (dropGroups.TryGetValue(enemy, out List<DropGroupRecord> groups))
            {
                row.DropSummary = BuildDropSummary(groups);
                AddDropWarnings(row, groups, itemCodes);
            }
            else
            {
                row.DropSummary = "-";
                AddWarning(row, WarningSeverity.Info, "[Info] 드랍 그룹 없음: " + row.DisplayName, enemy);
            }

            if (_hasPoolScene && !prefabMap.ContainsKey(enemy))
                AddWarning(row, WarningSeverity.Error, "[Error] 풀 매핑 없음: " + row.DisplayName, enemy);

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
        public EnemyController Prefab;
        public bool IsBoss;
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
