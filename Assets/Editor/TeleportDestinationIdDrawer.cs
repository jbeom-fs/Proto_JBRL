using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(TeleportDestinationIdAttribute))]
public sealed class TeleportDestinationIdDrawer : PropertyDrawer
{
    private const float LineGap = 2f;
    private const float TextFieldWidth = 150f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        TeleportDestinationDatabase database = FindDatabase();
        List<Entry> entries = ReadEntries(database);

        if (database == null || entries.Count == 0)
        {
            EditorGUI.PropertyField(LineRect(position, 0), property, label);
            Rect helpRect = LineRect(position, 1);
            string message = database == null
                ? "TeleportDestinationDatabase asset not found. Falling back to string input."
                : "TeleportDestinationDatabase has no destination ids. Falling back to string input.";
            EditorGUI.HelpBox(helpRect, message, MessageType.Info);
            return;
        }

        EditorGUI.BeginProperty(position, label, property);

        Rect controlRect = LineRect(position, 0);
        Rect valueRect = EditorGUI.PrefixLabel(controlRect, label);
        Rect popupRect = valueRect;
        popupRect.width = Mathf.Max(60f, valueRect.width - TextFieldWidth - 4f);
        Rect textRect = valueRect;
        textRect.x = popupRect.xMax + 4f;
        textRect.width = Mathf.Min(TextFieldWidth, valueRect.width * 0.45f);

        string current = property.stringValue;
        int selectedEntryIndex = FindEntryIndex(entries, current);
        string[] options = BuildOptions(entries, current, selectedEntryIndex);
        int popupIndex = selectedEntryIndex >= 0 ? selectedEntryIndex : 0;

        EditorGUI.BeginChangeCheck();
        int newPopupIndex = EditorGUI.Popup(popupRect, popupIndex, options);
        if (EditorGUI.EndChangeCheck())
        {
            int newEntryIndex = selectedEntryIndex >= 0 ? newPopupIndex : newPopupIndex - 1;
            if (newEntryIndex >= 0 && newEntryIndex < entries.Count)
                property.stringValue = entries[newEntryIndex].Id;
        }

        EditorGUI.BeginChangeCheck();
        string typed = EditorGUI.TextField(textRect, property.stringValue);
        if (EditorGUI.EndChangeCheck())
            property.stringValue = typed;

        string updated = property.stringValue;
        int updatedEntryIndex = FindEntryIndex(entries, updated);
        if (!string.IsNullOrWhiteSpace(updated) && updatedEntryIndex < 0)
        {
            EditorGUI.HelpBox(LineRect(position, 1),
                "Destination id is not registered in TeleportDestinationDatabase: " + updated,
                MessageType.Warning);
        }
        else if (updatedEntryIndex >= 0)
        {
            Entry entry = entries[updatedEntryIndex];
            string details = entry.DisplayName + " | " + entry.LocationType;
            if (!string.IsNullOrWhiteSpace(entry.Description))
                details += " | " + entry.Description;
            EditorGUI.HelpBox(LineRect(position, 1), details, MessageType.None);
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
            return EditorGUI.GetPropertyHeight(property, label, true);

        return EditorGUIUtility.singleLineHeight * 2f + LineGap;
    }

    private static Rect LineRect(Rect position, int line)
    {
        return new Rect(
            position.x,
            position.y + line * (EditorGUIUtility.singleLineHeight + LineGap),
            position.width,
            EditorGUIUtility.singleLineHeight);
    }

    private static TeleportDestinationDatabase FindDatabase()
    {
        string[] guids = AssetDatabase.FindAssets("t:TeleportDestinationDatabase");
        if (guids == null || guids.Length == 0)
            return null;

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<TeleportDestinationDatabase>(path);
    }

    private static List<Entry> ReadEntries(TeleportDestinationDatabase database)
    {
        var entries = new List<Entry>();
        if (database == null)
            return entries;

        var serializedDatabase = new SerializedObject(database);
        SerializedProperty locations = serializedDatabase.FindProperty("locations");
        if (locations == null || !locations.isArray)
            return entries;

        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        for (int i = 0; i < locations.arraySize; i++)
        {
            SerializedProperty location = locations.GetArrayElementAtIndex(i);
            if (location == null)
                continue;

            string id = GetString(location, "id");
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                continue;

            entries.Add(new Entry
            {
                Id = id,
                DisplayName = GetString(location, "displayName"),
                Description = GetString(location, "description"),
                LocationType = GetEnumDisplay(location, "locationType"),
            });
        }

        return entries;
    }

    private static string[] BuildOptions(List<Entry> entries, string current, int selectedEntryIndex)
    {
        int extra = selectedEntryIndex >= 0 ? 0 : 1;
        var options = new string[entries.Count + extra];
        int offset = 0;
        if (extra == 1)
        {
            options[0] = string.IsNullOrWhiteSpace(current)
                ? "Select destination..."
                : "Missing: " + current;
            offset = 1;
        }

        for (int i = 0; i < entries.Count; i++)
            options[i + offset] = entries[i].PopupLabel;

        return options;
    }

    private static int FindEntryIndex(List<Entry> entries, string id)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Id == id)
                return i;

        return -1;
    }

    private static string GetString(SerializedProperty parent, string propertyName)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        return property == null ? string.Empty : property.stringValue;
    }

    private static string GetEnumDisplay(SerializedProperty parent, string propertyName)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.Enum)
            return string.Empty;

        int index = property.enumValueIndex;
        if (index < 0 || index >= property.enumDisplayNames.Length)
            return string.Empty;

        return property.enumDisplayNames[index];
    }

    private struct Entry
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string LocationType;

        public string PopupLabel =>
            string.IsNullOrWhiteSpace(DisplayName)
                ? Id
                : Id + " - " + DisplayName;
    }
}
