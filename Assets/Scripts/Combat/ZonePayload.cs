public readonly struct ZonePayload
{
    public ZonePayload(
        int tickDamage,
        float duration,
        float slowPercentage,
        float slowDuration,
        AilmentApplication[] ailments,
        float ailmentMultiplier,
        float radius,
        float tickInterval)
    {
        TickDamage = tickDamage;
        Duration = duration;
        SlowPercentage = slowPercentage;
        SlowDuration = slowDuration;
        Ailments = ailments;
        AilmentMultiplier = ailmentMultiplier;
        Radius = radius;
        TickInterval = tickInterval;
    }

    public int TickDamage { get; }
    public float Duration { get; }
    public float SlowPercentage { get; }
    public float SlowDuration { get; }
    public AilmentApplication[] Ailments { get; }
    public float AilmentMultiplier { get; }
    public float Radius { get; }
    public float TickInterval { get; }

    public bool HasAnyEffect =>
        TickDamage > 0 ||
        SlowPercentage > 0f ||
        (Ailments != null && Ailments.Length > 0);
}
