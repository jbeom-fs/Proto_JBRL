using System;
using System.Collections.Generic;

public readonly struct AilmentStatus
{
    public StatusEffectIconType Type { get; }
    public int StackCount { get; }

    public AilmentStatus(StatusEffectIconType type, int stackCount)
    {
        Type = type;
        StackCount = stackCount;
    }
}

public sealed class AilmentStatusTracker
{
    private static readonly StatusEffectIconType[] s_StatusTypes =
        (StatusEffectIconType[])Enum.GetValues(typeof(StatusEffectIconType));

    private readonly bool[] _active = new bool[s_StatusTypes.Length];
    private readonly List<AilmentStatus> _activeStatuses = new List<AilmentStatus>(s_StatusTypes.Length);

    public IReadOnlyList<AilmentStatus> ActiveStatuses => _activeStatuses;
    public bool OrderChanged { get; private set; }

    public bool Update(int poisonStacks, int bleedStacks, bool isSlowed, bool isStunned)
    {
        OrderChanged = false;

        UpdateStatus(StatusEffectIconType.Poison, poisonStacks > 0, poisonStacks);
        UpdateStatus(StatusEffectIconType.Bleed, bleedStacks > 0, bleedStacks);
        UpdateStatus(StatusEffectIconType.Slow, isSlowed, 0);
        UpdateStatus(StatusEffectIconType.Stun, isStunned, 0);

        return OrderChanged;
    }

    public bool IsActive(StatusEffectIconType type)
    {
        int index = GetStatusIndex(type);
        return index >= 0 && _active[index];
    }

    public void Reset()
    {
        for (int i = 0; i < _active.Length; i++)
            _active[i] = false;

        _activeStatuses.Clear();
        OrderChanged = false;
    }

    private void UpdateStatus(StatusEffectIconType type, bool active, int stackCount)
    {
        int statusIndex = GetStatusIndex(type);
        if (statusIndex < 0)
            return;

        bool wasActive = _active[statusIndex];
        if (wasActive != active)
        {
            _active[statusIndex] = active;
            if (active)
                _activeStatuses.Add(new AilmentStatus(type, stackCount));
            else
                RemoveStatus(type);

            OrderChanged = true;
            return;
        }

        if (active)
            SetStackCount(type, stackCount);
    }

    private void RemoveStatus(StatusEffectIconType type)
    {
        for (int i = 0; i < _activeStatuses.Count; i++)
        {
            if (_activeStatuses[i].Type != type)
                continue;

            _activeStatuses.RemoveAt(i);
            return;
        }
    }

    private void SetStackCount(StatusEffectIconType type, int stackCount)
    {
        for (int i = 0; i < _activeStatuses.Count; i++)
        {
            if (_activeStatuses[i].Type != type)
                continue;

            if (_activeStatuses[i].StackCount != stackCount)
                _activeStatuses[i] = new AilmentStatus(type, stackCount);
            return;
        }
    }

    private static int GetStatusIndex(StatusEffectIconType type)
    {
        return Array.IndexOf(s_StatusTypes, type);
    }
}
