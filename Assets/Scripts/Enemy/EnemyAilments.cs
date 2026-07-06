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

        if (bucket.Stacks == 0)
            bucket.TickTimer = profile.TickInterval;

        if (bucket.Stacks < profile.MaxStacks)
        {
            bucket.TotalTickDamage += tickDamage;
            bucket.Stacks += 1;
        }

        bucket.RemainingDuration = Mathf.Max(bucket.RemainingDuration, duration);
        _buckets[index] = bucket;
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
            while (bucket.TickTimer <= 0f)
            {
                _applyTickDamage?.Invoke(Mathf.Max(1, Mathf.RoundToInt(bucket.TotalTickDamage)));
                bucket.TickTimer += profile.TickInterval;
            }

            if (bucket.RemainingDuration <= 0f)
                bucket = default;

            _buckets[i] = bucket;
        }
    }

    public void Clear()
    {
        for (int i = 0; i < _buckets.Length; i++)
            _buckets[i] = default;
    }

    public int GetStacks(AilmentType type)
    {
        return TryGetIndex(type, out int index) ? _buckets[index].Stacks : 0;
    }

    private static bool TryGetIndex(AilmentType type, out int index)
    {
        index = (int)type;
        return (uint)index < AilmentTypeCount;
    }
}
