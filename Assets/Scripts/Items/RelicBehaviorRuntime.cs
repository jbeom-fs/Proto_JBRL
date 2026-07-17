using System;
using System.Collections.Generic;

public sealed class RelicBehaviorRuntime
{
    private readonly struct RuntimeEntry
    {
        public RuntimeEntry(RelicAction action, int skillTypeFilter, int value)
        {
            Action = action;
            SkillTypeFilter = skillTypeFilter;
            Value = value;
        }

        public RelicAction Action { get; }
        public int SkillTypeFilter { get; }
        public int Value { get; }
    }

    private readonly Action<int> _healCallback;
    private readonly List<RuntimeEntry> _onKill = new List<RuntimeEntry>();
    private readonly List<RuntimeEntry> _onSkillUsed = new List<RuntimeEntry>();
    private readonly List<AilmentApplication> _attackAilments = new List<AilmentApplication>();

    public RelicBehaviorRuntime(Action<int> healCallback)
    {
        _healCallback = healCallback;
    }

    public IReadOnlyList<AilmentApplication> AttackAilments => _attackAilments;

    public void Rescan(IReadOnlyList<InventoryItemStack> items)
    {
        _onKill.Clear();
        _onSkillUsed.Clear();
        _attackAilments.Clear();

        if (items == null)
            return;

        for (int i = 0; i < items.Count; i++)
        {
            InventoryItemStack stack = items[i];
            if (stack == null || stack.Count <= 0)
                continue;

            ItemData item = stack.Item;
            if (item == null || item.ItemType != ItemType.Relic)
                continue;

            AddBehaviors(item.BehaviorEffects, stack.Count);
        }
    }

    public void HandleKill()
    {
        for (int i = 0; i < _onKill.Count; i++)
            Execute(_onKill[i]);
    }

    public void HandleSkillUsed(SkillData skill)
    {
        if (skill == null)
            return;

        int skillTypeBit = 1 << (int)skill.executionType;
        for (int i = 0; i < _onSkillUsed.Count; i++)
        {
            RuntimeEntry entry = _onSkillUsed[i];
            if ((entry.SkillTypeFilter & skillTypeBit) != 0)
                Execute(entry);
        }
    }

    private void AddBehaviors(IReadOnlyList<RelicBehavior> behaviors, int stackCount)
    {
        if (behaviors == null)
            return;

        for (int i = 0; i < behaviors.Count; i++)
        {
            RelicBehavior behavior = behaviors[i];
            if (behavior == null)
                continue;

            RuntimeEntry entry = new RuntimeEntry(
                behavior.action,
                behavior.skillTypeFilter,
                behavior.value * stackCount);

            switch (behavior.trigger)
            {
                case RelicTrigger.OnKill:
                    _onKill.Add(entry);
                    break;
                case RelicTrigger.OnSkillUsed:
                    _onSkillUsed.Add(entry);
                    break;
                case RelicTrigger.Passive:
                    AddPassiveAttackAilment(behavior, stackCount);
                    break;
            }
        }
    }

    private void AddPassiveAttackAilment(RelicBehavior behavior, int stackCount)
    {
        AilmentType type;
        switch (behavior.action)
        {
            case RelicAction.AttackPoison:
                type = AilmentType.Poison;
                break;
            case RelicAction.AttackBleed:
                type = AilmentType.Bleed;
                break;
            default:
                return;
        }

        _attackAilments.Add(new AilmentApplication
        {
            type = type,
            tickDamage = behavior.value * stackCount,
            duration = behavior.duration
        });
    }

    private void Execute(RuntimeEntry entry)
    {
        switch (entry.Action)
        {
            case RelicAction.Heal:
                _healCallback?.Invoke(entry.Value);
                break;
        }
    }
}
