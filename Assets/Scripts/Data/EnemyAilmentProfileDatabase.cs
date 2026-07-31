using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "EnemyAilmentProfiles",
    menuName = "JBRogLike/Combat/Enemy Ailment Profiles")]
public sealed class EnemyAilmentProfileDatabase : ScriptableObject
{
    [Serializable]
    public struct Profile
    {
        [SerializeField, Min(0.01f)] private float tickInterval;
        [SerializeField, Min(1)] private int maxStacks;
        [SerializeField, Min(0f)] private float damagePerStack;

        public float TickInterval => tickInterval;
        public int MaxStacks => maxStacks;
        public float DamagePerStack => damagePerStack;

        public Profile(float tickInterval, int maxStacks, float damagePerStack)
        {
            this.tickInterval = tickInterval;
            this.maxStacks = maxStacks;
            this.damagePerStack = damagePerStack;
        }

        public bool IsValid =>
            tickInterval > 0f &&
            maxStacks > 0 &&
            damagePerStack > 0f;
    }

    [SerializeField] private Profile poison = new Profile(1f, 10, 5f);
    [SerializeField] private Profile bleed = new Profile(0.5f, 5, 5f);

    public bool TryGetProfile(AilmentType type, out Profile profile)
    {
        switch (type)
        {
            case AilmentType.Poison:
                profile = poison;
                return true;
            case AilmentType.Bleed:
                profile = bleed;
                return true;
            default:
                profile = default;
                return false;
        }
    }

    public bool TryValidate(out string error)
    {
        if (!poison.IsValid)
        {
            error = "Poison profile requires tickInterval > 0, maxStacks > 0, and damagePerStack > 0.";
            return false;
        }

        if (!bleed.IsValid)
        {
            error = "Bleed profile requires tickInterval > 0, maxStacks > 0, and damagePerStack > 0.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
