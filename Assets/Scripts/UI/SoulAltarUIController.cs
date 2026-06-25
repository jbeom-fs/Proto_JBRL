using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class SoulAltarUIController : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerSoulEnhancements soulEnhancements;
    [SerializeField] private SoulEnhancementTable enhancementTable;
    [SerializeField] private ItemDatabase itemDatabase;
    [SerializeField] private Transform formButtonContainer;
    [SerializeField] private Button formButtonTemplate;
    [SerializeField] private TMP_Text selectedFormText;
    [SerializeField] private Transform statRowContainer;
    [SerializeField] private SoulAltarStatRowUI statRowTemplate;
    [SerializeField] private TMP_Text emptyText;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private Button closeButton;
    [SerializeField] private Color formSelectedColor = Color.white;
    [SerializeField] private Color formNormalColor = new Color(0.65f, 0.65f, 0.65f, 1f);

    private static readonly PlayerFormId[] s_FormScanOrder =
    {
        PlayerFormId.Sword,
        PlayerFormId.Dagger,
        PlayerFormId.Freischutz,
        PlayerFormId.Parry,
        PlayerFormId.Normal
    };

    private readonly List<PlayerFormId> _ownedForms = new List<PlayerFormId>(8);
    private readonly List<SoulStatGrowth> _growthBuffer = new List<SoulStatGrowth>(8);
    private readonly List<Button> _formButtons = new List<Button>(8);
    private readonly List<TMP_Text> _formButtonTexts = new List<TMP_Text>(8);
    private readonly List<SoulAltarStatRowUI> _statRows = new List<SoulAltarStatRowUI>(8);

    private PlayerFormId _selectedForm;
    private bool _subscribed;
    private bool _warnedMissingReferences;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (formButtonTemplate != null)
            formButtonTemplate.gameObject.SetActive(false);

        if (statRowTemplate != null)
            statRowTemplate.gameObject.SetActive(false);

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
        if (!HasRequiredReferences())
            return;

        Subscribe();
        IsOpen = true;
        panel.SetActive(true);

        RebuildOwnedForms();
        if (_ownedForms.Count > 0)
            SelectForm(_ownedForms[0]);
        else
            RefreshSelectedForm();
    }

    public void Close()
    {
        Unsubscribe();
        IsOpen = false;

        if (panel != null)
            panel.SetActive(false);
    }

    public void HandleEnhanceClicked(PlayerFormId form, SoulStatType stat)
    {
        TryEnhance(form, stat);
    }

    private void TryEnhance(PlayerFormId form, SoulStatType stat)
    {
        if (!HasRequiredReferences())
            return;

        if (!enhancementTable.TryGetGrowth(form, stat, out SoulStatGrowth growth))
            return;

        int currentLevel = soulEnhancements.GetLevel(form, stat);
        if (currentLevel >= Mathf.Max(0, growth.maxLevel))
        {
            SetFeedback("Max level");
            return;
        }

        int cost = SoulEnhancementCost.GetMaterialCost(growth, currentLevel);
        if (cost > 0)
        {
            if (!TryResolveShardItem(form, out ItemData shardItem))
            {
                SetFeedback("Shard item missing");
                return;
            }

            if (!playerInventory.HasItem(shardItem, cost))
            {
                SetFeedback("Not enough shards");
                return;
            }

            if (!playerInventory.RemoveItem(shardItem, cost))
                return;
        }

        soulEnhancements.AddLevel(form, stat, 1);
        SetFeedback("Enhanced");
        RefreshAll();
    }

    private void RefreshAll()
    {
        if (!IsOpen)
            return;

        RebuildOwnedForms();

        if (_ownedForms.Count == 0)
        {
            RefreshSelectedForm();
            return;
        }

        if (!ContainsOwnedForm(_selectedForm))
            _selectedForm = _ownedForms[0];

        RefreshSelectedForm();
    }

    private void RebuildOwnedForms()
    {
        _ownedForms.Clear();

        for (int i = 0; i < s_FormScanOrder.Length; i++)
        {
            PlayerFormId form = s_FormScanOrder[i];
            if (!playerInventory.OwnsSoulForm(form))
                continue;

            _growthBuffer.Clear();
            enhancementTable.GetGrowths(form, _growthBuffer);
            if (_growthBuffer.Count > 0)
                _ownedForms.Add(form);
        }

        EnsureFormButtonCount(_ownedForms.Count);
        for (int i = 0; i < _formButtons.Count; i++)
        {
            if (i < _ownedForms.Count)
            {
                PlayerFormId form = _ownedForms[i];
                _formButtons[i].gameObject.SetActive(true);

                if (_formButtonTexts[i] != null)
                    _formButtonTexts[i].text = form.ToString();
            }
            else
            {
                _formButtons[i].gameObject.SetActive(false);
            }
        }

        RefreshFormButtonVisuals();
    }

    private void SelectForm(PlayerFormId form)
    {
        _selectedForm = form;
        RefreshSelectedForm();
    }

    private void RefreshSelectedForm()
    {
        if (_ownedForms.Count == 0)
        {
            HideStatRows();
            if (selectedFormText != null)
                selectedFormText.text = string.Empty;
            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(true);
                emptyText.text = "No owned soul forms";
            }
            RefreshFormButtonVisuals();
            return;
        }

        if (selectedFormText != null)
            selectedFormText.text = _selectedForm.ToString();

        _growthBuffer.Clear();
        enhancementTable.GetGrowths(_selectedForm, _growthBuffer);
        EnsureStatRowCount(_growthBuffer.Count);

        TryResolveShardItem(_selectedForm, out ItemData shardItem);
        int shardCount = shardItem != null ? playerInventory.GetItemCount(shardItem) : 0;

        for (int i = 0; i < _statRows.Count; i++)
        {
            if (i >= _growthBuffer.Count)
            {
                _statRows[i].Hide();
                continue;
            }

            SoulStatGrowth growth = _growthBuffer[i];
            int currentLevel = soulEnhancements.GetLevel(_selectedForm, growth.stat);
            int cost = SoulEnhancementCost.GetMaterialCost(growth, currentLevel);
            _statRows[i].Bind(_selectedForm, growth, currentLevel, cost, shardItem, shardCount);
        }

        if (emptyText != null)
            emptyText.gameObject.SetActive(_growthBuffer.Count == 0);

        RefreshFormButtonVisuals();
    }

    private void EnsureFormButtonCount(int count)
    {
        if (formButtonTemplate == null || formButtonContainer == null)
            return;

        while (_formButtons.Count < count)
        {
            int index = _formButtons.Count;
            Button button = Instantiate(formButtonTemplate, formButtonContainer);
            button.onClick.AddListener(() => HandleFormButtonClicked(index));
            button.gameObject.SetActive(false);
            _formButtons.Add(button);
            _formButtonTexts.Add(button.GetComponentInChildren<TMP_Text>(true));
        }
    }

    private void EnsureStatRowCount(int count)
    {
        if (statRowTemplate == null || statRowContainer == null)
            return;

        while (_statRows.Count < count)
        {
            SoulAltarStatRowUI row = Instantiate(statRowTemplate, statRowContainer);
            row.Initialize(this);
            row.Hide();
            _statRows.Add(row);
        }
    }

    private void HandleFormButtonClicked(int index)
    {
        if (index < 0 || index >= _ownedForms.Count)
            return;

        SelectForm(_ownedForms[index]);
    }

    private void RefreshFormButtonVisuals()
    {
        for (int i = 0; i < _formButtons.Count; i++)
        {
            Image image = _formButtons[i].GetComponent<Image>();
            if (image != null)
                image.color = i < _ownedForms.Count && _ownedForms[i] == _selectedForm ? formSelectedColor : formNormalColor;
        }
    }

    private void HideStatRows()
    {
        for (int i = 0; i < _statRows.Count; i++)
            _statRows[i].Hide();
    }

    private bool TryResolveShardItem(PlayerFormId form, out ItemData shardItem)
    {
        shardItem = null;

        if (!TryResolveSoulItem(form, out ItemData soulItem))
            return false;

        if (string.IsNullOrWhiteSpace(soulItem.SalvageItemCode))
            return false;

        if (playerInventory != null && playerInventory.TryGetDatabaseItem(soulItem.SalvageItemCode, out shardItem))
            return true;

        return itemDatabase != null && itemDatabase.TryGetItem(soulItem.SalvageItemCode, out shardItem);
    }

    private bool TryResolveSoulItem(PlayerFormId form, out ItemData soulItem)
    {
        soulItem = null;

        if (itemDatabase != null && itemDatabase.TryGetSoulByForm(form, out soulItem))
            return true;

        return playerInventory != null && playerInventory.TryGetSoulByForm(form, out soulItem);
    }

    private bool ContainsOwnedForm(PlayerFormId form)
    {
        for (int i = 0; i < _ownedForms.Count; i++)
        {
            if (_ownedForms[i] == form)
                return true;
        }

        return false;
    }

    private bool HasRequiredReferences()
    {
        bool valid = panel != null &&
                     playerInventory != null &&
                     soulEnhancements != null &&
                     enhancementTable != null &&
                     formButtonContainer != null &&
                     formButtonTemplate != null &&
                     statRowContainer != null &&
                     statRowTemplate != null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!valid && !_warnedMissingReferences)
        {
            Debug.LogWarning("[SoulAltarUIController] Required references are not assigned.", this);
            _warnedMissingReferences = true;
        }
#endif

        return valid;
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;

        playerInventory.OnInventoryChanged += RefreshAll;
        soulEnhancements.OnChanged += RefreshAll;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;

        if (playerInventory != null)
            playerInventory.OnInventoryChanged -= RefreshAll;

        if (soulEnhancements != null)
            soulEnhancements.OnChanged -= RefreshAll;

        _subscribed = false;
    }

    private void SetFeedback(string message)
    {
        if (feedbackText != null)
            feedbackText.text = message;
    }
}
