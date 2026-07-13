// 일회성 — 실행·검증 후 삭제 예정.
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class AilmentCanvasSlotSetupBuilder
{
    private const string MainScenePath = "Assets/Scenes/Main.unity";
    private const string PanelName = "ArenaHealthBarPanel";
    private const string IconTablePath = "Assets/Scriptable/Icon/StatusEffectIconTable.asset";
    private const string PrefabFolderPath = "Assets/Perfabs/UI";
    private const string PrefabPath = PrefabFolderPath + "/AilmentCanvasSlot.prefab";

    private const float SlotSize = 24f;
    private const float StackTextHeight = 16f;
    private const float StackTextFontSize = 13f;
    private const int UiLayer = 5;
    private const float StripGap = 4f;
    private const float StripPaddingVertical = 2f;
    private const float BossBarHeight = 54f;
    private const float EliteBarHeight = 44f;
    private const float BossSlotScale = 1.5f;
    private const float EliteSlotScale = 1f;
    private const float PanelBottomPadding = 30f;

    private static readonly string[] s_BossRowNames = { "BossRow_1", "BossRow_2" };
    private static readonly string[] s_EliteRowNames =
    {
        "EliteRow_1",
        "EliteRow_2",
        "EliteRow_3"
    };

    [MenuItem("JBRogLike/Setup/Create Ailment Canvas Slot Prefab")]
    public static void CreateAilmentCanvasSlotPrefab()
    {
        StatusEffectIconTable table = LoadIconTable();
        if (table == null)
            return;

        AilmentCanvasSlotView prefab = CreateOrUpdatePrefab();
        if (prefab == null)
            return;

        WireCanvasSlotPrefab(table, prefab);
        AssetDatabase.SaveAssets();
        Debug.Log("[AilmentCanvasSlotSetupBuilder] Canvas slot prefab and icon table wired.", prefab);
    }

    [MenuItem("JBRogLike/Setup/Arena Ailment Slot S2.5")]
    public static void BuildArenaAilmentSlotS25()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[AilmentCanvasSlotSetupBuilder] Exit Play Mode before scene setup.");
            return;
        }

        StatusEffectIconTable table = LoadIconTable();
        if (table == null)
            return;

        AilmentCanvasSlotView prefab = CreateOrUpdatePrefab();
        if (prefab == null)
            return;

        WireCanvasSlotPrefab(table, prefab);
        AssetDatabase.SaveAssets();

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != MainScenePath)
        {
            Debug.LogError("[AilmentCanvasSlotSetupBuilder] Open Assets/Scenes/Main.unity before scene setup.");
            return;
        }

        bool hadUnsavedChanges = scene.isDirty;
        if (hadUnsavedChanges)
        {
            Debug.LogWarning(
                "[AilmentCanvasSlotSetupBuilder] Main scene has unsaved changes. " +
                "Prefab/table were updated, but scene migration was not saved.");
            return;
        }

        ArenaHealthBarPanel panel = FindPanel(scene);
        if (panel == null)
        {
            Debug.LogError("[AilmentCanvasSlotSetupBuilder] ArenaHealthBarPanel was not found.");
            return;
        }

        int configuredRows = 0;
        int removedSlots = 0;
        int validationErrors = 0;

        ConfigureRows(
            panel,
            s_BossRowNames,
            BossBarHeight,
            BossSlotScale,
            table,
            ref configuredRows,
            ref removedSlots,
            ref validationErrors);
        ConfigureRows(
            panel,
            s_EliteRowNames,
            EliteBarHeight,
            EliteSlotScale,
            table,
            ref configuredRows,
            ref removedSlots,
            ref validationErrors);

        ConfigurePanelHeight(panel);

        if (configuredRows != s_BossRowNames.Length + s_EliteRowNames.Length)
            validationErrors++;

        EditorUtility.SetDirty(panel);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = panel.gameObject;

        if (!saved)
        {
            Debug.LogError("[AilmentCanvasSlotSetupBuilder] Main scene save failed.", panel);
            return;
        }

        if (validationErrors > 0)
        {
            Debug.LogError(
                $"[AilmentCanvasSlotSetupBuilder] Setup saved with {validationErrors} validation error(s). " +
                $"Rows={configuredRows}, RemovedSlots={removedSlots}, PreExistingDirty={hadUnsavedChanges}.",
                panel);
            return;
        }

        Debug.Log(
            $"[AilmentCanvasSlotSetupBuilder] Setup and wiring verified. " +
            $"Rows={configuredRows}, RemovedSlots={removedSlots}, PreExistingDirty={hadUnsavedChanges}.",
            panel);
    }

    private static StatusEffectIconTable LoadIconTable()
    {
        StatusEffectIconTable table = AssetDatabase.LoadAssetAtPath<StatusEffectIconTable>(IconTablePath);
        if (table == null)
            Debug.LogError("[AilmentCanvasSlotSetupBuilder] StatusEffectIconTable missing at " + IconTablePath + ".");
        return table;
    }

    private static AilmentCanvasSlotView CreateOrUpdatePrefab()
    {
        EnsureFolder(PrefabFolderPath);

        GameObject root;
        bool loadedExisting = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        if (loadedExisting)
            root = PrefabUtility.LoadPrefabContents(PrefabPath);
        else
            root = new GameObject("AilmentCanvasSlot", typeof(RectTransform));

        try
        {
            root.name = "AilmentCanvasSlot";
            ConfigureSlotRoot(root);
            ConfigureStackText(root.transform);

            AilmentCanvasSlotView view = GetOrAdd<AilmentCanvasSlotView>(root);
            Image iconImage = root.GetComponent<Image>();
            TMP_Text stackText = root.transform.Find("StackText")?.GetComponent<TMP_Text>();
            WireSlotView(view, iconImage, stackText);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (saved == null)
            {
                Debug.LogError("[AilmentCanvasSlotSetupBuilder] Failed to save " + PrefabPath + ".");
                return null;
            }
        }
        finally
        {
            if (loadedExisting)
                PrefabUtility.UnloadPrefabContents(root);
            else
                UnityEngine.Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<AilmentCanvasSlotView>(PrefabPath);
    }

    private static void ConfigureSlotRoot(GameObject root)
    {
        root.layer = UiLayer;

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(SlotSize, SlotSize);
        rect.localScale = Vector3.one;

        Image image = GetOrAdd<Image>(root);
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;

        LayoutElement layout = GetOrAdd<LayoutElement>(root);
        layout.minWidth = SlotSize;
        layout.preferredWidth = SlotSize;
        layout.minHeight = SlotSize;
        layout.preferredHeight = SlotSize;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;
    }

    private static void ConfigureStackText(Transform root)
    {
        Transform existing = root.Find("StackText");
        GameObject textObject = existing != null
            ? existing.gameObject
            : new GameObject("StackText", typeof(RectTransform), typeof(TextMeshProUGUI));
        if (existing == null)
            textObject.transform.SetParent(root, false);

        textObject.layer = root.gameObject.layer;

        TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(textObject);
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(SlotSize, StackTextHeight);

        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = StackTextFontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.BottomRight;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.text = string.Empty;
        textObject.SetActive(false);
    }

    private static void WireSlotView(AilmentCanvasSlotView view, Image iconImage, TMP_Text stackText)
    {
        var serializedView = new SerializedObject(view);
        serializedView.FindProperty("iconImage").objectReferenceValue = iconImage;
        serializedView.FindProperty("stackText").objectReferenceValue = stackText;
        serializedView.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireCanvasSlotPrefab(StatusEffectIconTable table, AilmentCanvasSlotView prefab)
    {
        var serializedTable = new SerializedObject(table);
        SerializedProperty property = serializedTable.FindProperty("canvasSlotPrefab");
        if (property == null)
        {
            Debug.LogError("[AilmentCanvasSlotSetupBuilder] canvasSlotPrefab field was not found.", table);
            return;
        }

        property.objectReferenceValue = prefab;
        serializedTable.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(table);
    }

    private static ArenaHealthBarPanel FindPanel(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            ArenaHealthBarPanel[] panels = root.GetComponentsInChildren<ArenaHealthBarPanel>(true);
            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] != null && panels[i].name == PanelName)
                    return panels[i];
            }
        }

        return null;
    }

    private static void ConfigureRows(
        ArenaHealthBarPanel panel,
        string[] rowNames,
        float barHeight,
        float scale,
        StatusEffectIconTable table,
        ref int configuredRows,
        ref int removedSlots,
        ref int validationErrors)
    {
        for (int i = 0; i < rowNames.Length; i++)
        {
            Transform rowTransform = panel.transform.Find(rowNames[i]);
            ArenaHealthBarRowUI row = rowTransform != null
                ? rowTransform.GetComponent<ArenaHealthBarRowUI>()
                : null;
            if (row == null)
            {
                validationErrors++;
                Debug.LogError("[AilmentCanvasSlotSetupBuilder] Missing row: " + rowNames[i] + ".", panel);
                continue;
            }

            ArenaAilmentStripUI strip = row.transform.Find("Strip")?.GetComponent<ArenaAilmentStripUI>();
            if (strip == null)
            {
                validationErrors++;
                Debug.LogError("[AilmentCanvasSlotSetupBuilder] Missing Strip on " + row.name + ".", row);
                continue;
            }

            float stripHeight = SlotSize * scale + StripPaddingVertical;
            float rowHeight = barHeight + StripGap + stripHeight;

            removedSlots += RemoveLegacySlots(strip.transform);
            ConfigureStrip(strip, table, barHeight, stripHeight, scale);
            ConfigureRowHeight(row, rowHeight);
            row.gameObject.SetActive(false);

            configuredRows++;
            if (!ValidateRow(row, strip, table, scale))
            {
                validationErrors++;
                Debug.LogError("[AilmentCanvasSlotSetupBuilder] Validation failed for " + row.name + ".", row);
            }
        }
    }

    private static int RemoveLegacySlots(Transform stripTransform)
    {
        int removed = 0;
        for (int i = stripTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = stripTransform.GetChild(i);
            if (!child.name.StartsWith("Slot_", StringComparison.Ordinal))
                continue;

            Undo.DestroyObjectImmediate(child.gameObject);
            removed++;
        }

        return removed;
    }

    private static void ConfigureStrip(
        ArenaAilmentStripUI strip,
        StatusEffectIconTable table,
        float barHeight,
        float stripHeight,
        float scale)
    {
        RectTransform rect = strip.GetComponent<RectTransform>();
        Undo.RecordObject(rect, "Configure Arena Ailment Strip");
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -(barHeight + StripGap));
        rect.sizeDelta = new Vector2(0f, stripHeight);

        HorizontalLayoutGroup layout = strip.GetComponent<HorizontalLayoutGroup>();
        Undo.RecordObject(layout, "Configure Arena Ailment Strip Layout");
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childScaleWidth = true;
        layout.childScaleHeight = true;

        var serializedStrip = new SerializedObject(strip);
        serializedStrip.FindProperty("iconTable").objectReferenceValue = table;
        serializedStrip.FindProperty("slotScale").floatValue = scale;
        serializedStrip.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(strip);
    }

    private static void ConfigureRowHeight(ArenaHealthBarRowUI row, float rowHeight)
    {
        RectTransform rowRect = row.GetComponent<RectTransform>();
        Undo.RecordObject(rowRect, "Resize Arena Health Bar Row");
        rowRect.sizeDelta = new Vector2(rowRect.sizeDelta.x, rowHeight);

        LayoutElement layout = row.GetComponent<LayoutElement>();
        Undo.RecordObject(layout, "Resize Arena Health Bar Row Layout");
        layout.minHeight = rowHeight;
        layout.preferredHeight = rowHeight;
        layout.flexibleHeight = 0f;

        EditorUtility.SetDirty(rowRect);
        EditorUtility.SetDirty(layout);
        EditorUtility.SetDirty(row);
    }

    private static void ConfigurePanelHeight(ArenaHealthBarPanel panel)
    {
        float bossRowHeight = BossBarHeight + StripGap + SlotSize * BossSlotScale + StripPaddingVertical;
        float eliteRowHeight = EliteBarHeight + StripGap + SlotSize * EliteSlotScale + StripPaddingVertical;
        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        float spacing = layout != null ? layout.spacing : 10f;
        float contentHeight = bossRowHeight * s_BossRowNames.Length +
                              eliteRowHeight * s_EliteRowNames.Length +
                              spacing * (s_BossRowNames.Length + s_EliteRowNames.Length - 1);

        RectTransform rect = panel.GetComponent<RectTransform>();
        Undo.RecordObject(rect, "Resize Arena Health Bar Panel");
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, contentHeight + PanelBottomPadding);
        EditorUtility.SetDirty(rect);
    }

    private static bool ValidateRow(
        ArenaHealthBarRowUI row,
        ArenaAilmentStripUI strip,
        StatusEffectIconTable table,
        float expectedScale)
    {
        if (strip.transform.childCount != 0)
            return false;

        HorizontalLayoutGroup layout = strip.GetComponent<HorizontalLayoutGroup>();
        if (layout == null || !layout.childScaleWidth || !layout.childScaleHeight)
            return false;

        var serializedStrip = new SerializedObject(strip);
        SerializedProperty tableProperty = serializedStrip.FindProperty("iconTable");
        SerializedProperty scaleProperty = serializedStrip.FindProperty("slotScale");
        if (tableProperty == null || tableProperty.objectReferenceValue != table ||
            scaleProperty == null || !Mathf.Approximately(scaleProperty.floatValue, expectedScale))
        {
            return false;
        }

        return row != null && !row.gameObject.activeSelf;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string parent = System.IO.Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
        string name = System.IO.Path.GetFileName(folderPath);
        if (!string.IsNullOrEmpty(parent) && AssetDatabase.IsValidFolder(parent))
            AssetDatabase.CreateFolder(parent, name);
    }

    private static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }
}
