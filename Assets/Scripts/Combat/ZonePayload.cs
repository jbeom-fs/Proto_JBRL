public readonly struct ZonePayload
{
    public ZonePayload(
        int tickDamage,
        float duration,
        float slowPercentage,
        float slowDuration,
        AilmentApplication[] ailments,
        AilmentDeliveryContext ailmentContext,
        float radius,
        float tickInterval)
    {
        TickDamage = tickDamage;
        Duration = duration;
        SlowPercentage = slowPercentage;
        SlowDuration = slowDuration;
        Ailments = ailments;
        AilmentContext = ailmentContext;
        Radius = radius;
        TickInterval = tickInterval;
    }

    public int TickDamage { get; }
    public float Duration { get; }
    public float SlowPercentage { get; }
    public float SlowDuration { get; }
    public AilmentApplication[] Ailments { get; }
    public AilmentDeliveryContext AilmentContext { get; }
    public float Radius { get; }
    public float TickInterval { get; }

    public bool HasAnyEffect =>
        TickDamage > 0 ||
        SlowPercentage > 0f ||
        (Ailments != null && Ailments.Length > 0);
}
