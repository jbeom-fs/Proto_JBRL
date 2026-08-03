using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BehaviorRuntime
{
    private readonly struct TriggerEntry
    {
        public TriggerEntry(
            BehaviorAction action,
            int skillTypeFilter,
            int value,
            float duration,
            ShieldSource shieldSource)
        {
            Action = action;
            SkillTypeFilter = skillTypeFilter;
            Value = value;
            Duration = duration;
            ShieldSource = shieldSource;
        }

        public BehaviorAction Action { get; }
        public int SkillTypeFilter { get; }
        public int Value { get; }
        public float Duration { get; }
        public ShieldSource ShieldSource { get; }
    }

    private readonly struct ProcEntry
    {
        public ProcEntry(BehaviorEffect behavior)
        {
            SkillTypeFilter = behavior.skillTypeFilter;
            ComboTierDamages = behavior.comboTierDamages;
            Skill = behavior.procSkill;
            OriginMode = behavior.procOrigin;
            DirectionMode = behavior.procDirection;
            SpawnRadius = behavior.procSpawnRadius;
        }

        public int SkillTypeFilter { get; }
        public int[] ComboTierDamages { get; }
        public SkillData Skill { get; }
        public ProcOriginMode OriginMode { get; }
        public ProcDirectionMode DirectionMode { get; }
        public float SpawnRadius { get; }
    }

    private readonly struct LifestealEngineEntry
    {
        public LifestealEngineEntry(BehaviorEffect behavior, int stackCount)
        {
            BasePct = behavior.value * stackCount;
            LowHealthThresholdPct = behavior.lowHealthThresholdPct;
            LostHealthPctPerBonusPct =
                behavior.lostHealthPctPerLifestealPct;
            ShieldConversionPct = behavior.overhealShieldConversionPct;
            ShieldCapPct = behavior.lifestealShieldCapPct;
            ShieldDuration = behavior.lifestealShieldDuration;
            StackCount = stackCount;
        }

        public float BasePct { get; }
        public float LowHealthThresholdPct { get; }
        public float LostHealthPctPerBonusPct { get; }
        public float ShieldConversionPct { get; }
        public float ShieldCapPct { get; }
        public float ShieldDuration { get; }
        public int StackCount { get; }
    }

    private readonly struct ExecuteThresholdEntry
    {
        public ExecuteThresholdEntry(BehaviorEffect behavior)
        {
            Settings = new ExecuteThresholdSettings(
                behavior.executeThresholdHpPct / 100f,
                behavior.executeEliteBossStartHpPct / 100f,
                behavior.executeEliteBossStartBonusPct / 100f,
                behavior.executeEliteBossIntervalHpPct / 100f,
                behavior.executeEliteBossBonusPerIntervalPct / 100f);
        }

        public ExecuteThresholdSettings Settings { get; }
    }

    private readonly Action<int> _healCallback;
    private readonly Action<ShieldSource, int, float> _shieldCallback;
    private readonly Action<int, float> _attackBuffCallback;
    private readonly Action<SkillData, Vector3, Vector2, int?> _procCallback;
    private readonly List<TriggerEntry> _onKill = new List<TriggerEntry>();
    private readonly List<TriggerEntry> _onSkillUsed = new List<TriggerEntry>();
    private readonly List<TriggerEntry> _onCancel = new List<TriggerEntry>();
    private readonly List<TriggerEntry> _onMarkerDetonate =
        new List<TriggerEntry>();
    private readonly List<ProcEntry> _onKillProcs = new List<ProcEntry>();
    private readonly List<ProcEntry> _onSkillUsedProcs = new List<ProcEntry>();
    private readonly List<ProcEntry> _onCancelProcs = new List<ProcEntry>();
    private readonly List<ProcEntry> _onMarkerDetonateProcs =
        new List<ProcEntry>();
    private readonly List<Vector3> _pendingKillPositions = new List<Vector3>(8);
    private readonly List<AilmentApplication> _attackAilments = new List<AilmentApplication>();
    private readonly List<LifestealEngineEntry> _lifestealEngines =
        new List<LifestealEngineEntry>();
    private readonly List<AilmentOverloadSettings> _ailmentOverloads =
        new List<AilmentOverloadSettings>();
    private readonly List<ExecuteThresholdEntry> _executeThresholds =
        new List<ExecuteThresholdEntry>();

    public BehaviorRuntime(
        Action<int> healCallback,
        Action<SkillData, Vector3, Vector2, int?> procCallback = null,
        Action<ShieldSource, int, float> shieldCallback = null,
        Action<int, float> attackBuffCallback = null)
    {
        _healCallback = healCallback;
        _procCallback = procCallback;
        _shieldCallback = shieldCallback;
        _attackBuffCallback = attackBuffCallback;
    }

    public IReadOnlyList<AilmentApplication> AttackAilments => _attackAilments;
    public IReadOnlyList<AilmentOverloadSettings> AilmentOverloads =>
        _ailmentOverloads;

    public float GetLifestealBonusPct(float hpRatio)
    {
        float hpPct = Mathf.Clamp01(hpRatio) * 100f;
        float totalPct = 0f;
        for (int i = 0; i < _lifestealEngines.Count; i++)
        {
            LifestealEngineEntry entry = _lifestealEngines[i];
            totalPct += entry.BasePct;

            float thresholdPct = Mathf.Clamp(
                entry.LowHealthThresholdPct,
                0f,
                100f);
            float stepPct = entry.LostHealthPctPerBonusPct;
            if (hpPct >= thresholdPct || stepPct <= 0f)
                continue;

            totalPct += Mathf.Floor(
                (thresholdPct - hpPct) / stepPct) * entry.StackCount;
        }

        return totalPct;
    }

    public bool TryGetLifestealShieldParameters(
        out float conversionPct,
        out float capPct,
        out float duration)
    {
        if (_lifestealEngines.Count == 0)
        {
            conversionPct = 0f;
            capPct = 0f;
            duration = 0f;
            return false;
        }

        // Current content assumes one engine per loadout. If multiple exist,
        // lifesteal bonuses stack but shield parameters use first scan entry.
        LifestealEngineEntry first = _lifestealEngines[0];
        conversionPct = first.ShieldConversionPct;
        capPct = first.ShieldCapPct;
        duration = first.ShieldDuration;
        return true;
    }

    public bool TryGetExecuteThresholdSettings(
        out ExecuteThresholdSettings settings)
    {
        if (_executeThresholds.Count == 0)
        {
            settings = default;
            return false;
        }

        // Current loadout supports one equipped passive per form.
        settings = _executeThresholds[0].Settings;
        return settings.Enabled;
    }

    public void Rescan(
        IReadOnlyList<InventoryItemStack> items,
        IReadOnlyList<PassiveEngravingData> equippedPassives)
    {
        _onKill.Clear();
        _onSkillUsed.Clear();
        _onCancel.Clear();
        _onMarkerDetonate.Clear();
        _onKillProcs.Clear();
        _onSkillUsedProcs.Clear();
        _onCancelProcs.Clear();
        _onMarkerDetonateProcs.Clear();
        _pendingKillPositions.Clear();
        _attackAilments.Clear();
        _lifestealEngines.Clear();
        _ailmentOverloads.Clear();
        _executeThresholds.Clear();

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

                AddBehaviors(
                    item.BehaviorEffects,
                    stack.Count,
                    ShieldSource.Relic);
            }
        }

        if (equippedPassives == null)
            return;

        for (int i = 0; i < equippedPassives.Count; i++)
        {
            PassiveEngravingData passive = equippedPassives[i];
            if (passive != null)
            {
                AddBehaviors(
                    passive.behaviors,
                    1,
                    ShieldSource.PassiveEngraving);
            }
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
        Vector2 aimDirection,
        int comboTier)
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
            if ((entry.SkillTypeFilter & skillTypeBit) == 0)
                continue;

            int? skillDamageOverride = null;
            if (entry.ComboTierDamages != null &&
                entry.ComboTierDamages.Length > 0)
            {
                if (comboTier <= 0)
                    continue;

                int damageIndex = Mathf.Min(
                    comboTier,
                    entry.ComboTierDamages.Length) - 1;
                skillDamageOverride = entry.ComboTierDamages[damageIndex];
            }

            ExecuteProc(
                entry,
                playerPosition,
                playerPosition,
                aimDirection,
                false,
                skillDamageOverride);
        }
    }

    public void HandleCancel(Vector3 playerPosition, Vector2 aimDirection)
    {
        for (int i = 0; i < _onCancel.Count; i++)
            Execute(_onCancel[i]);

        for (int i = 0; i < _onCancelProcs.Count; i++)
            ExecuteProc(_onCancelProcs[i], playerPosition, playerPosition, aimDirection, false);
    }

    public void HandleMarkerDetonate(
        Vector3 playerPosition,
        Vector3 detonationPosition,
        Vector2 aimDirection)
    {
        // Marker detonation is an event, not a skill use.
        // skillTypeFilter intentionally does not apply on this trigger.
        for (int i = 0; i < _onMarkerDetonate.Count; i++)
            Execute(_onMarkerDetonate[i]);

        for (int i = 0; i < _onMarkerDetonateProcs.Count; i++)
        {
            ExecuteProc(
                _onMarkerDetonateProcs[i],
                playerPosition,
                detonationPosition,
                aimDirection,
                true);
        }
    }

    private void AddBehaviors(
        IReadOnlyList<BehaviorEffect> behaviors,
        int stackCount,
        ShieldSource shieldSource)
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
                    AddTriggeredBehavior(
                        behavior,
                        stackCount,
                        shieldSource,
                        _onKill,
                        _onKillProcs);
                    break;
                case BehaviorTrigger.OnSkillUsed:
                    AddTriggeredBehavior(
                        behavior,
                        stackCount,
                        shieldSource,
                        _onSkillUsed,
                        _onSkillUsedProcs);
                    break;
                case BehaviorTrigger.OnSkillCanceled:
                    AddTriggeredBehavior(
                        behavior,
                        stackCount,
                        shieldSource,
                        _onCancel,
                        _onCancelProcs);
                    break;
                case BehaviorTrigger.OnMarkerDetonate:
                    AddTriggeredBehavior(
                        behavior,
                        stackCount,
                        shieldSource,
                        _onMarkerDetonate,
                        _onMarkerDetonateProcs);
                    break;
                case BehaviorTrigger.Passive:
                    AddPassiveBehavior(behavior, stackCount);
                    break;
            }
        }
    }

    private static void AddTriggeredBehavior(
        BehaviorEffect behavior,
        int stackCount,
        ShieldSource shieldSource,
        List<TriggerEntry> triggerEntries,
        List<ProcEntry> procEntries)
    {
        switch (behavior.action)
        {
            case BehaviorAction.Heal:
            case BehaviorAction.Shield:
            case BehaviorAction.AttackBuff:
                triggerEntries.Add(new TriggerEntry(
                    behavior.action,
                    behavior.skillTypeFilter,
                    behavior.value * stackCount,
                    behavior.duration,
                    shieldSource));
                break;
            case BehaviorAction.CastSkill:
                if (behavior.procSkill != null)
                    procEntries.Add(new ProcEntry(behavior));
                break;
        }
    }

    private void AddPassiveBehavior(BehaviorEffect behavior, int stackCount)
    {
        if (behavior.action == BehaviorAction.LifestealEngine)
        {
            _lifestealEngines.Add(
                new LifestealEngineEntry(behavior, stackCount));
            return;
        }

        if (behavior.action == BehaviorAction.AilmentOverload)
        {
            _ailmentOverloads.Add(
                new AilmentOverloadSettings(
                    behavior.ailmentOverloadType,
                    behavior.ailmentOverloadBonusPct / 100f));
            return;
        }

        if (behavior.action == BehaviorAction.ExecuteThreshold)
        {
            _executeThresholds.Add(new ExecuteThresholdEntry(behavior));
            return;
        }

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
            stacks = behavior.value * stackCount
        });
    }

    private void Execute(TriggerEntry entry)
    {
        switch (entry.Action)
        {
            case BehaviorAction.Heal:
                _healCallback?.Invoke(entry.Value);
                break;
            case BehaviorAction.Shield:
                _shieldCallback?.Invoke(
                    entry.ShieldSource,
                    entry.Value,
                    entry.Duration);
                break;
            case BehaviorAction.AttackBuff:
                _attackBuffCallback?.Invoke(entry.Value, entry.Duration);
                break;
        }
    }

    private void ExecuteProc(
        ProcEntry entry,
        Vector3 playerPosition,
        Vector3 contextPosition,
        Vector2 aimDirection,
        bool hasHitContext,
        int? skillDamageOverride = null)
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

        _procCallback?.Invoke(
            entry.Skill,
            origin,
            direction,
            skillDamageOverride);
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
