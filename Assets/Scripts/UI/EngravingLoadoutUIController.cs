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
    [SerializeField] private EngravingSlotUI slotButtonTemplate;
    [SerializeField] private Sprite emptySlotSprite;
    [SerializeField] private TierColorTable tierColorTable;
    [SerializeField] private Transform poolContainer;
    [SerializeField] private EngravingSlotUI poolButtonTemplate;
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
    private readonly List<EngravingSlotUI> _pool = new List<EngravingSlotUI>(8);
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
            SetFeedback(UiMessages.SelectEngravingSlotFirst);
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

    private void HandleUnequipClicked()
    {
        if (_selectedSlot < 0)
        {
            SetFeedback(UiMessages.SelectEngravingSlotFirst);
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
        int count = EngravingLoadout.SlotCount;
        EnsureSlotButtonCount(count);

        for (int i = 0; i < _slots.Count; i++)
        {
            bool active = i < count;
            _slots[i].gameObject.SetActive(active);
            if (!active)
                continue;

            if (EngravingDisplayInfo.TryCreate(_stagedSlots[i], out EngravingDisplayInfo info))
            {
                _slots[i].BindCard(
                    info.Icon != null ? info.Icon : emptySlotSprite,
                    string.Format(
                        UiMessages.EngravingSlotFormat,
                        i,
                        FormatNameWithGrade(info)),
                    info.Description,
                    true,
                    GradeColor(info.HasGrade, info.Grade));
            }
            else
            {
                _slots[i].BindCard(
                    emptySlotSprite,
                    string.Format(
                        UiMessages.EngravingSlotFormat,
                        i,
                        UiMessages.EmptyEngravingSlot),
                    string.Empty,
                    true,
                    GradeColor(false, default));
            }
        }
    }

    private void RefreshPool()
    {
        int count = _stagedPool.Count;
        EnsurePoolButtonCount(count);
        for (int i = 0; i < _pool.Count; i++)
        {
            if (i < count)
            {
                _pool[i].gameObject.SetActive(true);

                if (EngravingDisplayInfo.TryCreate(
                        _stagedPool[i],
                        out EngravingDisplayInfo info))
                {
                    _pool[i].BindCard(
                        info.Icon != null ? info.Icon : emptySlotSprite,
                        FormatNameWithGrade(info),
                        info.Description,
                        true,
                        GradeColor(info.HasGrade, info.Grade));
                }
                else
                {
                    _pool[i].BindCard(
                        emptySlotSprite,
                        UiMessages.EmptyEngravingSlot,
                        string.Empty,
                        true,
                        GradeColor(false, default));
                }
            }
            else
            {
                _pool[i].gameObject.SetActive(false);
            }
        }
    }

    private void RefreshVisuals()
    {
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

    private void EnsurePoolButtonCount(int count)
    {
        if (poolButtonTemplate == null || poolContainer == null)
            return;

        while (_pool.Count < count)
        {
            int index = _pool.Count;
            EngravingSlotUI card = Instantiate(poolButtonTemplate, poolContainer);
            if (card.Button != null)
                card.Button.onClick.AddListener(() => HandlePoolClicked(index));

            card.gameObject.SetActive(false);
            _pool.Add(card);
        }
    }

    private static string FormatNameWithGrade(EngravingDisplayInfo info)
    {
        if (!info.HasGrade)
            return info.Name;

        return string.Format(
            UiMessages.EngravingGradeFormat,
            info.Name,
            UiMessages.GetEngravingGradeName(info.Grade));
    }

    private Color GradeColor(bool hasGrade, EngravingGrade grade)
    {
        return tierColorTable != null
            ? tierColorTable.GetColor((int)grade, hasGrade)
            : Color.white;
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
        return IsActiveDirty();
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
        {
            string confirmation = _owner != null
                ? UiMessages.SaveEngravingChangesConfirmation
                : UiMessages.SaveReusableEngravingChangesConfirmation;
            ShowDialog(confirmation, CommitAndClose);
        }
        else
            CloseWithoutSave();
    }

    private void CommitAndClose()
    {
        bool activeDirty = IsActiveDirty();
        if (activeDirty && !loadout.CanApplyArrangement(_form, _stagedSlots))
        {
            SetFeedback(UiMessages.EngravingApplyFailed);
            return;
        }

        if (activeDirty && !loadout.ApplyArrangement(_form, _stagedSlots))
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
