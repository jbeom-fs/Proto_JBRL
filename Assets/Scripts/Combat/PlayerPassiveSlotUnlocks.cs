using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerPassiveSlotUnlocks : MonoBehaviour
{
    [SerializeField] private List<PassiveSlotUnlockEntry> unlocks = new List<PassiveSlotUnlockEntry>();

    private readonly Dictionary<PlayerFormId, PassiveSlotUnlockEntry> _byForm =
        new Dictionary<PlayerFormId, PassiveSlotUnlockEntry>();
    private bool _cacheBuilt;

    public static PlayerPassiveSlotUnlocks Active { get; private set; }

    public event Action OnChanged;

    public int GetUnlockCount(PlayerFormId form)
    {
        int count = RawCount(form);
        return Mathf.Clamp(
            count <= 0 ? 1 : count,
            1,
            EngravingLoadout.PassiveSlotCount);
    }

    public void GetUnlocks(List<PassiveSlotUnlockSnapshot> output)
    {
        if (output == null)
            return;

        EnsureCache();

        if (unlocks == null)
            return;

        for (int i = 0; i < unlocks.Count; i++)
        {
            PassiveSlotUnlockEntry entry = unlocks[i];
            if (entry == null)
                continue;

            int count = Mathf.Clamp(entry.count, 1, EngravingLoadout.PassiveSlotCount);
            if (count <= 1)
                continue;

            output.Add(new PassiveSlotUnlockSnapshot(entry.form, count));
        }
    }

    public void AddUnlock(PlayerFormId form, int delta)
    {
        if (delta == 0)
            return;

        SetUnlock(form, GetUnlockCount(form) + delta);
    }

    public void SetUnlock(PlayerFormId form, int count)
    {
        EnsureCache();

        int clampedCount = Mathf.Clamp(count, 1, EngravingLoadout.PassiveSlotCount);
        if (_byForm.TryGetValue(form, out PassiveSlotUnlockEntry entry))
        {
            if (clampedCount == 1)
            {
                unlocks.Remove(entry);
                _byForm.Remove(form);
            }
            else
            {
                int currentCount = Mathf.Clamp(entry.count, 1, EngravingLoadout.PassiveSlotCount);
                if (currentCount == clampedCount)
                    return;

                entry.count = clampedCount;
            }

            OnChanged?.Invoke();
            return;
        }

        if (clampedCount == 1)
            return;

        if (unlocks == null)
            unlocks = new List<PassiveSlotUnlockEntry>();

        entry = new PassiveSlotUnlockEntry
        {
            form = form,
            count = clampedCount
        };
        unlocks.Add(entry);
        _byForm.Add(form, entry);
        OnChanged?.Invoke();
    }

    public void Clear()
    {
        EnsureCache();
        if ((unlocks == null || unlocks.Count == 0) && _byForm.Count == 0)
            return;

        if (unlocks != null)
            unlocks.Clear();

        _byForm.Clear();
        _cacheBuilt = true;
        OnChanged?.Invoke();
    }

    private void OnEnable()
    {
        Active = this;
        RebuildCache();
    }

    private void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    private void OnValidate()
    {
        if (unlocks == null)
            return;

        for (int i = 0; i < unlocks.Count; i++)
        {
            PassiveSlotUnlockEntry entry = unlocks[i];
            if (entry != null)
                entry.count = Mathf.Clamp(entry.count, 1, EngravingLoadout.PassiveSlotCount);
        }

        RebuildCache();
    }

    private int RawCount(PlayerFormId form)
    {
        EnsureCache();
        return _byForm.TryGetValue(form, out PassiveSlotUnlockEntry entry)
            ? entry.count
            : 0;
    }

    private void EnsureCache()
    {
        if (!_cacheBuilt)
            RebuildCache();
    }

    private void RebuildCache()
    {
        _byForm.Clear();
        _cacheBuilt = true;

        if (unlocks == null)
            return;

        for (int i = 0; i < unlocks.Count; i++)
        {
            PassiveSlotUnlockEntry entry = unlocks[i];
            if (entry == null)
                continue;

            entry.count = Mathf.Clamp(entry.count, 1, EngravingLoadout.PassiveSlotCount);
            if (_byForm.ContainsKey(entry.form))
                continue;

            _byForm.Add(entry.form, entry);
        }
    }
}

[Serializable]
public sealed class PassiveSlotUnlockEntry
{
    public PlayerFormId form;
    [Min(1)] public int count;
}

public readonly struct PassiveSlotUnlockSnapshot
{
    public PassiveSlotUnlockSnapshot(PlayerFormId form, int count)
    {
        Form = form;
        Count = count;
    }

    public PlayerFormId Form { get; }
    public int Count { get; }
}
