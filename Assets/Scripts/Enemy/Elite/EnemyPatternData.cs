using UnityEngine;

public abstract class EnemyPatternData : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField] private float cooldown = 3f;
    [SerializeField] private float minRange = 0f;
    [SerializeField] private float maxRange = 20f;
    [SerializeField] private int weight = 1;
    [SerializeField] private float recoveryDuration = 0.5f;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float Cooldown => Mathf.Max(0f, cooldown);
    public float MinRange => Mathf.Max(0f, minRange);
    public float MaxRange => Mathf.Max(MinRange, maxRange);
    public int Weight => Mathf.Max(0, weight);
    public float RecoveryDuration => Mathf.Max(0f, recoveryDuration);

    public bool IsInRange(float distance)
    {
        float safeDistance = Mathf.Max(0f, distance);
        return safeDistance >= MinRange && safeDistance <= MaxRange;
    }

    public abstract EnemyPatternRuntime CreateRuntime();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    protected virtual void OnValidate()
    {
        if (cooldown < 0f)
            Debug.LogWarning($"[EnemyPatternData] {name}: cooldown is negative.", this);
        if (minRange < 0f)
            Debug.LogWarning($"[EnemyPatternData] {name}: minRange is negative.", this);
        if (maxRange < minRange)
            Debug.LogWarning($"[EnemyPatternData] {name}: maxRange is smaller than minRange.", this);
        if (weight <= 0)
            Debug.LogWarning($"[EnemyPatternData] {name}: weight must be greater than 0 to participate in weighted selection.", this);
        if (recoveryDuration < 0f)
            Debug.LogWarning($"[EnemyPatternData] {name}: recoveryDuration is negative.", this);
    }
#endif
}
