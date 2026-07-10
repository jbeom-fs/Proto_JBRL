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
    public float tickDamage;
    public float duration;
}

public sealed class EnemyAilments
{
    private const int AilmentTypeCount = 2;

    private static readonly AilmentProfile[] s_Profiles =
    {
        new AilmentProfile(1.0f, 10),
        new AilmentProfile(0.5f, 5)
    };

    private readonly Action<int> _applyTickDamage;
    private readonly Bucket[] _buckets = new Bucket[AilmentTypeCount];
    private readonly AilmentType[] _activeOrder = new AilmentType[AilmentTypeCount];
    private int _activeOrderCount;
    private int _version;

    private readonly struct AilmentProfile
    {
        public readonly float TickInterval;
        public readonly int MaxStacks;

        public AilmentProfile(float tickInterval, int maxStacks)
        {
            TickInterval = tickInterval;
            MaxStacks = maxStacks;
        }
    }

    private struct Bucket
    {
        public float TotalTickDamage;
        public int Stacks;
        public float RemainingDuration;
        public float TickTimer;
    }

    public EnemyAilments(Action<int> applyTickDamage)
    {
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

    public void Apply(AilmentType type, float tickDamage, float duration)
    {
        if (tickDamage <= 0f || duration <= 0f || !TryGetIndex(type, out int index))
            return;

        AilmentProfile profile = s_Profiles[index];
        Bucket bucket = _buckets[index];
        bool wasInactive = bucket.Stacks == 0;

        if (bucket.Stacks == 0)
            bucket.TickTimer = profile.TickInterval;

        if (bucket.Stacks < profile.MaxStacks)
        {
            bucket.TotalTickDamage += tickDamage;
            bucket.Stacks += 1;
        }

        bucket.RemainingDuration = Mathf.Max(bucket.RemainingDuration, duration);
        _buckets[index] = bucket;

        if (wasInactive && bucket.Stacks > 0)
            AppendActiveType(type);
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

            float activeDelta = Mathf.Min(dt, bucket.RemainingDuration);
            bucket.RemainingDuration -= dt;
            bucket.TickTimer -= activeDelta;

            AilmentProfile profile = s_Profiles[i];
            int versionBeforeTick = _version;
            while (bucket.TickTimer <= 0f)
            {
                _applyTickDamage?.Invoke(Mathf.Max(1, Mathf.RoundToInt(bucket.TotalTickDamage)));
                if (_version != versionBeforeTick)
                    return;

                bucket.TickTimer += profile.TickInterval;
            }

            if (bucket.RemainingDuration <= 0f)
            {
                bucket = default;
                RemoveActiveType((AilmentType)i);
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
