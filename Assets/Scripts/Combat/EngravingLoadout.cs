using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EngravingLoadout : MonoBehaviour
{
    public const int SlotCount = 4;

    public static EngravingLoadout Active { get; private set; }

    private sealed class FormState
    {
        public readonly SkillData[] Slots = new SkillData[SlotCount];
        public readonly List<SkillData> Pool = new();
        public bool Seeded;
        public PassiveEngravingData PassiveEngraving;
        public bool PassiveSeeded;
    }

    private readonly Dictionary<PlayerFormId, FormState> _states = new();

    public event Action OnChanged;
    public event Action OnPassiveChanged;
    // UI 표시 전용. PlayerCombatController·BehaviorRuntime은 구독 금지.
    public event Action OnPoolChanged;

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

        state.PassiveEngraving = seeds != null && seeds.Count > 0 ? seeds[0] : null;

        state.PassiveSeeded = true;
        OnPassiveChanged?.Invoke();
    }

    public PassiveEngravingData GetPassive(PlayerFormId form)
    {
        return _states.TryGetValue(form, out FormState state) ? state.PassiveEngraving : null;
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

    public bool CanApplyArrangement(PlayerFormId form, IReadOnlyList<SkillData> desiredSlots)
    {
        return TryPrepareArrangement(form, desiredSlots, out _, out _);
    }

    public bool ApplyArrangement(PlayerFormId form, IReadOnlyList<SkillData> desiredSlots)
    {
        if (!TryPrepareArrangement(form, desiredSlots, out FormState state, out List<SkillData> remaining))
            return false;

        for (int i = 0; i < SlotCount; i++)
            state.Slots[i] = desiredSlots[i];

        state.Pool.Clear();
        state.Pool.AddRange(remaining);
        OnChanged?.Invoke();
        return true;
    }

    private bool TryPrepareArrangement(
        PlayerFormId form,
        IReadOnlyList<SkillData> desiredSlots,
        out FormState state,
        out List<SkillData> remaining)
    {
        state = null;
        remaining = null;

        if (desiredSlots == null || desiredSlots.Count != SlotCount)
            return false;

        if (!_states.TryGetValue(form, out FormState formState))
            return false;

        List<SkillData> remainingTokens = new List<SkillData>(formState.Pool.Count + SlotCount);
        remainingTokens.AddRange(formState.Pool);
        for (int i = 0; i < SlotCount; i++)
        {
            if (formState.Slots[i] != null)
                remainingTokens.Add(formState.Slots[i]);
        }

        for (int i = 0; i < SlotCount; i++)
        {
            SkillData token = desiredSlots[i];
            if (token == null)
                continue;

            if (!remainingTokens.Remove(token))
                return false;
        }

        state = formState;
        remaining = remainingTokens;
        return true;
    }

    public bool AddToPool(PlayerFormId form, SkillData skill)
    {
        if (skill == null)
            return false;

        if (skill is EngravingData engraving && engraving.owningForm != form)
            return false;

        GetOrCreate(form).Pool.Add(skill);
        OnPoolChanged?.Invoke();
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
        OnPoolChanged?.Invoke();
    }
}
