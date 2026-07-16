using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class FormSelectScreenBuilder : EditorWindow
{
    private const string CardPrefabPath = "Assets/Prefabs/UI/FormSelectCard.prefab";
    private const string FontSourcePrefabPath = "Assets/Prefabs/UI/SoulAltarStatRow.prefab";

    private static readonly PlayerFormId[] s_Forms =
    {
        PlayerFormId.Sword,
        PlayerFormId.Dagger,
        PlayerFormId.Freischutz,
        PlayerFormId.Parry
    };

    [SerializeField] private Transform parentCanvas;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private LocationTransitionManager transitionManager;

    [MenuItem("JBRogLike/UI/Build Form Select Screen")]
    private static void Open()
    {
        GetWindow<FormSelectScreenBuilder>(true, "Form Select Screen Builder");
    }

    private void OnEnable()
    {
        if (transitionManager == null)
            transitionManager = FindSceneObject<LocationTransitionManager>();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Form Select Screen", EditorStyles.boldLabel);
        parentCanvas = (Transform)EditorGUILayout.ObjectField(
            "Parent Canvas", parentCanvas, typeof(Transform), true);
        playerInventory = (PlayerInventory)EditorGUILayout.ObjectField(
            "Player Inventory", playerInventory, typeof(PlayerInventory), true);
        transitionManager = (LocationTransitionManager)EditorGUILayout.ObjectField(
            "Transition Manager", transitionManager, typeof(LocationTransitionManager), true);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(
                   parentCanvas == null || playerInventory == null || transitionManager == null))
        {
            if (GUILayout.Button("Build"))
                Build();
        }
    }

    private void Build()
    {
        if (parentCanvas == null || playerInventory == null || transitionManager == null)
        {
            Debug.LogError("[FormSelectScreenBuilder] Parent Canvas, Player Inventory, Transition Manager 지정 필요.");
            return;
        }

        FormSelectScreenUI existing = FindSceneObject<FormSelectScreenUI>();
        if (existing != null && HasConnectedCards(existing))
        {
            Debug.LogWarning("[FormSelectScreenBuilder] cards가 이미 결선됨. 기존 화면 유지.", existing);
            return;
        }

        TMP_FontAsset font = ResolveFont();
        FormSelectCardUI cardPrefab = ResolveOrCreateCardPrefab();
        if (cardPrefab == null)
            return;

        Undo.SetCurrentGroupName("Build Form Select Screen");
        int undoGroup = Undo.GetCurrentGroup();

        GameObject host = CreateUiObject("FormSelectScreenController", parentCanvas);
        FormSelectScreenUI screen = Undo.AddComponent<FormSelectScreenUI>(host);

        GameObject panel = CreateUiObject("FormSelectScreen", parentCanvas);
        SetStretch(panel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image background = Undo.AddComponent<Image>(panel);
        background.color = Color.white;

        GameObject blocker = CreateUiObject("Backdrop", panel.transform);
        SetStretch(blocker.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image blockerImage = Undo.AddComponent<Image>(blocker);
        blockerImage.color = new Color(0.03f, 0.04f, 0.06f, 0.82f);

        TMP_Text title = CreateText("Title", panel.transform, "폼 선택", font, 30f, TextAlignmentOptions.Center);
        SetAnchoredRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(700f, 46f));

        TMP_Text formName = CreateText("FormName", panel.transform, string.Empty, font, 28f, TextAlignmentOptions.Center);
        SetAnchoredRect(formName.rectTransform, new Vector2(0.5f, 0.68f), Vector2.zero, new Vector2(760f, 44f));

        TMP_Text description = CreateText("Description", panel.transform, string.Empty, font, 18f, TextAlignmentOptions.Center);
        SetAnchoredRect(description.rectTransform, new Vector2(0.5f, 0.58f), Vector2.zero, new Vector2(760f, 90f));

        GameObject cardsRoot = CreateUiObject("Cards", panel.transform);
        RectTransform cardsRect = cardsRoot.GetComponent<RectTransform>();
        cardsRect.anchorMin = new Vector2(0.08f, 0.12f);
        cardsRect.anchorMax = new Vector2(0.92f, 0.38f);
        cardsRect.offsetMin = Vector2.zero;
        cardsRect.offsetMax = Vector2.zero;
        HorizontalLayoutGroup cardsLayout = Undo.AddComponent<HorizontalLayoutGroup>(cardsRoot);
        cardsLayout.spacing = 18f;
        cardsLayout.childAlignment = TextAnchor.MiddleCenter;
        cardsLayout.childControlWidth = true;
        cardsLayout.childControlHeight = true;
        cardsLayout.childForceExpandWidth = true;
        cardsLayout.childForceExpandHeight = true;

        FormSelectCardUI[] cards = new FormSelectCardUI[s_Forms.Length];
        for (int i = 0; i < s_Forms.Length; i++)
        {
            GameObject cardObject = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab.gameObject, cardsRoot.transform);
            Undo.RegisterCreatedObjectUndo(cardObject, "Create form select card");
            FormSelectCardUI card = cardObject.GetComponent<FormSelectCardUI>();
            SerializedObject cardObjectSerialized = new SerializedObject(card);
            cardObjectSerialized.FindProperty("form").enumValueIndex = (int)s_Forms[i];
            cardObjectSerialized.ApplyModifiedProperties();
            cards[i] = card;
        }

        GameObject actions = CreateUiObject("Actions", panel.transform);
        RectTransform actionsRect = actions.GetComponent<RectTransform>();
        actionsRect.anchorMin = new Vector2(0.35f, 0.03f);
        actionsRect.anchorMax = new Vector2(0.65f, 0.09f);
        actionsRect.offsetMin = Vector2.zero;
        actionsRect.offsetMax = Vector2.zero;
        HorizontalLayoutGroup actionsLayout = Undo.AddComponent<HorizontalLayoutGroup>(actions);
        actionsLayout.spacing = 16f;
        actionsLayout.childControlWidth = true;
        actionsLayout.childControlHeight = true;
        actionsLayout.childForceExpandWidth = true;
        actionsLayout.childForceExpandHeight = true;
        Button enter = CreateButton("Enter", actions.transform, "입장", font);
        Button exit = CreateButton("Exit", actions.transform, "나가기", font);

        SerializedObject screenObject = new SerializedObject(screen);
        screenObject.FindProperty("panel").objectReferenceValue = panel;
        screenObject.FindProperty("backgroundImage").objectReferenceValue = background;
        screenObject.FindProperty("formNameText").objectReferenceValue = formName;
        screenObject.FindProperty("descriptionText").objectReferenceValue = description;
        SerializedProperty cardsProperty = screenObject.FindProperty("cards");
        cardsProperty.arraySize = cards.Length;
        for (int i = 0; i < cards.Length; i++)
            cardsProperty.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
        screenObject.FindProperty("enterButton").objectReferenceValue = enter;
        screenObject.FindProperty("exitButton").objectReferenceValue = exit;
        screenObject.FindProperty("playerInventory").objectReferenceValue = playerInventory;
        screenObject.FindProperty("transitionManager").objectReferenceValue = transitionManager;
        screenObject.ApplyModifiedProperties();

        panel.SetActive(false);
        EditorUtility.SetDirty(screen);
        EditorSceneManager.MarkSceneDirty(parentCanvas.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = panel;
        Debug.Log(
            "[FormSelectScreenBuilder] 풀스크린 화면, 카드 4개, 버튼 결선 완료. " +
            "수동 처리: ①던전 입구 기존 TeleportService 오브젝트 비활성 " +
            "②같은 위치에 DungeonEntryStation 배치(트리거+sceneUI 결선) " +
            "③dungeonDestinationId에 기존 targetDestinationId 복사 " +
            "④카드 저작물 5종×4폼 입력 ⑤씬 저장 ⑥Play 검증 ⑦빌더 삭제.",
            screen);
    }

    private static FormSelectCardUI ResolveOrCreateCardPrefab()
    {
        FormSelectCardUI existing = AssetDatabase.LoadAssetAtPath<FormSelectCardUI>(CardPrefabPath);
        if (existing != null)
            return existing;

        GameObject root = CreateAssetUiObject("FormSelectCard", null);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(180f, 220f);
        Image raycastImage = root.AddComponent<Image>();
        raycastImage.color = new Color(0f, 0f, 0f, 0.01f);
        Button button = root.AddComponent<Button>();
        button.targetGraphic = raycastImage;

        GameObject visualObject = CreateAssetUiObject("CardVisual", root.transform);
        SetStretch(visualObject.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image cardImage = visualObject.AddComponent<Image>();
        cardImage.enabled = false;
        cardImage.raycastTarget = false;

        GameObject highlightObject = CreateAssetUiObject("SelectedHighlight", root.transform);
        SetStretch(highlightObject.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(-4f, -4f), new Vector2(4f, 4f));
        Image highlightImage = highlightObject.AddComponent<Image>();
        highlightImage.color = new Color(1f, 0.82f, 0.2f, 0.45f);
        highlightImage.raycastTarget = false;
        highlightObject.SetActive(false);

        FormSelectCardUI card = root.AddComponent<FormSelectCardUI>();
        SerializedObject cardObject = new SerializedObject(card);
        cardObject.FindProperty("cardImage").objectReferenceValue = cardImage;
        cardObject.FindProperty("selectedHighlight").objectReferenceValue = highlightObject;
        cardObject.FindProperty("selectButton").objectReferenceValue = button;
        cardObject.ApplyModifiedPropertiesWithoutUndo();

        EnsurePrefabFolder();
        GameObject prefabRoot = PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
        DestroyImmediate(root);
        if (prefabRoot == null)
        {
            Debug.LogError("[FormSelectScreenBuilder] FormSelectCard 프리팹 생성 실패.");
            return null;
        }

        return prefabRoot.GetComponent<FormSelectCardUI>();
    }

    private static bool HasConnectedCards(FormSelectScreenUI screen)
    {
        SerializedObject screenObject = new SerializedObject(screen);
        SerializedProperty cards = screenObject.FindProperty("cards");
        return cards != null && cards.arraySize > 0 && cards.GetArrayElementAtIndex(0).objectReferenceValue != null;
    }

    private static TMP_FontAsset ResolveFont()
    {
        SoulAltarStatRowUI row = AssetDatabase.LoadAssetAtPath<SoulAltarStatRowUI>(FontSourcePrefabPath);
        if (row == null)
            return null;

        SerializedObject rowObject = new SerializedObject(row);
        TMP_Text source = rowObject.FindProperty("summaryText").objectReferenceValue as TMP_Text;
        return source != null ? source.font : null;
    }

    private static Button CreateButton(string objectName, Transform parent, string label, TMP_FontAsset font)
    {
        GameObject root = CreateUiObject(objectName, parent);
        Image image = Undo.AddComponent<Image>(root);
        image.color = new Color(0.72f, 0.86f, 0.62f, 1f);
        Button button = Undo.AddComponent<Button>(root);
        button.targetGraphic = image;
        TMP_Text text = CreateText("Label", root.transform, label, font, 18f, TextAlignmentOptions.Center);
        SetStretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return button;
    }

    private static TMP_Text CreateText(
        string objectName,
        Transform parent,
        string value,
        TMP_FontAsset font,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject root = CreateUiObject(objectName, parent);
        TMP_Text text = Undo.AddComponent<TextMeshProUGUI>(root);
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(gameObject, "Create form select UI");
        gameObject.layer = parent.gameObject.layer;
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static GameObject CreateAssetUiObject(string objectName, Transform parent)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
        if (parent != null)
            gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static void SetStretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void SetAnchoredRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void EnsurePrefabFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI"))
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
    }

    private static T FindSceneObject<T>() where T : Component
    {
        T[] objects = Resources.FindObjectsOfTypeAll<T>();
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null && objects[i].gameObject.scene.IsValid())
                return objects[i];
        }

        return null;
    }
}
