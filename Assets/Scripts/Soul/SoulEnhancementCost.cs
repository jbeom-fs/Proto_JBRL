using UnityEngine;

public static class SoulEnhancementCost
{
    public static int GetMaterialCost(in SoulStatGrowth growth, int currentLevel)
    {
        return Mathf.Max(0, growth.baseMaterialCost) * (Mathf.Max(0, currentLevel) + 1);
    }
}
