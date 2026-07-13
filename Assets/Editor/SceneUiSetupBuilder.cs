// 일회성 — 실행·검증 후 삭제 예정.
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class SceneUiSetupBuilder
{
    private const string MainScenePath = "Assets/Scenes/Main.unity";
    private const string PanelName = "ArenaHealthBarPanel";
    private const int BossRowCount = 2;
    private const int EliteRowCount = 3;

    [MenuItem("JBRogLike/Setup/Arena Health Bar UI")]
    public static void BuildArenaHealthBarUi()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[SceneUiSetupBuilder] Exit Play Mode before building scene UI.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != MainScenePath)
        {
            Debug.LogError("[SceneUiSetupBuilder] Open Assets/Scenes/Main.unity before running this menu.");
            return;
        }

        Canvas canvas = FindOverlayCanvas(scene);
        if (canvas == null)
        {
            Debug.LogError("[SceneUiSetupBuilder] Screen Space Overlay Canvas was not found.");
            return;
        }

        ArenaHealthBarPanel panel = FindPanel(canvas);
        if (panel == null)
            panel = CreatePanel(canvas.transform);

        ArenaHealthBarRowUI[] bossRows = ResolveRows(panel.transform, "BossRow", BossRowCount, true);
        ArenaHealthBarRowUI[] eliteRows = ResolveRows(panel.transform, "EliteRow", EliteRowCount, false);

        WirePanel(panel, bossRows, eliteRows);
        WireEncounterControllers(scene, panel);

        panel.transform.SetAsLastSibling();
        panel.gameObject.SetActive(true);
        SetRowsInactive(bossRows);
        SetRowsInactive(eliteRows);

        EditorUtility.SetDirty(panel);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = panel.gameObject;

        Debug.Log("[SceneUiSetupBuilder] Arena health bar UI created and wired in Main scene.", panel);
    }

    private static Canvas FindOverlayCanvas(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].renderMode == RenderMode.ScreenSpaceOverlay)
                    return canvases[i];
            }
        }

        return null;
    }

    private static ArenaHealthBarPanel FindPanel(Canvas canvas)
    {
        ArenaHealthBarPanel[] panels = canvas.GetComponentsInChildren<ArenaHealthBarPanel>(true);
        return panels.Length > 0 ? panels[0] : null;
    }

    private static ArenaHealthBarPanel CreatePanel(Transform canvasTransform)
    {
        var panelObject = new GameObject(
            PanelName,
            typeof(RectTransform),
            typeof(VerticalLayoutGroup),
            typeof(ArenaHealthBarPanel));
        Undo.RegisterCreatedObjectUndo(panelObject, "Create Arena Health Bar UI");
        SetUiParent(panelObject, canvasTransform);

        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -36f);
        rect.sizeDelta = new Vector2(920f, 360f);

        VerticalLayoutGroup layout = panelObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 0, 0);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        return panelObject.GetComponent<ArenaHealthBarPanel>();
    }

    private static ArenaHealthBarRowUI[] ResolveRows(
        Transform panelTransform,
        string rowPrefix,
        int count,
        bool isBoss)
    {
        var rows = new ArenaHealthBarRowUI[count];
        for (int i = 0; i < count; i++)
        {
            string rowName = rowPrefix + "_" + (i + 1);
            Transform existing = panelTransform.Find(rowName);
            ArenaHealthBarRowUI row = existing != null
                ? existing.GetComponent<ArenaHealthBarRowUI>()
                : null;

            if (row == null)
                row = CreateRow(panelTransform, rowName, isBoss);

            WireRow(row);
            rows[i] = row;
        }

        return rows;
    }

    private static ArenaHealthBarRowUI CreateRow(
        Transform panelTransform,
        string rowName,
        bool isBoss)
    {
        var rowObject = new GameObject(
            rowName,
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(ArenaHealthBarRowUI));
        SetUiParent(rowObject, panelTransform);

        float width = isBoss ? 820f : 680f;
        float height = isBoss ? 54f : 44f;

        RectTransform rowRect = rowObject.GetComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(width, height);

        LayoutElement layout = rowObject.GetComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.minHeight = height;
        layout.preferredHeight = height;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        Image background = CreateImageChild(
            rowObject.transform,
            "BG",
            new Color(0.06f, 0.06f, 0.08f, 0.92f),
            uiSprite);

        Image fill = CreateImageChild(
            rowObject.transform,
            "Fill",
            isBoss
                ? new Color(0.78f, 0.12f, 0.12f, 1f)
                : new Color(0.92f, 0.48f, 0.10f, 1f),
            uiSprite);
        RectTransform fillRect = fill.rectTransform;
        fillRect.offsetMin = new Vector2(4f, 4f);
        fillRect.offsetMax = new Vector2(-4f, -4f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;

        TMP_Text nameText = CreateTextChild(rowObject.transform, isBoss ? 28f : 23f);
        nameText.text = isBoss ? "BOSS" : "ELITE";

        WireRow(rowObject.GetComponent<ArenaHealthBarRowUI>(), nameText, fill, background);
        rowObject.SetActive(false);
        return rowObject.GetComponent<ArenaHealthBarRowUI>();
    }

    private static Image CreateImageChild(
        Transform parent,
        string name,
        Color color,
        Sprite sprite)
    {
        var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        SetUiParent(imageObject, parent);

        RectTransform rect = imageObject.GetComponent<RectTransform>();
        SetStretch(rect);

        Image image = imageObject.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text CreateTextChild(Transform parent, float fontSize)
    {
        var textObject = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
        SetUiParent(textObject, parent);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        SetStretch(rect);
        rect.offsetMin = new Vector2(12f, 0f);
        rect.offsetMax = new Vector2(-12f, 0f);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private static void WireRow(ArenaHealthBarRowUI row)
    {
        if (row == null)
            return;

        Transform rowTransform = row.transform;
        TMP_Text nameText = rowTransform.Find("Name")?.GetComponent<TMP_Text>();
        Image fill = rowTransform.Find("Fill")?.GetComponent<Image>();
        Image background = rowTransform.Find("BG")?.GetComponent<Image>();
        WireRow(row, nameText, fill, background);
    }

    private static void WireRow(
        ArenaHealthBarRowUI row,
        TMP_Text nameText,
        Image fill,
        Image background)
    {
        var serializedRow = new SerializedObject(row);
        serializedRow.FindProperty("nameText").objectReferenceValue = nameText;
        serializedRow.FindProperty("hpFill").objectReferenceValue = fill;
        serializedRow.FindProperty("backgroundImage").objectReferenceValue = background;
        serializedRow.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(row);
    }

    private static void WirePanel(
        ArenaHealthBarPanel panel,
        ArenaHealthBarRowUI[] bossRows,
        ArenaHealthBarRowUI[] eliteRows)
    {
        var serializedPanel = new SerializedObject(panel);
        SetObjectArray(serializedPanel.FindProperty("bossRows"), bossRows);
        SetObjectArray(serializedPanel.FindProperty("eliteRows"), eliteRows);
        serializedPanel.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireEncounterControllers(Scene scene, ArenaHealthBarPanel panel)
    {
        foreach (EliteArenaEncounterController controller in FindInScene<EliteArenaEncounterController>(scene))
            SetPanelReference(controller, panel);

        foreach (BossEncounterController controller in FindInScene<BossEncounterController>(scene))
            SetPanelReference(controller, panel);
    }

    private static void SetPanelReference(Object controller, ArenaHealthBarPanel panel)
    {
        var serializedController = new SerializedObject(controller);
        SerializedProperty property = serializedController.FindProperty("healthBarPanel");
        if (property == null)
        {
            Debug.LogError("[SceneUiSetupBuilder] healthBarPanel field was not found.", controller);
            return;
        }

        property.objectReferenceValue = panel;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
    }

    private static List<T> FindInScene<T>(Scene scene) where T : Component
    {
        var results = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects())
            results.AddRange(root.GetComponentsInChildren<T>(true));
        return results;
    }

    private static void SetObjectArray<T>(SerializedProperty property, T[] values)
        where T : Object
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void SetRowsInactive(ArenaHealthBarRowUI[] rows)
    {
        for (int i = 0; i < rows.Length; i++)
            rows[i].gameObject.SetActive(false);
    }

    private static void SetUiParent(GameObject child, Transform parent)
    {
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
    }

    private static void SetStretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }
}
