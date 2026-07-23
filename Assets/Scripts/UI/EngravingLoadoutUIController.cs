using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class EngravingLoadoutUIController : MonoBehaviour
{
    private enum EngravingTab
    {
        Active,
        Passive
    }

    [SerializeField] private GameObject panel;
    [SerializeField] private EngravingLoadout loadout;
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private Button activeTabButton;
    [SerializeField] private Button passiveTabButton;
    [SerializeField] private TMP_Text selectedFormText;
    [SerializeField] private Transform slotContainer;
    [SerializeField] private EngravingSlotUI slotButtonTemplate;
    [SerializeField] private Sprite emptySlotSprite;
    [SerializeField] private Sprite lockSprite;
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

    private readonly List<EngravingSlotUI> _slots =
        new List<EngravingSlotUI>(EngravingLoadout.SlotCount);
    private readonly List<Button> _poolButtons = new List<Button>(8);
    private readonly List<TMP_Text> _poolTexts = new List<TMP_Text>(8);
    private readonly SkillData[] _stagedSlots = new SkillData[EngravingLoadout.SlotCount];
    private readonly List<SkillData> _stagedPool = new List<SkillData>(8);
    private readonly PassiveEngravingData[] _stagedPassiveSlots =
        new PassiveEngravingData[EngravingLoadout.PassiveSlotCount];
    private readonly List<PassiveEngravingData> _stagedPassivePool = new List<PassiveEngravingData>(8);
    private readonly List<SkillData> _activeValidationBuffer = new List<SkillData>(12);
    private readonly List<PassiveEngravingData> _passiveValidationBuffer =
        new List<PassiveEngravingData>(12);

    private PlayerFormId _form;
    private EngravingTab _activeTab;
    private int _selectedSlot = -1;
    private EngravingStation _owner;
    private System.Action _dialogYesAction;
    private bool _warnedMissingReferences;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (activeTabButton != null)
            activeTabButton.onClick.AddListener(HandleActiveTabClicked);

        if (passiveTabButton != null)
            passiveTabButton.onClick.AddListener(HandlePassiveTabClicked);

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
        _activeTab = EngravingTab.Active;
        _selectedSlot = -1;

        for (int i = 0; i < EngravingLoadout.SlotCount; i++)
            _stagedSlots[i] = loadout.GetSlot(_form, i);

        _stagedPool.Clear();
        int poolCount = loadout.PoolCount(_form);
        for (int i = 0; i < poolCount; i++)
            _stagedPool.Add(loadout.GetPoolAt(_form, i));

        for (int i = 0; i < EngravingLoadout.PassiveSlotCount; i++)
            _stagedPassiveSlots[i] = loadout.GetPassiveSlot(_form, i);

        _stagedPassivePool.Clear();
        int passivePoolCount = loadout.PassivePoolCount(_form);
        for (int i = 0; i < passivePoolCount; i++)
            _stagedPassivePool.Add(loadout.GetPassivePoolAt(_form, i));

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
        if (_activeTab == EngravingTab.Passive && slot >= CurrentPassiveUnlockCount())
            return;

        _selectedSlot = slot;
        RefreshVisuals();
    }

    private void HandleActiveTabClicked()
    {
        SwitchTab(EngravingTab.Active);
    }

    private void HandlePassiveTabClicked()
    {
        SwitchTab(EngravingTab.Passive);
    }

    private void SwitchTab(EngravingTab tab)
    {
        if (_activeTab == tab)
            return;

        _activeTab = tab;
        _selectedSlot = -1;
        SetFeedback(string.Empty);
        RefreshAll();
    }

    private void HandlePoolClicked(int poolIndex)
    {
        if (_selectedSlot < 0)
        {
            SetFeedback(UiMessages.SelectEngravingSlotFirst);
            return;
        }

        if (_activeTab == EngravingTab.Passive)
        {
            EquipStagedPassive(poolIndex);
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

        SetFeedback(UiMessages.EngravingEquippedUnsaved);
        RefreshAll();
    }

    private void EquipStagedPassive(int poolIndex)
    {
        if ((uint)_selectedSlot >= (uint)EngravingLoadout.PassiveSlotCount ||
            (uint)poolIndex >= (uint)_stagedPassivePool.Count)
            return;

        if (_selectedSlot >= CurrentPassiveUnlockCount())
            return;

        PassiveEngravingData incoming = _stagedPassivePool[poolIndex];
        _stagedPassivePool.RemoveAt(poolIndex);
        PassiveEngravingData displaced = _stagedPassiveSlots[_selectedSlot];
        _stagedPassiveSlots[_selectedSlot] = incoming;
        if (displaced != null)
            _stagedPassivePool.Add(displaced);

        SetFeedback(UiMessages.EngravingEquippedUnsaved);
        RefreshAll();
    }

    private void HandleUnequipClicked()
    {
        if (_selectedSlot < 0)
        {
            SetFeedback(UiMessages.SelectEngravingSlotFirst);
            return;
        }

        if (_activeTab == EngravingTab.Passive)
        {
            UnequipStagedPassive();
            return;
        }

        if ((uint)_selectedSlot >= (uint)EngravingLoadout.SlotCount)
            return;

        SkillData token = _stagedSlots[_selectedSlot];
        if (token == null)
        {
            SetFeedback(UiMessages.EngravingSlotEmpty);
            return;
        }

        _stagedSlots[_selectedSlot] = null;
        _stagedPool.Add(token);
        SetFeedback(UiMessages.EngravingUnequippedUnsaved);
        RefreshAll();
    }

    private void UnequipStagedPassive()
    {
        if ((uint)_selectedSlot >= (uint)EngravingLoadout.PassiveSlotCount)
            return;

        PassiveEngravingData token = _stagedPassiveSlots[_selectedSlot];
        if (token == null)
        {
            SetFeedback(UiMessages.EngravingSlotEmpty);
            return;
        }

        _stagedPassiveSlots[_selectedSlot] = null;
        _stagedPassivePool.Add(token);
        SetFeedback(UiMessages.EngravingUnequippedUnsaved);
        RefreshAll();
    }

    private void RefreshAll()
    {
        if (!IsOpen)
            return;

        if (selectedFormText != null)
            selectedFormText.text = UiMessages.GetFormName(_form);

        RefreshSlots();
        RefreshPool();
        RefreshVisuals();
    }

    private void RefreshSlots()
    {
        int count = _activeTab == EngravingTab.Passive
            ? EngravingLoadout.PassiveSlotCount
            : EngravingLoadout.SlotCount;
        EnsureSlotButtonCount(count);

        int passiveUnlock = _activeTab == EngravingTab.Passive
            ? CurrentPassiveUnlockCount()
            : EngravingLoadout.SlotCount;

        for (int i = 0; i < _slots.Count; i++)
        {
            bool active = i < count;
            _slots[i].gameObject.SetActive(active);
            if (!active)
                continue;

            if (_activeTab == EngravingTab.Passive && i >= passiveUnlock)
            {
                _slots[i].Bind(
                    string.Format(UiMessages.EngravingSlotLockedFormat, i),
                    lockSprite,
                    false);
                continue;
            }

            Sprite iconSprite;
            string displayName;
            if (_activeTab == EngravingTab.Passive)
            {
                PassiveEngravingData passive = _stagedPassiveSlots[i];
                iconSprite = passive != null ? passive.icon : emptySlotSprite;
                displayName = DisplayName(passive);
            }
            else
            {
                SkillData skill = _stagedSlots[i];
                iconSprite = skill != null ? skill.icon : emptySlotSprite;
                displayName = DisplayName(skill);
            }

            if (iconSprite == null)
                iconSprite = emptySlotSprite;

            _slots[i].Bind(
                string.Format(UiMessages.EngravingSlotFormat, i, displayName),
                iconSprite,
                true);
        }
    }

    private void RefreshPool()
    {
        int count = _activeTab == EngravingTab.Passive
            ? _stagedPassivePool.Count
            : _stagedPool.Count;
        EnsurePoolButtonCount(count);
        for (int i = 0; i < _poolButtons.Count; i++)
        {
            if (i < count)
            {
                _poolButtons[i].gameObject.SetActive(true);
                if (_poolTexts[i] != null)
                {
                    _poolTexts[i].text = _activeTab == EngravingTab.Passive
                        ? DisplayName(_stagedPassivePool[i])
                        : DisplayName(_stagedPool[i]);
                }
            }
            else
            {
                _poolButtons[i].gameObject.SetActive(false);
            }
        }
    }

    private void RefreshVisuals()
    {
        SetButtonColor(activeTabButton, _activeTab == EngravingTab.Active);
        SetButtonColor(passiveTabButton, _activeTab == EngravingTab.Passive);

        for (int i = 0; i < _slots.Count; i++)
            SetButtonColor(_slots[i].Button, i == _selectedSlot);
    }

    private void SetButtonColor(Button button, bool selected)
    {
        if (button == null)
            return;

        Image image = button.GetComponent<Image>();
        if (image != null)
            image.color = selected ? selectedColor : normalColor;
    }

    private void EnsureSlotButtonCount(int count)
    {
        if (slotButtonTemplate == null || slotContainer == null)
            return;

        while (_slots.Count < count)
        {
            int index = _slots.Count;
            EngravingSlotUI slot = Instantiate(slotButtonTemplate, slotContainer);
            if (slot.Button != null)
                slot.Button.onClick.AddListener(() => HandleSlotClicked(index));

            slot.gameObject.SetActive(false);
            _slots.Add(slot);
        }
    }

    private int CurrentPassiveUnlockCount()
    {
        return PlayerPassiveSlotUnlocks.Active != null
            ? PlayerPassiveSlotUnlocks.Active.GetUnlockCount(_form)
            : 1;
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
        return EngravingDisplayInfo.TryCreate(skill, out EngravingDisplayInfo info)
            ? DisplayName(info)
            : UiMessages.EmptyEngravingSlot;
    }

    private static string DisplayName(PassiveEngravingData passive)
    {
        return EngravingDisplayInfo.TryCreate(passive, out EngravingDisplayInfo info)
            ? DisplayName(info)
            : UiMessages.EmptyEngravingSlot;
    }

    private static string DisplayName(EngravingDisplayInfo info)
    {
        if (!info.HasGrade)
            return info.Name;

        return string.Format(
            UiMessages.EngravingGradeFormat,
            info.Name,
            UiMessages.GetEngravingGradeName(info.Grade));
    }

    private bool HasRequiredReferences()
    {
        bool valid = panel != null &&
                     loadout != null &&
                     combat != null &&
                     activeTabButton != null &&
                     passiveTabButton != null &&
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
        return IsActiveDirty() || IsPassiveDirty();
    }

    private bool IsPassiveDirty()
    {
        for (int i = 0; i < EngravingLoadout.PassiveSlotCount; i++)
        {
            if (_stagedPassiveSlots[i] != loadout.GetPassiveSlot(_form, i))
                return true;
        }

        return false;
    }

    private bool IsActiveDirty()
    {
        for (int i = 0; i < EngravingLoadout.SlotCount; i++)
        {
            if (_stagedSlots[i] != loadout.GetSlot(_form, i))
                return true;
        }

        return false;
    }

    private bool CanApplyActiveArrangement()
    {
        _activeValidationBuffer.Clear();
        int poolCount = loadout.PoolCount(_form);
        for (int i = 0; i < poolCount; i++)
            _activeValidationBuffer.Add(loadout.GetPoolAt(_form, i));

        for (int i = 0; i < EngravingLoadout.SlotCount; i++)
        {
            SkillData token = loadout.GetSlot(_form, i);
            if (token != null)
                _activeValidationBuffer.Add(token);
        }

        for (int i = 0; i < EngravingLoadout.SlotCount; i++)
        {
            SkillData token = _stagedSlots[i];
            if (token != null && !_activeValidationBuffer.Remove(token))
                return false;
        }

        return true;
    }

    private bool CanApplyPassiveArrangement()
    {
        _passiveValidationBuffer.Clear();
        int poolCount = loadout.PassivePoolCount(_form);
        for (int i = 0; i < poolCount; i++)
            _passiveValidationBuffer.Add(loadout.GetPassivePoolAt(_form, i));

        for (int i = 0; i < EngravingLoadout.PassiveSlotCount; i++)
        {
            PassiveEngravingData token = loadout.GetPassiveSlot(_form, i);
            if (token != null)
                _passiveValidationBuffer.Add(token);
        }

        for (int i = 0; i < EngravingLoadout.PassiveSlotCount; i++)
        {
            PassiveEngravingData token = _stagedPassiveSlots[i];
            if (token != null && !_passiveValidationBuffer.Remove(token))
                return false;
        }

        return true;
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
        bool activeDirty = IsActiveDirty();
        bool passiveDirty = IsPassiveDirty();
        if ((activeDirty && !CanApplyActiveArrangement()) ||
            (passiveDirty && !CanApplyPassiveArrangement()))
        {
            SetFeedback(UiMessages.EngravingApplyFailed);
            return;
        }

        if ((activeDirty && !loadout.ApplyArrangement(_form, _stagedSlots)) ||
            (passiveDirty && !loadout.ApplyPassiveArrangement(_form, _stagedPassiveSlots)))
        {
            SetFeedback(UiMessages.EngravingApplyFailed);
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
