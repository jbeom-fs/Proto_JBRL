using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EngravingLoadout : MonoBehaviour
{
    public const int SlotCount = 4;
    public const int PassiveSlotCount = 4;

    public static EngravingLoadout Active { get; private set; }

    private sealed class FormState
    {
        public readonly SkillData[] Slots = new SkillData[SlotCount];
        public readonly List<SkillData> Pool = new();
        public bool Seeded;
        public readonly PassiveEngravingData[] PassiveSlots = new PassiveEngravingData[PassiveSlotCount];
        public readonly List<PassiveEngravingData> PassivePool = new();
        public bool PassiveSeeded;
    }

    private readonly Dictionary<PlayerFormId, FormState> _states = new();

    public event Action OnChanged;
    public event Action OnPassiveChanged;

    private void OnEnable()
    {
        Active = this;
    }

    private void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    private FormState GetOrCreate(PlayerFormId form)
    {
        if (!_states.TryGetValue(form, out FormState state))
        {
            state = new FormState();
            _states[form] = state;
        }

        return state;
    }

    private static int PassiveUnlockCount(PlayerFormId form)
    {
        return PlayerPassiveSlotUnlocks.Active != null
            ? PlayerPassiveSlotUnlocks.Active.GetUnlockCount(form)
            : 1;
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

    public void EnsurePassiveSeeded(PlayerFormId form, IReadOnlyList<PassiveEngravingData> seeds)
    {
        FormState state = GetOrCreate(form);
        if (state.PassiveSeeded)
            return;

        for (int i = 0; i < PassiveSlotCount; i++)
            state.PassiveSlots[i] = null;

        if (seeds != null && seeds.Count > 0)
            state.PassiveSlots[0] = seeds[0];

        state.PassiveSeeded = true;
        OnPassiveChanged?.Invoke();
    }

    public PassiveEngravingData GetPassiveSlot(PlayerFormId form, int slot)
    {
        if ((uint)slot >= (uint)PassiveSlotCount)
            return null;

        return _states.TryGetValue(form, out FormState state) ? state.PassiveSlots[slot] : null;
    }

    public bool EquipPassive(PlayerFormId form, int slot, int poolIndex)
    {
        if ((uint)slot >= (uint)PassiveSlotCount)
            return false;

        if (slot >= PassiveUnlockCount(form))
            return false;

        if (!_states.TryGetValue(form, out FormState state))
            return false;

        if ((uint)poolIndex >= (uint)state.PassivePool.Count)
            return false;

        PassiveEngravingData incoming = state.PassivePool[poolIndex];
        state.PassivePool.RemoveAt(poolIndex);
        PassiveEngravingData displaced = state.PassiveSlots[slot];
        state.PassiveSlots[slot] = incoming;
        if (displaced != null)
            state.PassivePool.Add(displaced);
        OnPassiveChanged?.Invoke();
        return true;
    }

    public bool UnequipPassive(PlayerFormId form, int slot)
    {
        if ((uint)slot >= (uint)PassiveSlotCount)
            return false;

        if (!_states.TryGetValue(form, out FormState state))
            return false;

        PassiveEngravingData token = state.PassiveSlots[slot];
        if (token == null)
            return false;

        state.PassiveSlots[slot] = null;
        state.PassivePool.Add(token);
        OnPassiveChanged?.Invoke();
        return true;
    }

    public bool ApplyPassiveArrangement(
        PlayerFormId form,
        IReadOnlyList<PassiveEngravingData> desiredSlots)
    {
        if (desiredSlots == null || desiredSlots.Count != PassiveSlotCount)
            return false;

        if (!_states.TryGetValue(form, out FormState state))
            return false;

        int unlockCount = PassiveUnlockCount(form);
        for (int i = unlockCount; i < PassiveSlotCount; i++)
        {
            if (desiredSlots[i] != null)
                return false;
        }

        List<PassiveEngravingData> remaining =
            new List<PassiveEngravingData>(state.PassivePool.Count + PassiveSlotCount);
        remaining.AddRange(state.PassivePool);
        for (int i = 0; i < PassiveSlotCount; i++)
        {
            if (state.PassiveSlots[i] != null)
                remaining.Add(state.PassiveSlots[i]);
        }

        for (int i = 0; i < PassiveSlotCount; i++)
        {
            PassiveEngravingData token = desiredSlots[i];
            if (token == null)
                continue;

            if (!remaining.Remove(token))
                return false;
        }

        for (int i = 0; i < PassiveSlotCount; i++)
            state.PassiveSlots[i] = desiredSlots[i];

        state.PassivePool.Clear();
        state.PassivePool.AddRange(remaining);
        OnPassiveChanged?.Invoke();
        return true;
    }

    public bool AddPassiveToPool(PlayerFormId form, PassiveEngravingData passive)
    {
        if (passive == null || passive.owningForm != form)
            return false;

        GetOrCreate(form).PassivePool.Add(passive);
        return true;
    }

    public int PassivePoolCount(PlayerFormId form)
    {
        return _states.TryGetValue(form, out FormState state) ? state.PassivePool.Count : 0;
    }

    public PassiveEngravingData GetPassivePoolAt(PlayerFormId form, int index)
    {
        return _states.TryGetValue(form, out FormState state) &&
               (uint)index < (uint)state.PassivePool.Count
            ? state.PassivePool[index]
            : null;
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

    public bool ApplyArrangement(PlayerFormId form, IReadOnlyList<SkillData> desiredSlots)
    {
        if (desiredSlots == null || desiredSlots.Count != SlotCount)
            return false;

        if (!_states.TryGetValue(form, out FormState state))
            return false;

        List<SkillData> remaining = new List<SkillData>(state.Pool.Count + SlotCount);
        remaining.AddRange(state.Pool);
        for (int i = 0; i < SlotCount; i++)
        {
            if (state.Slots[i] != null)
                remaining.Add(state.Slots[i]);
        }

        for (int i = 0; i < SlotCount; i++)
        {
            SkillData token = desiredSlots[i];
            if (token == null)
                continue;

            if (!remaining.Remove(token))
                return false;
        }

        for (int i = 0; i < SlotCount; i++)
            state.Slots[i] = desiredSlots[i];

        state.Pool.Clear();
        state.Pool.AddRange(remaining);
        OnChanged?.Invoke();
        return true;
    }

    public bool AddToPool(PlayerFormId form, SkillData skill)
    {
        if (skill == null)
            return false;

        if (skill is EngravingData engraving && engraving.owningForm != form)
            return false;

        GetOrCreate(form).Pool.Add(skill);
        return true;
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
        OnPassiveChanged?.Invoke();
    }
}
