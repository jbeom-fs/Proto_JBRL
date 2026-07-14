using UnityEngine;

[CreateAssetMenu(fileName = "ComboTierConfig", menuName = "JBRogLike/Combat/Combo Tier Config")]
public sealed class ComboTierConfig : ScriptableObject
{
    [SerializeField, Min(1)] private int stacksPerTier = 5;
    [SerializeField, Min(0)] private int maxTier = 4;
    [SerializeField, Min(0.01f)] private float window = 2f;
    [SerializeField, Min(0)] private int gainPerHit = 1;
    [SerializeField] private float[] tierBonusPct = { 10f, 25f, 45f, 70f };

    public int StacksPerTier => Mathf.Max(1, stacksPerTier);
    public int MaxTier => Mathf.Max(0, maxTier);
    public float Window => Mathf.Max(0.01f, window);
    public int GainPerHit => Mathf.Max(0, gainPerHit);

    public float GetTierBonusPct(int tier)
    {
        int index = tier - 1;
        return tierBonusPct != null && (uint)index < (uint)tierBonusPct.Length
            ? tierBonusPct[index]
            : 0f;
    }

    private void OnValidate()
    {
        int bonusCount = tierBonusPct != null ? tierBonusPct.Length : 0;
        if (bonusCount != maxTier)
        {
            Debug.LogWarning(
                "[ComboTierConfig] tierBonusPct length (" + bonusCount +
                ") must match maxTier (" + maxTier + ").",
                this);
        }
    }
}
