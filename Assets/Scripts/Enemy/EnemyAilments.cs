using System;
using UnityEngine;

public enum AilmentType
{
    Poison = 0,
    Bleed = 1
}

[Serializable]
public struct AilmentApplication
{
    public AilmentType type;
    [Min(1)] public int stacks;
}

public readonly struct AilmentOverloadSettings
{
    public AilmentOverloadSettings(
        AilmentType type,
        int thresholdStacks,
        float bonusRate)
    {
        Enabled = true;
        Type = type;
        ThresholdStacks = thresholdStacks;
        BonusRate = bonusRate;
    }

    public bool Enabled { get; }
    public AilmentType Type { get; }
    public int ThresholdStacks { get; }
    public float BonusRate { get; }

    public bool ShouldTrigger(AilmentType type, int stacks)
    {
        return Enabled &&
               Type == type &&
               ThresholdStacks > 0 &&
               stacks >= ThresholdStacks;
    }
}

public readonly struct AilmentDeliveryContext
{
    public AilmentDeliveryContext(
        float damageMultiplier,
        AilmentOverloadSettings overload)
    {
        DamageMultiplier = damageMultiplier;
        Overload = overload;
    }

    public float DamageMultiplier { get; }
    public AilmentOverloadSettings Overload { get; }

    public static AilmentDeliveryContext Default =>
        new AilmentDeliveryContext(1f, default);
}

public sealed class EnemyAilments
{
    private const int AilmentTypeCount = 2;

    private readonly Action<int> _applyTickDamage;
    private readonly EnemyAilmentProfileDatabase _profiles;
    private readonly Bucket[] _buckets = new Bucket[AilmentTypeCount];
    private readonly AilmentType[] _activeOrder = new AilmentType[AilmentTypeCount];
    private int _activeOrderCount;
    private int _version;

    private struct Bucket
    {
        public int Stacks;
        public float TickTimer;
        public float DamagePerStack;
    }

    public EnemyAilments(
        EnemyAilmentProfileDatabase profiles,
        Action<int> applyTickDamage)
    {
        _profiles = profiles;
        _applyTickDamage = applyTickDamage;
    }

    public bool HasAny
    {
        get
        {
            for (int i = 0; i < _buckets.Length; i++)
            {
                if (_buckets[i].Stacks > 0)
                    return true;
            }

            return false;
        }
    }

    public void Apply(
        AilmentType type,
        int stacks,
        in AilmentDeliveryContext context)
    {
        if (stacks <= 0 ||
            context.DamageMultiplier <= 0f ||
            _profiles == null ||
            !TryGetIndex(type, out int index) ||
            !_profiles.TryGetProfile(
                type,
                out EnemyAilmentProfileDatabase.Profile profile) ||
            !profile.IsValid)
        {
            return;
        }

        Bucket bucket = _buckets[index];
        bool wasInactive = bucket.Stacks == 0;
        int acceptedStacks = Mathf.Min(stacks, profile.MaxStacks - bucket.Stacks);
        if (acceptedStacks <= 0)
            return;

        if (wasInactive)
            bucket.TickTimer = profile.TickInterval;

        bucket.Stacks += acceptedStacks;
        bucket.DamagePerStack =
            profile.DamagePerStack * context.DamageMultiplier;
        _buckets[index] = bucket;

        if (wasInactive)
            AppendActiveType(type);

        AilmentOverloadSettings overload = context.Overload;
        if (!overload.ShouldTrigger(type, bucket.Stacks))
            return;

        float stackCount = bucket.Stacks;
        float remainingDotTotal =
            bucket.DamagePerStack *
            stackCount *
            (stackCount + 1f) *
            0.5f;
        float overloadDamage =
            remainingDotTotal * (1f + overload.BonusRate);
        int versionBeforeOverload = _version;
        _applyTickDamage?.Invoke(
            Mathf.Max(1, Mathf.RoundToInt(overloadDamage)));
        if (_version != versionBeforeOverload)
            return;

        _buckets[index] = default;
        RemoveActiveType(type);
    }

    public void Tick(float dt)
    {
        if (dt <= 0f)
            return;

        for (int i = 0; i < _buckets.Length; i++)
        {
            Bucket bucket = _buckets[i];
            if (bucket.Stacks <= 0)
                continue;

            if (_profiles == null ||
                !_profiles.TryGetProfile(
                    (AilmentType)i,
                    out EnemyAilmentProfileDatabase.Profile profile) ||
                !profile.IsValid)
            {
                continue;
            }

            bucket.TickTimer -= dt;
            int versionBeforeTick = _version;
            while (bucket.TickTimer <= 0f && bucket.Stacks > 0)
            {
                float tickDamage = bucket.DamagePerStack * bucket.Stacks;
                _applyTickDamage?.Invoke(
                    Mathf.Max(1, Mathf.RoundToInt(tickDamage)));
                if (_version != versionBeforeTick)
                    return;

                bucket.Stacks--;
                if (bucket.Stacks == 0)
                {
                    bucket = default;
                    RemoveActiveType((AilmentType)i);
                    break;
                }

                bucket.TickTimer += profile.TickInterval;
            }

            _buckets[i] = bucket;
        }
    }

    public void Clear()
    {
        for (int i = 0; i < _buckets.Length; i++)
            _buckets[i] = default;

        _activeOrderCount = 0;
        _version++;
    }

    public int GetStacks(AilmentType type)
    {
        return TryGetIndex(type, out int index) ? _buckets[index].Stacks : 0;
    }

    public bool TryGetFirstActiveType(out AilmentType type)
    {
        if (_activeOrderCount > 0)
        {
            type = _activeOrder[0];
            return true;
        }

        type = default;
        return false;
    }

    private void AppendActiveType(AilmentType type)
    {
        if (_activeOrderCount >= _activeOrder.Length)
            return;

        _activeOrder[_activeOrderCount] = type;
        _activeOrderCount++;
    }

    private void RemoveActiveType(AilmentType type)
    {
        for (int i = 0; i < _activeOrderCount; i++)
        {
            if (_activeOrder[i] != type)
                continue;

            for (int j = i; j < _activeOrderCount - 1; j++)
                _activeOrder[j] = _activeOrder[j + 1];

            _activeOrderCount--;
            return;
        }
    }

    private static bool TryGetIndex(AilmentType type, out int index)
    {
        index = (int)type;
        return (uint)index < AilmentTypeCount;
    }
}
