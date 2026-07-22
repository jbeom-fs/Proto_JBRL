using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BehaviorRuntime
{
    private readonly struct TriggerEntry
    {
        public TriggerEntry(BehaviorAction action, int skillTypeFilter, int value)
        {
            Action = action;
            SkillTypeFilter = skillTypeFilter;
            Value = value;
        }

        public BehaviorAction Action { get; }
        public int SkillTypeFilter { get; }
        public int Value { get; }
    }

    private readonly struct ProcEntry
    {
        public ProcEntry(BehaviorEffect behavior)
        {
            SkillTypeFilter = behavior.skillTypeFilter;
            Skill = behavior.procSkill;
            OriginMode = behavior.procOrigin;
            DirectionMode = behavior.procDirection;
            SpawnRadius = behavior.procSpawnRadius;
        }

        public int SkillTypeFilter { get; }
        public SkillData Skill { get; }
        public ProcOriginMode OriginMode { get; }
        public ProcDirectionMode DirectionMode { get; }
        public float SpawnRadius { get; }
    }

    private readonly Action<int> _healCallback;
    private readonly Action<SkillData, Vector3, Vector2> _procCallback;
    private readonly List<TriggerEntry> _onKill = new List<TriggerEntry>();
    private readonly List<TriggerEntry> _onSkillUsed = new List<TriggerEntry>();
    private readonly List<TriggerEntry> _onCancel = new List<TriggerEntry>();
    private readonly List<ProcEntry> _onKillProcs = new List<ProcEntry>();
    private readonly List<ProcEntry> _onSkillUsedProcs = new List<ProcEntry>();
    private readonly List<ProcEntry> _onCancelProcs = new List<ProcEntry>();
    private readonly List<Vector3> _pendingKillPositions = new List<Vector3>(8);
    private readonly List<AilmentApplication> _attackAilments = new List<AilmentApplication>();

    public BehaviorRuntime(
        Action<int> healCallback,
        Action<SkillData, Vector3, Vector2> procCallback = null)
    {
        _healCallback = healCallback;
        _procCallback = procCallback;
    }

    public IReadOnlyList<AilmentApplication> AttackAilments => _attackAilments;

    public void Rescan(
        IReadOnlyList<InventoryItemStack> items,
        IReadOnlyList<PassiveEngravingData> equippedPassives)
    {
        _onKill.Clear();
        _onSkillUsed.Clear();
        _onCancel.Clear();
        _onKillProcs.Clear();
        _onSkillUsedProcs.Clear();
        _onCancelProcs.Clear();
        _pendingKillPositions.Clear();
        _attackAilments.Clear();

        if (items != null)
        {
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

        if (equippedPassives == null)
            return;

        for (int i = 0; i < equippedPassives.Count; i++)
        {
            PassiveEngravingData passive = equippedPassives[i];
            if (passive != null)
                AddBehaviors(passive.behaviors, 1);
        }
    }

    public void HandleKill(Vector3 enemyPosition, Vector3 playerPosition)
    {
        for (int i = 0; i < _onKill.Count; i++)
            Execute(_onKill[i]);

        if (_onKillProcs.Count > 0)
            _pendingKillPositions.Add(enemyPosition);
    }

    public void FlushKillProcs(Vector3 playerPosition, Vector2 aimDirection)
    {
        if (_pendingKillPositions.Count == 0)
            return;

        Vector3 nearestKillPosition = FindNearestKillPosition(playerPosition);
        for (int i = 0; i < _onKillProcs.Count; i++)
            ExecuteProc(_onKillProcs[i], playerPosition, nearestKillPosition, aimDirection, true);

        _pendingKillPositions.Clear();
    }

    public void HandleSkillUsed(
        SkillData skill,
        Vector3 playerPosition,
        Vector2 aimDirection)
    {
        if (skill == null)
            return;

        int skillTypeBit = 1 << (int)skill.executionType;
        for (int i = 0; i < _onSkillUsed.Count; i++)
        {
            TriggerEntry entry = _onSkillUsed[i];
            if ((entry.SkillTypeFilter & skillTypeBit) != 0)
                Execute(entry);
        }

        for (int i = 0; i < _onSkillUsedProcs.Count; i++)
        {
            ProcEntry entry = _onSkillUsedProcs[i];
            if ((entry.SkillTypeFilter & skillTypeBit) != 0)
                ExecuteProc(entry, playerPosition, playerPosition, aimDirection, false);
        }
    }

    public void HandleCancel(Vector3 playerPosition, Vector2 aimDirection)
    {
        for (int i = 0; i < _onCancel.Count; i++)
            Execute(_onCancel[i]);

        for (int i = 0; i < _onCancelProcs.Count; i++)
            ExecuteProc(_onCancelProcs[i], playerPosition, playerPosition, aimDirection, false);
    }

    private void AddBehaviors(IReadOnlyList<BehaviorEffect> behaviors, int stackCount)
    {
        if (behaviors == null)
            return;

        for (int i = 0; i < behaviors.Count; i++)
        {
            BehaviorEffect behavior = behaviors[i];
            if (behavior == null)
                continue;

            switch (behavior.trigger)
            {
                case BehaviorTrigger.OnKill:
                    AddTriggeredBehavior(behavior, stackCount, _onKill, _onKillProcs);
                    break;
                case BehaviorTrigger.OnSkillUsed:
                    AddTriggeredBehavior(behavior, stackCount, _onSkillUsed, _onSkillUsedProcs);
                    break;
                case BehaviorTrigger.OnSkillCanceled:
                    AddTriggeredBehavior(behavior, stackCount, _onCancel, _onCancelProcs);
                    break;
                case BehaviorTrigger.Passive:
                    AddPassiveAttackAilment(behavior, stackCount);
                    break;
            }
        }
    }

    private static void AddTriggeredBehavior(
        BehaviorEffect behavior,
        int stackCount,
        List<TriggerEntry> triggerEntries,
        List<ProcEntry> procEntries)
    {
        switch (behavior.action)
        {
            case BehaviorAction.Heal:
                triggerEntries.Add(new TriggerEntry(
                    behavior.action,
                    behavior.skillTypeFilter,
                    behavior.value * stackCount));
                break;
            case BehaviorAction.CastSkill:
                if (behavior.procSkill != null)
                    procEntries.Add(new ProcEntry(behavior));
                break;
        }
    }

    private void AddPassiveAttackAilment(BehaviorEffect behavior, int stackCount)
    {
        AilmentType type;
        switch (behavior.action)
        {
            case BehaviorAction.AttackPoison:
                type = AilmentType.Poison;
                break;
            case BehaviorAction.AttackBleed:
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

    private void Execute(TriggerEntry entry)
    {
        if (entry.Action == BehaviorAction.Heal)
            _healCallback?.Invoke(entry.Value);
    }

    private void ExecuteProc(
        ProcEntry entry,
        Vector3 playerPosition,
        Vector3 contextPosition,
        Vector2 aimDirection,
        bool hasHitContext)
    {
        if (entry.Skill == null)
            return;
        if (!hasHitContext && entry.OriginMode == ProcOriginMode.HitPosition)
            return;

        Vector3 origin;
        switch (entry.OriginMode)
        {
            case ProcOriginMode.HitPosition:
                origin = contextPosition;
                break;
            case ProcOriginMode.RandomInRadius:
                origin = playerPosition +
                         (Vector3)(UnityEngine.Random.insideUnitCircle * Mathf.Max(0f, entry.SpawnRadius));
                break;
            default:
                origin = playerPosition;
                break;
        }

        Vector2 direction;
        switch (entry.DirectionMode)
        {
            case ProcDirectionMode.Context:
                direction = hasHitContext
                    ? (Vector2)(contextPosition - playerPosition)
                    : aimDirection;
                if (direction.sqrMagnitude <= 0.0001f)
                    direction = aimDirection;
                break;
            case ProcDirectionMode.Random:
                float radians = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                break;
            default:
                direction = aimDirection;
                break;
        }

        if (direction.sqrMagnitude > 0.0001f)
            direction.Normalize();

        _procCallback?.Invoke(entry.Skill, origin, direction);
    }

    private Vector3 FindNearestKillPosition(Vector3 playerPosition)
    {
        Vector3 nearest = _pendingKillPositions[0];
        float nearestSqrDistance = (nearest - playerPosition).sqrMagnitude;
        for (int i = 1; i < _pendingKillPositions.Count; i++)
        {
            Vector3 candidate = _pendingKillPositions[i];
            float sqrDistance = (candidate - playerPosition).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearest = candidate;
                nearestSqrDistance = sqrDistance;
            }
        }

        return nearest;
    }
}
