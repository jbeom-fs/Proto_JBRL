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
    [SerializeField] private Color selectedColor = Color.white;
    [SerializeField] private Color normalColor = new Color(0.65f, 0.65f, 0.65f, 1f);

    private readonly List<Button> _slotButtons = new List<Button>(EngravingLoadout.SlotCount);
    private readonly List<TMP_Text> _slotTexts = new List<TMP_Text>(EngravingLoadout.SlotCount);
    private readonly List<Button> _poolButtons = new List<Button>(8);
    private readonly List<TMP_Text> _poolTexts = new List<TMP_Text>(8);

    private PlayerFormId _form;
    private int _selectedSlot = -1;
    private bool _subscribed;
    private bool _warnedMissingReferences;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (unequipButton != null)
            unequipButton.onClick.AddListener(HandleUnequipClicked);

        if (slotButtonTemplate != null)
            slotButtonTemplate.gameObject.SetActive(false);

        if (poolButtonTemplate != null)
            poolButtonTemplate.gameObject.SetActive(false);

        if (panel != null)
            panel.SetActive(false);
    }

    private void OnDisable()
    {
        Close();
    }

    private void Update()
    {
        if (!IsOpen)
            return;

        if (DeveloperConsoleUI.IsOpen)
        {
            Close();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            Close();
    }

    public void Open()
    {
        if (IsOpen)
            return;

        if (!HasRequiredReferences())
            return;

        Subscribe();
        IsOpen = true;
        _form = combat.CurrentFormId;
        _selectedSlot = -1;
        SetFeedback(string.Empty);
        panel.SetActive(true);
        GamePauseController.Active?.Pause(GamePauseSource.EngravingLoadout);
        RefreshAll();
    }

    public void Close()
    {
        Unsubscribe();
        IsOpen = false;
        _selectedSlot = -1;

        if (panel != null)
            panel.SetActive(false);

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

        if (loadout.Equip(_form, _selectedSlot, poolIndex))
            SetFeedback("Equipped");
        else
            SetFeedback("Cannot equip");
    }

    private void HandleUnequipClicked()
    {
        if (_selectedSlot < 0)
        {
            SetFeedback("Select a slot first");
            return;
        }

        if (loadout.Unequip(_form, _selectedSlot))
            SetFeedback("Unequipped");
        else
            SetFeedback("Slot empty");
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
                _slotTexts[i].text = "[" + i + "] " + DisplayName(loadout.GetSlot(_form, i));
        }
    }

    private void RefreshPool()
    {
        int count = loadout.PoolCount(_form);
        EnsurePoolButtonCount(count);
        for (int i = 0; i < _poolButtons.Count; i++)
        {
            if (i < count)
            {
                _poolButtons[i].gameObject.SetActive(true);
                if (_poolTexts[i] != null)
                    _poolTexts[i].text = DisplayName(loadout.GetPoolAt(_form, i));
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
                     poolButtonTemplate != null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!valid && !_warnedMissingReferences)
        {
            Debug.LogWarning("[EngravingLoadoutUIController] Required references are not assigned.", this);
            _warnedMissingReferences = true;
        }
#endif

        return valid;
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;

        loadout.OnChanged += RefreshAll;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;

        if (loadout != null)
            loadout.OnChanged -= RefreshAll;

        _subscribed = false;
    }

    private void SetFeedback(string message)
    {
        if (feedbackText != null)
            feedbackText.text = message;
    }
}
