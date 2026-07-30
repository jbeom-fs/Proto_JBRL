using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ArenaAilmentStripUI : MonoBehaviour
{
    private static readonly StatusEffectIconType[] s_IconTypes =
    {
        StatusEffectIconType.Poison,
        StatusEffectIconType.Bleed,
        StatusEffectIconType.Slow,
        StatusEffectIconType.Stun
    };
    private static readonly int s_StatusCount = s_IconTypes.Length;

    [SerializeField] private StatusEffectIconTable iconTable;
    [SerializeField, Min(0f)] private float slotScale = 1f;

    private readonly AilmentStatusTracker _tracker = new AilmentStatusTracker();
    private readonly List<AilmentCanvasSlotView> _slots = new List<AilmentCanvasSlotView>();
    private readonly Sprite[] _icons = new Sprite[s_StatusCount];
    private readonly bool[] _hasIcon = new bool[s_StatusCount];
    private readonly bool[] _warnedMissingIcon = new bool[s_StatusCount];
    private EnemyController _enemy;
    private AilmentCanvasSlotView _slotPrefab;
    private bool _iconsResolved;
    private bool _warnedMissingTable;
    private bool _warnedMissingSlotPrefab;

    public void Prewarm()
    {
        ResolveIconsIfNeeded();
        EnsureSlotCount(s_StatusCount);
        ClearSlots();
    }

    public void Bind(EnemyController enemy)
    {
        if (enemy == null)
            return;

        _enemy = enemy;
        _tracker.Reset();
        ResolveIconsIfNeeded();
        ClearSlots();
        Refresh();
    }

    public void Release()
    {
        _enemy = null;
        _tracker.Reset();
        ClearSlots();
    }

    private void Update()
    {
        if (_enemy == null)
            return;

        if (!_enemy.gameObject.activeInHierarchy || !_enemy.IsAlive)
        {
            Release();
            return;
        }

        Refresh();
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
            return;

        Release();
    }

    private void Refresh()
    {
        if (_enemy == null)
            return;

        int poisonStacks = HasIcon(StatusEffectIconType.Poison)
            ? _enemy.GetAilmentStacks(AilmentType.Poison)
            : 0;
        int bleedStacks = HasIcon(StatusEffectIconType.Bleed)
            ? _enemy.GetAilmentStacks(AilmentType.Bleed)
            : 0;
        bool isSlowed = HasIcon(StatusEffectIconType.Slow) && _enemy.IsSlowed;
        bool isStunned = HasIcon(StatusEffectIconType.Stun) && _enemy.IsStunned;

        _tracker.Update(poisonStacks, bleedStacks, isSlowed, isStunned);
        if (_tracker.OrderChanged)
            RefreshSlotArrangement();

        RefreshStackCounts();
    }

    private void RefreshSlotArrangement()
    {
        IReadOnlyList<AilmentStatus> activeStatuses = _tracker.ActiveStatuses;
        EnsureSlotCount(activeStatuses.Count);

        for (int i = 0; i < _slots.Count; i++)
        {
            AilmentCanvasSlotView slot = _slots[i];
            if (slot == null)
                continue;

            if (i >= activeStatuses.Count)
            {
                ClearSlot(slot);
                continue;
            }

            AilmentStatus status = activeStatuses[i];
            int iconIndex = GetStatusIndex(status.Type);
            if (iconIndex < 0 || !_hasIcon[iconIndex])
            {
                ClearSlot(slot);
                continue;
            }

            slot.SetIcon(_icons[iconIndex]);
            slot.SetVisible(true);
        }
    }

    private void RefreshStackCounts()
    {
        IReadOnlyList<AilmentStatus> activeStatuses = _tracker.ActiveStatuses;
        int visibleCount = Mathf.Min(activeStatuses.Count, _slots.Count);

        for (int i = 0; i < visibleCount; i++)
        {
            AilmentCanvasSlotView slot = _slots[i];
            if (slot == null)
                continue;

            AilmentStatus status = activeStatuses[i];
            slot.SetStack(status.Type, status.StackCount);
        }
    }

    private void ResolveIconsIfNeeded()
    {
        if (_iconsResolved)
            return;

        _iconsResolved = true;
        StatusEffectIconTable table = iconTable;
        if (table == null)
        {
            WarnMissingTable();
            return;
        }

        _slotPrefab = table.CanvasSlotPrefab;
        if (_slotPrefab == null)
            WarnMissingSlotPrefab();

        for (int i = 0; i < s_IconTypes.Length; i++)
        {
            StatusEffectIconType type = s_IconTypes[i];
            int iconIndex = GetStatusIndex(type);
            if (iconIndex < 0)
                continue;

            if (table.TryGetIcon(type, out Sprite icon))
            {
                _icons[iconIndex] = icon;
                _hasIcon[iconIndex] = true;
            }
            else
            {
                WarnMissingIcon(type);
            }
        }
    }

    private bool HasIcon(StatusEffectIconType type)
    {
        int index = GetStatusIndex(type);
        return index >= 0 && _hasIcon[index];
    }

    private static int GetStatusIndex(StatusEffectIconType type)
    {
        return Array.IndexOf(s_IconTypes, type);
    }

    private void ClearSlots()
    {
        for (int i = 0; i < _slots.Count; i++)
            ClearSlot(_slots[i]);
    }

    private void EnsureSlotCount(int requiredCount)
    {
        if (requiredCount <= _slots.Count)
        {
            ApplySlotScale();
            return;
        }

        if (_slotPrefab == null)
        {
            WarnMissingSlotPrefab();
            return;
        }

        while (_slots.Count < requiredCount)
        {
            AilmentCanvasSlotView slot = Instantiate(_slotPrefab, transform, false);
            slot.transform.SetSiblingIndex(_slots.Count);
            _slots.Add(slot);
        }

        ApplySlotScale();
    }

    private void ApplySlotScale()
    {
        if (_slotPrefab == null)
            return;

        Vector3 authoredScale = _slotPrefab.transform.localScale;
        Vector3 appliedScale = authoredScale * Mathf.Max(0f, slotScale);
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i] != null)
                _slots[i].transform.localScale = appliedScale;
        }
    }

    private static void ClearSlot(AilmentCanvasSlotView slot)
    {
        if (slot == null)
            return;

        slot.SetIcon(null);
        slot.SetStack(StatusEffectIconType.Poison, 0);
        slot.SetVisible(false);
    }

    private void WarnMissingTable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedMissingTable)
            return;

        _warnedMissingTable = true;
        Debug.LogWarning(
            "[ArenaAilmentStripUI] StatusEffectIconTable is missing. Inspector assignment is required.",
            this);
#endif
    }

    private void WarnMissingIcon(StatusEffectIconType type)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int index = GetStatusIndex(type);
        if (index < 0 || _warnedMissingIcon[index])
            return;

        _warnedMissingIcon[index] = true;
        Debug.LogWarning("[ArenaAilmentStripUI] " + type + " icon missing; status hidden.", this);
#endif
    }

    private void WarnMissingSlotPrefab()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedMissingSlotPrefab)
            return;

        _warnedMissingSlotPrefab = true;
        Debug.LogWarning(
            "[ArenaAilmentStripUI] StatusEffectIconTable.canvasSlotPrefab is missing; strip hidden.",
            this);
#endif
    }
}
