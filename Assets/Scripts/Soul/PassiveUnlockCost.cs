using UnityEngine;

public static class PassiveUnlockCost
{
    public static int GetCost(int baseCost, int unlockedCandidateCount)
    {
        return Mathf.Max(0, baseCost) *
               (Mathf.Max(0, unlockedCandidateCount) + 1);
    }
}
