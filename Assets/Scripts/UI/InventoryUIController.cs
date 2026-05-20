using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class InventoryUIController : MonoBehaviour
{
    private static InventoryUIController s_Active;

    [SerializeField] private GameObject root;
    [SerializeField] private Transform slotContent;
    [SerializeField] private InventorySlotUI slotTemplate;
    [SerializeField] private TMP_Text emptyText;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerInputReader inputReader;

    private readonly List<InventorySlotUI> _slots = new List<InventorySlotUI>(16);
    private bool _warnedMissingInventory;
    private bool _warnedMissingInputReader;

    public static InventoryUIController Active => s_Active;
    public static bool IsOpen => s_Active != null && s_Active.IsInventoryOpen;

    public bool IsInventoryOpen => root != null && root.activeSelf;

    private void Awake()
    {
        if (s_Active != null && s_Active != this)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[InventoryUIController] Duplicate instance detected. Latest instance is now active.", this);
#endif
        }

        s_Active = this;
        EnsureDefaultUi();
        if (root != null)
            root.SetActive(false);
    }

    private void OnEnable()
    {
        if (playerInventory != null)
            playerInventory.OnInventoryChanged += Refresh;
    }

    private void OnDisable()
    {
        if (playerInventory != null)
            playerInventory.OnInventoryChanged -= Refresh;

        if (s_Active == this)
            s_Active = null;
    }

    private void Update()
    {
        if (DeveloperConsoleUI.IsOpen)
        {
            if (IsInventoryOpen)
                Close();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (IsInventoryOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        if (inputReader == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_warnedMissingInputReader)
            {
                Debug.LogWarning("[InventoryUIController] inputReader is not assigned.", this);
                _warnedMissingInputReader = true;
            }
#endif
            return;
        }

        if (inputReader.InventoryPressedThisFrame)
            Toggle();
    }

    public void Toggle()
    {
        if (IsInventoryOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (root == null)
            return;

        root.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (root != null)
            root.SetActive(false);
    }

    private void Refresh()
    {
        if (playerInventory == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_warnedMissingInventory)
            {
                Debug.LogWarning("[InventoryUIController] playerInventory is not assigned.", this);
                _warnedMissingInventory = true;
            }
#endif
            return;
        }

        IReadOnlyList<InventoryItemStack> items = playerInventory.Items;
        EnsureSlotCount(items.Count);

        for (int i = 0; i < _slots.Count; i++)
        {
            if (i < items.Count)
                _slots[i].Bind(items[i].Item, items[i].Count);
            else
                _slots[i].gameObject.SetActive(false);
        }

        if (emptyText != null)
            emptyText.gameObject.SetActive(items.Count == 0);
    }

    private void EnsureSlotCount(int count)
    {
        if (slotTemplate == null || slotContent == null)
            return;

        while (_slots.Count < count)
        {
            InventorySlotUI slot = Instantiate(slotTemplate, slotContent);
            slot.gameObject.SetActive(false);
            _slots.Add(slot);
        }
    }

    private void EnsureDefaultUi()
    {
        if (root != null && slotContent != null && slotTemplate != null)
            return;

        // Parent InventoryRoot to the Canvas so the drag coordinate space
        // aligns with the boundary used by UIDraggableWindow.
        Canvas canvas = GetComponentInParent<Canvas>();
        Transform panelParent = canvas != null ? canvas.transform : transform;

        RectTransform rootRect = CreateRect("InventoryRoot", panelParent);
        root = rootRect.gameObject;
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.sizeDelta = new Vector2(360f, 420f);

        Image rootImage = root.AddComponent<Image>();
        rootImage.color = new Color(0.08f, 0.09f, 0.1f, 0.88f);

        // TitleBar: drag handle. Image provides the raycast target for UIDraggableWindow.
        RectTransform titleBarRect = CreateRect("TitleBar", rootRect);
        titleBarRect.anchorMin = new Vector2(0f, 1f);
        titleBarRect.anchorMax = new Vector2(1f, 1f);
        titleBarRect.pivot = new Vector2(0.5f, 1f);
        titleBarRect.anchoredPosition = Vector2.zero;
        titleBarRect.sizeDelta = new Vector2(0f, 48f);

        Image titleBarImage = titleBarRect.gameObject.AddComponent<Image>();
        titleBarImage.color = new Color(0.15f, 0.17f, 0.2f, 1f);

        TextMeshProUGUI title = CreateText("TitleText", titleBarRect, "Inventory", 22f);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.offsetMin = new Vector2(12f, 0f);
        titleRect.offsetMax = new Vector2(-12f, 0f);
        title.raycastTarget = false;

        UIDraggableWindow draggable = titleBarRect.gameObject.AddComponent<UIDraggableWindow>();
        RectTransform boundaryRect = canvas != null ? (RectTransform)canvas.transform : (RectTransform)panelParent;
        draggable.Initialize(rootRect, boundaryRect);

        ScrollRect scrollRect = CreateScrollView(rootRect);
        slotContent = scrollRect.content;

        emptyText = CreateText("EmptyText", rootRect, "Inventory empty", 18f);
        RectTransform emptyRect = emptyText.rectTransform;
        emptyRect.anchorMin = new Vector2(0f, 0f);
        emptyRect.anchorMax = new Vector2(1f, 1f);
        emptyRect.offsetMin = new Vector2(24f, 24f);
        emptyRect.offsetMax = new Vector2(-24f, -56f);

        slotTemplate = CreateSlotTemplate(slotContent);
    }

    private static ScrollRect CreateScrollView(RectTransform parent)
    {
        RectTransform scrollRectTransform = CreateRect("SlotScrollView", parent);
        scrollRectTransform.anchorMin = new Vector2(0f, 0f);
        scrollRectTransform.anchorMax = new Vector2(1f, 1f);
        scrollRectTransform.offsetMin = new Vector2(16f, 16f);
        scrollRectTransform.offsetMax = new Vector2(-16f, -56f);

        ScrollRect scrollRect = scrollRectTransform.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 22f;

        RectTransform viewport = CreateRect("Viewport", scrollRectTransform);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.18f);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = new Vector2(8f, 0f);
        content.offsetMax = new Vector2(-8f, 0f);

        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 6f;

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewport;
        scrollRect.content = content;
        return scrollRect;
    }

    private static InventorySlotUI CreateSlotTemplate(Transform parent)
    {
        RectTransform slotRect = CreateRect("InventorySlotTemplate", parent);
        slotRect.sizeDelta = new Vector2(0f, 44f);
        Image background = slotRect.gameObject.AddComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.08f);

        RectTransform iconRect = CreateRect("Icon", slotRect);
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(8f, 0f);
        iconRect.sizeDelta = new Vector2(32f, 32f);
        Image icon = iconRect.gameObject.AddComponent<Image>();

        TextMeshProUGUI nameText = CreateText("NameText", slotRect, string.Empty, 16f);
        RectTransform nameRect = nameText.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.offsetMin = new Vector2(48f, 0f);
        nameRect.offsetMax = new Vector2(-56f, 0f);
        nameText.alignment = TextAlignmentOptions.MidlineLeft;

        TextMeshProUGUI countText = CreateText("CountText", slotRect, string.Empty, 15f);
        RectTransform countRect = countText.rectTransform;
        countRect.anchorMin = new Vector2(1f, 0f);
        countRect.anchorMax = new Vector2(1f, 1f);
        countRect.pivot = new Vector2(1f, 0.5f);
        countRect.anchoredPosition = new Vector2(-10f, 0f);
        countRect.sizeDelta = new Vector2(48f, 0f);
        countText.alignment = TextAlignmentOptions.MidlineRight;

        InventorySlotUI slot = slotRect.gameObject.AddComponent<InventorySlotUI>();
        slot.SetReferences(icon, countText, nameText);
        slot.gameObject.SetActive(false);
        return slot;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }
}
