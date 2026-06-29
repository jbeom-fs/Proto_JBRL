using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EngravingLoadout : MonoBehaviour
{
    public const int SlotCount = 4;

    [Header("DEBUG ONLY (Slice B verification catalog, remove in later slice)")]
    [Tooltip("Temporary engraving list added to pool by /engraving give <form> <index>.")]
    [SerializeField] private SkillData[] debugEngravingPool;

    private sealed class FormState
    {
        public readonly SkillData[] Slots = new SkillData[SlotCount];
        public readonly List<SkillData> Pool = new();
        public bool Seeded;
    }

    private readonly Dictionary<PlayerFormId, FormState> _states = new();

    public event Action OnChanged;

    public int DebugPoolCount => debugEngravingPool != null ? debugEngravingPool.Length : 0;

    private FormState GetOrCreate(PlayerFormId form)
    {
        if (!_states.TryGetValue(form, out FormState state))
        {
            state = new FormState();
            _states[form] = state;
        }

        return state;
    }

    public void EnsureSeeded(PlayerFormId form, SkillData[] baseSkills)
    {
        FormState state = GetOrCreate(form);
        if (state.Seeded)
            return;

        for (int i = 0; i < SlotCount; i++)
            state.Slots[i] = baseSkills != null && i < baseSkills.Length ? baseSkills[i] : null;

        state.Seeded = true;
    }

    public SkillData GetSlot(PlayerFormId form, int slot)
    {
        if ((uint)slot >= (uint)SlotCount)
            return null;

        return _states.TryGetValue(form, out FormState state) ? state.Slots[slot] : null;
    }

    public bool Equip(PlayerFormId form, int slot, int poolIndex)
    {
        if ((uint)slot >= (uint)SlotCount)
            return false;

        if (!_states.TryGetValue(form, out FormState state))
            return false;

        if ((uint)poolIndex >= (uint)state.Pool.Count)
            return false;

        SkillData incoming = state.Pool[poolIndex];
        state.Pool.RemoveAt(poolIndex);
        SkillData displaced = state.Slots[slot];
        state.Slots[slot] = incoming;
        if (displaced != null)
            state.Pool.Add(displaced);
        OnChanged?.Invoke();
        return true;
    }

    public bool Unequip(PlayerFormId form, int slot)
    {
        if ((uint)slot >= (uint)SlotCount)
            return false;

        if (!_states.TryGetValue(form, out FormState state))
            return false;

        SkillData token = state.Slots[slot];
        if (token == null)
            return false;

        state.Slots[slot] = null;
        state.Pool.Add(token);
        OnChanged?.Invoke();
        return true;
    }

    public void AddToPool(PlayerFormId form, SkillData skill)
    {
        if (skill == null)
            return;

        GetOrCreate(form).Pool.Add(skill);
    }

    public int PoolCount(PlayerFormId form)
    {
        return _states.TryGetValue(form, out FormState state) ? state.Pool.Count : 0;
    }

    public SkillData GetPoolAt(PlayerFormId form, int index)
    {
        return _states.TryGetValue(form, out FormState state) && (uint)index < (uint)state.Pool.Count
            ? state.Pool[index]
            : null;
    }

    public void ClearAll()
    {
        if (_states.Count == 0)
            return;

        _states.Clear();
        OnChanged?.Invoke();
    }

    public SkillData GetDebugEngraving(int index)
    {
        return debugEngravingPool != null && (uint)index < (uint)debugEngravingPool.Length
            ? debugEngravingPool[index]
            : null;
    }
}
