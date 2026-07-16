using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ShardPaymentModalUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text summaryText;
    [SerializeField] private ShardAllocationRowUI[] rows;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerSoulEnhancements soulEnhancements;
    [SerializeField] private SoulEnhancementTable enhancementTable;
    [SerializeField] private ItemDatabase itemDatabase;

    private readonly List<SoulCommonEnhancement.ShardAllocation> _allocations =
        new List<SoulCommonEnhancement.ShardAllocation>(4);

    private SoulStatType _stat;
    private int _cost;
    private int _lastDisplayedTotal = -1;
    private int _lastDisplayedCost = -1;
    private bool _listenersRegistered;
    private bool _initialized;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        EnsureInitialized();
        if (!IsOpen)
            Close();
    }

    public void Open(SoulStatType stat, int cost)
    {
        EnsureInitialized();
        if (!SoulStatTypeUtility.IsShared(stat) ||
            panel == null ||
            playerInventory == null ||
            soulEnhancements == null ||
            enhancementTable == null ||
            itemDatabase == null ||
            rows == null)
        {
            return;
        }

        _stat = stat;
        _cost = Mathf.Max(0, cost);
        _lastDisplayedTotal = -1;
        _lastDisplayedCost = -1;

        for (int i = 0; i < rows.Length; i++)
        {
            ShardAllocationRowUI row = rows[i];
            if (row == null)
                continue;

            bool ownedForm = playerInventory.OwnsSoulForm(row.Form);
            row.gameObject.SetActive(ownedForm);
            row.SetAmount(0);
            if (!ownedForm)
                continue;

            ItemData shardItem = null;
            int ownedCount = 0;
            if (SoulShardResolver.TryResolveShardItem(itemDatabase, playerInventory, row.Form, out shardItem))
                ownedCount = playerInventory.GetItemCount(shardItem);

            row.SetOwned(shardItem, ownedCount);
        }

        IsOpen = true;
        panel.SetActive(true);
        RefreshState();
    }

    public void Close()
    {
        IsOpen = false;
        if (panel != null)
            panel.SetActive(false);
    }

    public void RequestAmount(ShardAllocationRowUI target, int requestedAmount)
    {
        if (!IsOpen || target == null || rows == null)
            return;

        int otherTotal = 0;
        bool found = false;
        for (int i = 0; i < rows.Length; i++)
        {
            ShardAllocationRowUI row = rows[i];
            if (row == null || !row.gameObject.activeSelf)
                continue;

            if (row == target)
            {
                found = true;
                continue;
            }

            otherTotal += row.Amount;
        }

        if (!found)
            return;

        int remaining = Mathf.Max(0, _cost - otherTotal);
        int maximum = Mathf.Min(target.OwnedCount, remaining);
        target.SetAmount(Mathf.Clamp(requestedAmount, 0, maximum));
        RefreshState();
    }

    private void RegisterListeners()
    {
        if (_listenersRegistered)
            return;

        if (confirmButton != null)
            confirmButton.onClick.AddListener(Confirm);
        if (resetButton != null)
            resetButton.onClick.AddListener(ResetAllocations);
        if (cancelButton != null)
            cancelButton.onClick.AddListener(Close);

        _listenersRegistered = true;
    }

    private void InitializeRows()
    {
        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] != null)
                rows[i].Initialize(this);
        }
    }

    private void ResetAllocations()
    {
        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] != null)
                rows[i].SetAmount(0);
        }

        RefreshState();
    }

    private void Confirm()
    {
        if (!IsOpen ||
            playerInventory == null ||
            soulEnhancements == null ||
            enhancementTable == null ||
            itemDatabase == null ||
            GetAllocatedTotal() != _cost)
        {
            return;
        }

        _allocations.Clear();
        for (int i = 0; i < rows.Length; i++)
        {
            ShardAllocationRowUI row = rows[i];
            if (row == null || !row.gameObject.activeSelf || row.Amount <= 0)
                continue;

            _allocations.Add(new SoulCommonEnhancement.ShardAllocation(row.Form, row.Amount));
        }

        SoulCommonEnhancement.Result result = SoulCommonEnhancement.TryEnhance(
            enhancementTable,
            soulEnhancements,
            playerInventory,
            itemDatabase,
            _stat,
            _allocations);

        if (result == SoulCommonEnhancement.Result.Success)
        {
            Close();
            return;
        }

        Debug.LogWarning("[ShardPaymentModalUI] Common enhancement failed: " + result + ".", this);
    }

    private void RefreshState()
    {
        int total = GetAllocatedTotal();
        if (summaryText != null && (total != _lastDisplayedTotal || _cost != _lastDisplayedCost))
        {
            summaryText.SetText("배분 {0} / 필요 {1}", total, _cost);
            _lastDisplayedTotal = total;
            _lastDisplayedCost = _cost;
        }

        if (confirmButton != null)
            confirmButton.interactable = total == _cost;
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        RegisterListeners();
        InitializeRows();
        _initialized = true;
    }

    private int GetAllocatedTotal()
    {
        int total = 0;
        if (rows == null)
            return total;

        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] != null && rows[i].gameObject.activeSelf)
                total += rows[i].Amount;
        }

        return total;
    }
}
