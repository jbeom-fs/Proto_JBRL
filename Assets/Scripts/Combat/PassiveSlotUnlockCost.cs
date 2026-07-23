using UnityEngine;

public static class PassiveSlotUnlockCost
{
    public static int GetCost(int baseCost, int currentCount)
    {
        return Mathf.Max(0, baseCost) * Mathf.Max(0, currentCount);
    }
}
