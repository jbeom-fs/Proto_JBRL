using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

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
}
