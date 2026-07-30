using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class SoulAltarUIController : MonoBehaviour
{
    [Serializable]
    public sealed class AltarSection
    {
        public PlayerFormId form;
        public GameObject headerRoot;
        public TMP_Text headerText;
        public SoulAltarStatRowUI[] rows;
        public PassiveUnlockRowUI[] passiveRows;
    }

    [SerializeField] private GameObject panel;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerSoulEnhancements soulEnhancements;
    [SerializeField] private PlayerPassiveUnlocks passiveUnlocks;
    [SerializeField, Min(0)] private int passiveUnlockBaseCost = 10;
    [SerializeField] private SoulEnhancementTable enhancementTable;
    [SerializeField] private ItemDatabase itemDatabase;
    [SerializeField] private AltarSection[] sections;
    [SerializeField] private Button closeButton;
    [SerializeField] private ShardPaymentModalUI paymentModal;

    private static readonly PlayerFormId[] s_FormScanOrder =
    {
        PlayerFormId.Sword,
        PlayerFormId.Dagger,
        PlayerFormId.Freischutz,
        PlayerFormId.Parry
    };

    private readonly List<SoulStatGrowth> _growthBuffer = new List<SoulStatGrowth>(8);
    private readonly List<PassiveEngravingData> _passiveCatalogBuffer =
        new List<PassiveEngravingData>(8);
    private string[] _sectionHeaderTexts;
    private bool _subscribed;
    private bool _warnedMissingReferences;
    private bool _consoleProtectedAltar;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        InitializeRows();

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
            if (paymentModal != null && paymentModal.IsOpen)
            {
                paymentModal.Close();
                _consoleProtectedAltar = true;
            }
            else if (!_consoleProtectedAltar)
            {
                Close();
            }

            return;
        }

        _consoleProtectedAltar = false;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (paymentModal != null && paymentModal.IsOpen)
                paymentModal.Close();
            else
                Close();
        }
    }

    public void Open()
    {
        if (!HasRequiredReferences())
            return;

        Subscribe();
        IsOpen = true;
        panel.SetActive(true);
        RefreshAll();
    }

    public void Close()
    {
        Unsubscribe();
        IsOpen = false;
        if (paymentModal != null)
            paymentModal.Close();

        if (panel != null)
            panel.SetActive(false);
    }

    public void HandleEnhanceClicked(PlayerFormId form, SoulStatType stat)
    {
        if (SoulStatTypeUtility.IsShared(stat))
        {
            OpenSharedPayment(stat);
            return;
        }

        TryEnhance(form, stat);
    }

    public void HandlePassiveUnlockClicked(
        PlayerFormId form,
        PassiveEngravingData passive)
    {
        if (!HasRequiredReferences() ||
            passiveUnlocks == null ||
            passive == null ||
            string.IsNullOrWhiteSpace(passive.unlockId))
        {
            return;
        }

        _passiveCatalogBuffer.Clear();
        passiveUnlocks.GetCatalog(form, _passiveCatalogBuffer);

        bool targetFound = false;
        int unlockedCandidateCount = 0;
        for (int i = 1; i < _passiveCatalogBuffer.Count; i++)
        {
            PassiveEngravingData candidate = _passiveCatalogBuffer[i];
            if (candidate == passive)
                targetFound = true;
            if (passiveUnlocks.IsUnlocked(candidate))
                unlockedCandidateCount++;
        }

        _passiveCatalogBuffer.Clear();
        if (!targetFound || passiveUnlocks.IsUnlocked(passive))
            return;

        int cost = PassiveUnlockCost.GetCost(
            passiveUnlockBaseCost,
            unlockedCandidateCount);
        ItemData shardItem = null;
        if (cost > 0)
        {
            if (!SoulShardResolver.TryResolveShardItem(
                    itemDatabase,
                    playerInventory,
                    form,
                    out shardItem) ||
                !playerInventory.HasItem(shardItem, cost) ||
                !playerInventory.RemoveItem(shardItem, cost))
            {
                return;
            }
        }

        if (passiveUnlocks.Unlock(passive))
            return;

        if (cost > 0 && shardItem != null)
            playerInventory.AddItem(shardItem, cost);
    }

    private void OpenSharedPayment(SoulStatType stat)
    {
        if (!HasRequiredReferences() || paymentModal == null)
            return;

        if (!enhancementTable.TryGetGrowth(PlayerFormId.Normal, stat, out SoulStatGrowth growth))
            return;

        int currentLevel = soulEnhancements.GetLevel(PlayerFormId.Normal, stat);
        if (currentLevel >= Mathf.Max(0, growth.maxLevel))
            return;

        int cost = SoulEnhancementCost.GetMaterialCost(growth, currentLevel);
        paymentModal.Open(stat, cost);
    }

    private void TryEnhance(PlayerFormId form, SoulStatType stat)
    {
        if (SoulStatTypeUtility.IsShared(stat))
            return;

        if (!HasRequiredReferences())
            return;

        if (!enhancementTable.TryGetGrowth(form, stat, out SoulStatGrowth growth))
            return;

        int currentLevel = soulEnhancements.GetLevel(form, stat);
        if (currentLevel >= Mathf.Max(0, growth.maxLevel))
            return;

        int cost = SoulEnhancementCost.GetMaterialCost(growth, currentLevel);
        if (cost > 0)
        {
            if (!SoulShardResolver.TryResolveShardItem(itemDatabase, playerInventory, form, out ItemData shardItem))
                return;

            if (!playerInventory.HasItem(shardItem, cost))
                return;

            if (!playerInventory.RemoveItem(shardItem, cost))
                return;
        }

        soulEnhancements.AddLevel(form, stat, 1);
    }

    private void RefreshAll()
    {
        if (!IsOpen)
            return;

        RefreshSection(PlayerFormId.Normal, true);
        for (int i = 0; i < s_FormScanOrder.Length; i++)
            RefreshSection(s_FormScanOrder[i], false);
    }

    private void RefreshSection(PlayerFormId form, bool shared)
    {
        int sectionIndex = FindSectionIndex(form);
        if (sectionIndex < 0)
            return;

        AltarSection section = sections[sectionIndex];
        if (section.headerRoot != null)
            section.headerRoot.SetActive(true);

        bool unlocked = shared || playerInventory.OwnsSoulForm(section.form);
        if (section.headerText != null)
            section.headerText.text = unlocked ? _sectionHeaderTexts[sectionIndex] : UiMessages.LockedSectionHeader;

        if (!unlocked)
        {
            HideRows(section.rows);
            HideRows(section.passiveRows);
            return;
        }

        _growthBuffer.Clear();
        enhancementTable.GetGrowths(section.form, _growthBuffer);

        int rowCount = section.rows?.Length ?? 0;
        if (_growthBuffer.Count > rowCount)
        {
            Debug.LogWarning(
                "[SoulAltarUIController] " + section.form + " 섹션 행 부족: 테이블 " +
                _growthBuffer.Count + "개, 씬 " + rowCount + "개. 씬 행 추가와 sections 재결선 필요.",
                this);
        }

        ItemData shardItem = null;
        int shardCount = 0;
        if (!shared && SoulShardResolver.TryResolveShardItem(itemDatabase, playerInventory, section.form, out shardItem))
            shardCount = playerInventory.GetItemCount(shardItem);

        RefreshPassiveRows(section, shared, shardItem, shardCount);

        for (int i = 0; i < rowCount; i++)
        {
            SoulAltarStatRowUI row = section.rows[i];
            if (row == null)
                continue;

            if (i >= _growthBuffer.Count)
            {
                row.Hide();
                continue;
            }

            SoulStatGrowth growth = _growthBuffer[i];
            int currentLevel = soulEnhancements.GetLevel(section.form, growth.stat);
            int cost = SoulEnhancementCost.GetMaterialCost(growth, currentLevel);
            row.Bind(section.form, growth, currentLevel, cost, shardItem, shardCount, shared);
        }
    }

    private void RefreshPassiveRows(
        AltarSection section,
        bool shared,
        ItemData shardItem,
        int shardCount)
    {
        if (shared || passiveUnlocks == null)
        {
            HideRows(section.passiveRows);
            return;
        }

        _passiveCatalogBuffer.Clear();
        passiveUnlocks.GetCatalog(section.form, _passiveCatalogBuffer);

        int rowCount = section.passiveRows?.Length ?? 0;
        int candidateCount = Mathf.Max(0, _passiveCatalogBuffer.Count - 1);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (candidateCount > rowCount)
        {
            Debug.LogWarning(
                "[SoulAltarUIController] " + section.form +
                " passive unlock row capacity exceeded: candidates " +
                candidateCount + ", rows " + rowCount + ".",
                this);
        }
#endif

        int unlockedCandidateCount = 0;
        for (int i = 1; i < _passiveCatalogBuffer.Count; i++)
        {
            if (passiveUnlocks.IsUnlocked(_passiveCatalogBuffer[i]))
                unlockedCandidateCount++;
        }

        int cost = PassiveUnlockCost.GetCost(
            passiveUnlockBaseCost,
            unlockedCandidateCount);
        for (int i = 0; i < rowCount; i++)
        {
            PassiveUnlockRowUI row = section.passiveRows[i];
            if (row == null)
                continue;

            int catalogIndex = i + 1;
            if (catalogIndex >= _passiveCatalogBuffer.Count)
            {
                row.Hide();
                continue;
            }

            PassiveEngravingData passive = _passiveCatalogBuffer[catalogIndex];
            row.Bind(
                section.form,
                passive,
                cost,
                shardItem,
                shardCount,
                passiveUnlocks.IsUnlocked(passive));
        }

        _passiveCatalogBuffer.Clear();
    }

    private int FindSectionIndex(PlayerFormId form)
    {
        if (sections == null)
            return -1;

        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i] != null && sections[i].form == form)
                return i;
        }

        return -1;
    }

    private void InitializeRows()
    {
        if (sections == null)
            return;

        _sectionHeaderTexts = new string[sections.Length];
        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i]?.headerText != null)
                _sectionHeaderTexts[i] = sections[i].headerText.text;

            if (sections[i]?.passiveRows != null)
            {
                for (int j = 0; j < sections[i].passiveRows.Length; j++)
                {
                    PassiveUnlockRowUI row = sections[i].passiveRows[j];
                    if (row == null)
                        continue;

                    row.Initialize(this);
                    row.Hide();
                }
            }

            if (sections[i]?.rows == null)
                continue;

            for (int j = 0; j < sections[i].rows.Length; j++)
            {
                SoulAltarStatRowUI row = sections[i].rows[j];
                if (row == null)
                    continue;

                row.Initialize(this);
                row.Hide();
            }
        }
    }

    private static void HideRows(SoulAltarStatRowUI[] rows)
    {
        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] != null)
                rows[i].Hide();
        }
    }

    private static void HideRows(PassiveUnlockRowUI[] rows)
    {
        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] != null)
                rows[i].Hide();
        }
    }

    private bool HasRequiredReferences()
    {
        bool valid = panel != null &&
                     playerInventory != null &&
                     soulEnhancements != null &&
                     enhancementTable != null &&
                     paymentModal != null &&
                     sections != null &&
                     sections.Length == 5;

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
        if (passiveUnlocks != null)
            passiveUnlocks.OnChanged += RefreshAll;
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

        if (passiveUnlocks != null)
            passiveUnlocks.OnChanged -= RefreshAll;

        _subscribed = false;
    }

}
