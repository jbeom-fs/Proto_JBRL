using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class EngravingLoadoutUIController : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private EngravingLoadout loadout;
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private TMP_Text selectedFormText;
    [SerializeField] private Transform slotContainer;
    [SerializeField] private Button slotButtonTemplate;
    [SerializeField] private Transform poolContainer;
    [SerializeField] private Button poolButtonTemplate;
    [SerializeField] private Button unequipButton;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private GameObject dialogPanel;
    [SerializeField] private TMP_Text dialogText;
    [SerializeField] private Button dialogYesButton;
    [SerializeField] private Button dialogNoButton;
    [SerializeField] private Color selectedColor = Color.white;
    [SerializeField] private Color normalColor = new Color(0.65f, 0.65f, 0.65f, 1f);

    private readonly List<Button> _slotButtons = new List<Button>(EngravingLoadout.SlotCount);
    private readonly List<TMP_Text> _slotTexts = new List<TMP_Text>(EngravingLoadout.SlotCount);
    private readonly List<Button> _poolButtons = new List<Button>(8);
    private readonly List<TMP_Text> _poolTexts = new List<TMP_Text>(8);
    private readonly SkillData[] _stagedSlots = new SkillData[EngravingLoadout.SlotCount];
    private readonly List<SkillData> _stagedPool = new List<SkillData>(8);

    private PlayerFormId _form;
    private int _selectedSlot = -1;
    private EngravingStation _owner;
    private System.Action _dialogYesAction;
    private bool _warnedMissingReferences;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(RequestCancel);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(RequestCancel);

        if (confirmButton != null)
            confirmButton.onClick.AddListener(RequestConfirm);

        if (unequipButton != null)
            unequipButton.onClick.AddListener(HandleUnequipClicked);

        if (dialogYesButton != null)
            dialogYesButton.onClick.AddListener(HandleDialogYes);

        if (dialogNoButton != null)
            dialogNoButton.onClick.AddListener(HideDialog);

        if (slotButtonTemplate != null)
            slotButtonTemplate.gameObject.SetActive(false);

        if (poolButtonTemplate != null)
            poolButtonTemplate.gameObject.SetActive(false);

        if (panel != null)
            panel.SetActive(false);

        if (dialogPanel != null)
            dialogPanel.SetActive(false);
    }

    private void OnDisable()
    {
        CloseWithoutSave();
    }

    private void Update()
    {
        if (!IsOpen)
            return;

        if (DeveloperConsoleUI.IsOpen)
        {
            CloseWithoutSave();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (dialogPanel != null && dialogPanel.activeSelf)
                HideDialog();
            else
                RequestCancel();
        }
    }

    public void Open()
    {
        Open(null);
    }

    public void Open(EngravingStation owner)
    {
        if (IsOpen)
            return;

        if (!HasRequiredReferences())
            return;

        IsOpen = true;
        _owner = owner;
        _form = combat.CurrentFormId;
        _selectedSlot = -1;

        for (int i = 0; i < EngravingLoadout.SlotCount; i++)
            _stagedSlots[i] = loadout.GetSlot(_form, i);

        _stagedPool.Clear();
        int poolCount = loadout.PoolCount(_form);
        for (int i = 0; i < poolCount; i++)
            _stagedPool.Add(loadout.GetPoolAt(_form, i));

        SetFeedback(string.Empty);
        HideDialog();
        panel.SetActive(true);
        GamePauseController.Active?.Pause(GamePauseSource.EngravingLoadout);
        RefreshAll();
    }

    public void Close()
    {
        CloseWithoutSave();
    }

    private void CloseWithoutSave()
    {
        bool wasOpen = IsOpen;
        IsOpen = false;
        _selectedSlot = -1;
        _owner = null;
        HideDialog();

        if (panel != null)
            panel.SetActive(false);

        if (wasOpen)
            GamePauseController.Active?.Resume(GamePauseSource.EngravingLoadout);
    }

    private void HandleSlotClicked(int slot)
    {
        _selectedSlot = slot;
        RefreshVisuals();
    }

    private void HandlePoolClicked(int poolIndex)
    {
        if (_selectedSlot < 0)
        {
            SetFeedback("Select a slot first");
            return;
        }

        if ((uint)_selectedSlot >= (uint)EngravingLoadout.SlotCount ||
            (uint)poolIndex >= (uint)_stagedPool.Count)
            return;

        SkillData incoming = _stagedPool[poolIndex];
        _stagedPool.RemoveAt(poolIndex);
        SkillData displaced = _stagedSlots[_selectedSlot];
        _stagedSlots[_selectedSlot] = incoming;
        if (displaced != null)
            _stagedPool.Add(displaced);

        SetFeedback("Equipped (unsaved)");
        RefreshAll();
    }

    private void HandleUnequipClicked()
    {
        if (_selectedSlot < 0)
        {
            SetFeedback("Select a slot first");
            return;
        }

        if ((uint)_selectedSlot >= (uint)EngravingLoadout.SlotCount)
            return;

        SkillData token = _stagedSlots[_selectedSlot];
        if (token == null)
        {
            SetFeedback("Slot empty");
            return;
        }

        _stagedSlots[_selectedSlot] = null;
        _stagedPool.Add(token);
        SetFeedback("Unequipped (unsaved)");
        RefreshAll();
    }

    private void RefreshAll()
    {
        if (!IsOpen)
            return;

        if (selectedFormText != null)
            selectedFormText.text = _form.ToString();

        RefreshSlots();
        RefreshPool();
        RefreshVisuals();
    }

    private void RefreshSlots()
    {
        EnsureSlotButtonCount(EngravingLoadout.SlotCount);
        for (int i = 0; i < _slotButtons.Count; i++)
        {
            bool active = i < EngravingLoadout.SlotCount;
            _slotButtons[i].gameObject.SetActive(active);
            if (active && _slotTexts[i] != null)
                _slotTexts[i].text = "[" + i + "] " + DisplayName(_stagedSlots[i]);
        }
    }

    private void RefreshPool()
    {
        int count = _stagedPool.Count;
        EnsurePoolButtonCount(count);
        for (int i = 0; i < _poolButtons.Count; i++)
        {
            if (i < count)
            {
                _poolButtons[i].gameObject.SetActive(true);
                if (_poolTexts[i] != null)
                    _poolTexts[i].text = DisplayName(_stagedPool[i]);
            }
            else
            {
                _poolButtons[i].gameObject.SetActive(false);
            }
        }
    }

    private void RefreshVisuals()
    {
        for (int i = 0; i < _slotButtons.Count; i++)
        {
            Image image = _slotButtons[i].GetComponent<Image>();
            if (image != null)
                image.color = i == _selectedSlot ? selectedColor : normalColor;
        }
    }

    private void EnsureSlotButtonCount(int count)
    {
        if (slotButtonTemplate == null || slotContainer == null)
            return;

        while (_slotButtons.Count < count)
        {
            int index = _slotButtons.Count;
            Button button = Instantiate(slotButtonTemplate, slotContainer);
            button.onClick.AddListener(() => HandleSlotClicked(index));
            button.gameObject.SetActive(false);
            _slotButtons.Add(button);
            _slotTexts.Add(button.GetComponentInChildren<TMP_Text>(true));
        }
    }

    private void EnsurePoolButtonCount(int count)
    {
        if (poolButtonTemplate == null || poolContainer == null)
            return;

        while (_poolButtons.Count < count)
        {
            int index = _poolButtons.Count;
            Button button = Instantiate(poolButtonTemplate, poolContainer);
            button.onClick.AddListener(() => HandlePoolClicked(index));
            button.gameObject.SetActive(false);
            _poolButtons.Add(button);
            _poolTexts.Add(button.GetComponentInChildren<TMP_Text>(true));
        }
    }

    private static string DisplayName(SkillData skill)
    {
        if (skill == null)
            return "(empty)";

        string skillName = string.IsNullOrWhiteSpace(skill.skillName) ? skill.name : skill.skillName;
        return skill is EngravingData engraving ? skillName + " [" + engraving.grade + "]" : skillName;
    }

    private bool HasRequiredReferences()
    {
        bool valid = panel != null &&
                     loadout != null &&
                     combat != null &&
                     slotContainer != null &&
                     slotButtonTemplate != null &&
                     poolContainer != null &&
                     poolButtonTemplate != null &&
                     confirmButton != null &&
                     cancelButton != null &&
                     dialogPanel != null &&
                     dialogText != null &&
                     dialogYesButton != null &&
                     dialogNoButton != null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!valid && !_warnedMissingReferences)
        {
            Debug.LogWarning("[EngravingLoadoutUIController] Required references are not assigned.", this);
            _warnedMissingReferences = true;
        }
#endif

        return valid;
    }

    private bool IsDirty()
    {
        for (int i = 0; i < EngravingLoadout.SlotCount; i++)
        {
            if (_stagedSlots[i] != loadout.GetSlot(_form, i))
                return true;
        }

        return false;
    }

    private void RequestCancel()
    {
        if (!IsOpen)
            return;

        if (IsDirty())
            ShowDialog(UiMessages.DiscardEngravingChangesConfirmation, CloseWithoutSave);
        else
            CloseWithoutSave();
    }

    private void RequestConfirm()
    {
        if (!IsOpen)
            return;

        if (IsDirty())
            ShowDialog(UiMessages.SaveEngravingChangesConfirmation, CommitAndClose);
        else
            CloseWithoutSave();
    }

    private void CommitAndClose()
    {
        if (!loadout.ApplyArrangement(_form, _stagedSlots))
        {
            SetFeedback("Apply failed");
            return;
        }

        EngravingStation owner = _owner;
        CloseWithoutSave();
        if (owner != null)
            owner.NotifyConsumed();
    }

    private void ShowDialog(string message, System.Action onYes)
    {
        _dialogYesAction = onYes;
        if (dialogText != null)
            dialogText.text = message;
        if (dialogPanel != null)
            dialogPanel.SetActive(true);
    }

    private void HandleDialogYes()
    {
        System.Action action = _dialogYesAction;
        HideDialog();
        action?.Invoke();
    }

    private void HideDialog()
    {
        _dialogYesAction = null;
        if (dialogPanel != null)
            dialogPanel.SetActive(false);
    }

    private void SetFeedback(string message)
    {
        if (feedbackText != null)
            feedbackText.text = message;
    }
}
