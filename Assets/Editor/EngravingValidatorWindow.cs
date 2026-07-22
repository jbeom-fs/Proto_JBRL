using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class EngravingValidatorWindow : EditorWindow
{
    private readonly List<ValidationResult> _results = new List<ValidationResult>(64);
    private bool _hasScanned;
    private Vector2 _scrollPosition;

    [MenuItem("JBRogLike/Engraving Validator")]
    public static void Open()
    {
        GetWindow<EngravingValidatorWindow>("Engraving Validator");
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (!_hasScanned)
        {
            EditorGUILayout.HelpBox("Click Scan to validate engravings, ItemDatabases, and EnemyDropDatabases.", MessageType.Info);
            return;
        }

        DrawResults();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            Scan();

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawResults()
    {
        EditorGUILayout.LabelField("Results: " + _results.Count, EditorStyles.boldLabel);

        if (_results.Count == 0)
        {
            EditorGUILayout.HelpBox("No issues found for current filters.", MessageType.Info);
            return;
        }

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        for (int i = 0; i < _results.Count; i++)
        {
            ValidationResult result = _results[i];
            DrawResult(result);
        }

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
        _results.Clear();
        _hasScanned = true;

        List<EngravingData> engravings = LoadAssets<EngravingData>("t:EngravingData");
        List<ItemDatabase> itemDatabases = LoadAssets<ItemDatabase>("t:ItemDatabase");
        List<EnemyDropDatabase> dropDatabases = LoadAssets<EnemyDropDatabase>("t:EnemyDropDatabase");

        ScanContext context = BuildItemContext(itemDatabases);
        List<DropRecord> dropRecords = BuildDropRecords(dropDatabases);

        AddOrphanEngravingResults(engravings, itemDatabases, context);
        AddItemDatabaseResults(context);
        AddDropDatabaseResults(dropRecords, context);
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
                AddResult(ResultSeverity.Error, "ItemDatabase '" + database.name + "' has no serialized items list.", database);
                continue;
            }

            Dictionary<string, List<ItemRecord>> codesInDatabase = new Dictionary<string, List<ItemRecord>>(StringComparer.Ordinal);
            context.CodesByDatabase.Add(database, codesInDatabase);

            for (int itemIndex = 0; itemIndex < items.arraySize; itemIndex++)
            {
                SerializedProperty item = items.GetArrayElementAtIndex(itemIndex);
                ItemRecord record = CreateItemRecord(database, itemIndex, item);
                context.Items.Add(record);

                if (record.Engraving != null)
                    AddToLookup(context.ItemsByEngraving, record.Engraving, record);

                if (string.IsNullOrEmpty(record.Code))
                {
                    AddResult(ResultSeverity.Warning, "Empty itemCode in ItemDatabase '" + database.name + "' entry " + itemIndex + ".", database);
                    continue;
                }

                AddToLookup(context.ItemsByCode, record.Code, record);
                AddToLookup(codesInDatabase, record.Code, record);
            }
        }

        return context;
    }

    private static ItemRecord CreateItemRecord(ItemDatabase database, int itemIndex, SerializedProperty item)
    {
        SerializedProperty itemCode = item.FindPropertyRelative("itemCode");
        SerializedProperty displayName = item.FindPropertyRelative("displayName");
        SerializedProperty itemType = item.FindPropertyRelative("itemType");
        SerializedProperty engraving = item.FindPropertyRelative("engraving");

        return new ItemRecord
        {
            Database = database,
            Index = itemIndex,
            RawCode = GetString(itemCode),
            Code = NormalizeCode(GetString(itemCode)),
            DisplayName = GetString(displayName),
            ItemType = itemType != null ? (ItemType)itemType.intValue : ItemType.Key,
            Engraving = engraving != null ? engraving.objectReferenceValue as EngravingData : null
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
                AddResult(ResultSeverity.Error, "EnemyDropDatabase '" + database.name + "' has no serialized groups list.", database);
                continue;
            }

            for (int groupIndex = 0; groupIndex < groups.arraySize; groupIndex++)
            {
                SerializedProperty group = groups.GetArrayElementAtIndex(groupIndex);
                string groupLabel = GetGroupLabel(group, groupIndex);

                SerializedProperty drops = group.FindPropertyRelative("drops");
                if (drops != null && drops.isArray)
                    AddDropEntries(database, groupIndex, groupLabel, drops, records);

                SerializedProperty choiceGroups = group.FindPropertyRelative("choiceGroups");
                if (choiceGroups != null && choiceGroups.isArray)
                    AddChoiceEntries(database, groupIndex, groupLabel, choiceGroups, records);
            }
        }

        return records;
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
                "drop " + dropIndex,
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
                    "choice group " + choiceGroupIndex + " choice " + choiceIndex,
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
        string rawCode = GetString(itemCode);
        return new DropRecord
        {
            Database = database,
            GroupIndex = groupIndex,
            GroupLabel = groupLabel,
            Path = path,
            RawCode = rawCode,
            Code = NormalizeCode(rawCode),
            MinAmount = minAmount != null ? minAmount.intValue : 0,
            MaxAmount = maxAmount != null ? maxAmount.intValue : 0
        };
    }

    private void AddOrphanEngravingResults(List<EngravingData> engravings, List<ItemDatabase> itemDatabases, ScanContext context)
    {
        ItemDatabase targetDatabase = itemDatabases.Count > 0 ? itemDatabases[0] : null;
        for (int i = 0; i < engravings.Count; i++)
        {
            EngravingData engraving = engravings[i];
            if (engraving == null || context.ItemsByEngraving.ContainsKey(engraving))
                continue;

            string message = "Orphan engraving '" + engraving.name + "': no ItemDatabase entry references it.";
            if (targetDatabase != null)
                message += " Fix target: '" + targetDatabase.name + "'.";
            else
                message += " No ItemDatabase found for auto-fix.";

            ValidationResult result = AddResult(ResultSeverity.Error, message, engraving);
            if (targetDatabase != null)
            {
                result.CanFix = true;
                result.FixLabel = "Add to ItemDatabase";
                result.FixDatabase = targetDatabase;
                result.FixEngraving = engraving;
            }
        }
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
                    "ItemDatabase '" + item.Database.name + "' entry " + item.Index +
                    " itemCode '" + DisplayCode(item) + "' has itemType Engraving but engraving is null.",
                    item.Database);
            }

            if (item.Engraving != null && item.ItemType != ItemType.Engraving)
            {
                AddResult(
                    ResultSeverity.Error,
                    "ItemDatabase '" + item.Database.name + "' entry " + item.Index +
                    " itemCode '" + DisplayCode(item) + "' references engraving '" + item.Engraving.name +
                    "' but itemType is " + item.ItemType + ".",
                    item.Database);
            }
        }

        foreach (KeyValuePair<EngravingData, List<ItemRecord>> pair in context.ItemsByEngraving)
        {
            if (pair.Value.Count < 2)
                continue;

            AddResult(
                ResultSeverity.Warning,
                "Duplicate engraving reference '" + pair.Key.name + "': " + FormatItemLocations(pair.Value) + ".",
                pair.Key);
        }

        foreach (KeyValuePair<ItemDatabase, Dictionary<string, List<ItemRecord>>> databasePair in context.CodesByDatabase)
        {
            foreach (KeyValuePair<string, List<ItemRecord>> codePair in databasePair.Value)
            {
                if (codePair.Value.Count < 2)
                    continue;

                AddResult(
                    ResultSeverity.Warning,
                    "Duplicate itemCode '" + codePair.Key + "' in ItemDatabase '" + databasePair.Key.name +
                    "': " + FormatItemLocations(codePair.Value) + ".",
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
                AddResult(ResultSeverity.Warning, "Empty itemCode in " + location + ".", drop.Database);
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

            if (IsEngravingCode(drop.Code, context))
            {
                int effectiveMax = Mathf.Max(Mathf.Max(1, drop.MinAmount), drop.MaxAmount);
                if (effectiveMax > 1)
                {
                    AddResult(
                        ResultSeverity.Warning,
                        "Engraving drop itemCode '" + drop.Code + "' in " + location +
                        " rolls up to " + effectiveMax + " (>1). Set min=max=1.",
                        drop.Database);
                }
            }
        }
    }

    private void ApplyFix(ValidationResult result)
    {
        if (!result.CanFix || result.FixDatabase == null || result.FixEngraving == null)
            return;

        AddEngravingToItemDatabase(result.FixDatabase, result.FixEngraving);
        EditorGUIUtility.PingObject(result.FixDatabase);
        Scan();
    }

    private static void AddEngravingToItemDatabase(ItemDatabase database, EngravingData engraving)
    {
        SerializedObject databaseObject = new SerializedObject(database);
        SerializedProperty items = databaseObject.FindProperty("items");
        if (items == null || !items.isArray)
            return;

        HashSet<string> existingCodes = new HashSet<string>(StringComparer.Ordinal);
        List<string> existingEngravingCodes = new List<string>(8);
        for (int i = 0; i < items.arraySize; i++)
        {
            SerializedProperty item = items.GetArrayElementAtIndex(i);
            string code = NormalizeCode(GetString(item.FindPropertyRelative("itemCode")));
            if (!string.IsNullOrEmpty(code))
                existingCodes.Add(code);

            SerializedProperty itemType = item.FindPropertyRelative("itemType");
            if (itemType != null && itemType.intValue == (int)ItemType.Engraving && !string.IsNullOrEmpty(code))
                existingEngravingCodes.Add(code);
        }

        Undo.RecordObject(database, "Add Engraving ItemData");
        items.arraySize++;
        SerializedProperty newItem = items.GetArrayElementAtIndex(items.arraySize - 1);
        ResetItemEntry(newItem);

        SetString(newItem, "itemCode", GenerateUniqueItemCode(engraving, existingCodes, existingEngravingCodes));
        SetString(newItem, "displayName", string.IsNullOrWhiteSpace(engraving.skillName) ? engraving.name : engraving.skillName);
        SetInt(newItem, "itemType", (int)ItemType.Engraving);
        SetObject(newItem, "engraving", engraving);
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
        SetObject(item, "engraving", null);
        SetString(item, "salvageItemCode", string.Empty);
        SetInt(item, "salvageMinAmount", 1);
        SetInt(item, "salvageMaxAmount", 1);
    }

    private static string GenerateUniqueItemCode(
        EngravingData engraving,
        HashSet<string> existingCodes,
        List<string> existingEngravingCodes)
    {
        string baseCode = GenerateItemCodeBase(engraving, existingEngravingCodes);
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

    private static string GenerateItemCodeBase(EngravingData engraving, List<string> existingEngravingCodes)
    {
        bool hasExisting = existingEngravingCodes.Count > 0;
        bool lowerSnake = hasExisting && IsLowerSnake(existingEngravingCodes[0]);
        bool engPrefix = false;

        for (int i = 0; i < existingEngravingCodes.Count; i++)
        {
            if (existingEngravingCodes[i].StartsWith("Eng_", StringComparison.Ordinal))
            {
                engPrefix = true;
                break;
            }
        }

        if (lowerSnake)
            return "eng_" + ToSnakeToken(engraving.owningForm.ToString()) + "_" + ToSnakeToken(engraving.grade.ToString());

        if (engPrefix || hasExisting)
            return "Eng_" + SanitizePascalToken(engraving.owningForm.ToString()) + "_" + SanitizePascalToken(engraving.grade.ToString());

        return "Eng_" + SanitizePascalToken(engraving.owningForm.ToString()) + "_" + SanitizePascalToken(engraving.grade.ToString());
    }

    private ValidationResult AddResult(ResultSeverity severity, string message, UnityEngine.Object target)
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

    private static bool IsEngravingCode(string code, ScanContext context)
    {
        if (!context.ItemsByCode.TryGetValue(code, out List<ItemRecord> items))
            return false;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].ItemType == ItemType.Engraving)
                return true;
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
        return "EnemyDropDatabase '" + drop.Database.name + "' group " + drop.GroupIndex +
               " (" + drop.GroupLabel + ") " + drop.Path;
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



    private static void SetObject(SerializedProperty parent, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static void SetArraySize(SerializedProperty parent, string propertyName, int size)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null && property.isArray)
            property.arraySize = size;
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

    private sealed class ScanContext
    {
        public readonly List<ItemRecord> Items = new List<ItemRecord>(64);
        public readonly Dictionary<string, List<ItemRecord>> ItemsByCode = new Dictionary<string, List<ItemRecord>>(StringComparer.Ordinal);
        public readonly Dictionary<EngravingData, List<ItemRecord>> ItemsByEngraving = new Dictionary<EngravingData, List<ItemRecord>>();
        public readonly Dictionary<ItemDatabase, Dictionary<string, List<ItemRecord>>> CodesByDatabase =
            new Dictionary<ItemDatabase, Dictionary<string, List<ItemRecord>>>();
    }

    private sealed class ItemRecord
    {
        public ItemDatabase Database;
        public int Index;
        public string RawCode;
        public string Code;
        public string DisplayName;
        public ItemType ItemType;
        public EngravingData Engraving;
    }

    private sealed class DropRecord
    {
        public EnemyDropDatabase Database;
        public int GroupIndex;
        public string GroupLabel;
        public string Path;
        public string RawCode;
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
        public EngravingData FixEngraving;
    }
}
