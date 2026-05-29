using UnityEngine;

public static class SkillProjectileUtility
{
    public static int GetEffectiveProjectileCount(SkillData skill)
    {
        if (skill == null)
            return 0;

        switch (skill.projectileFirePattern)
        {
            case ProjectileFirePattern.Burst:
            case ProjectileFirePattern.Spread:
            case ProjectileFirePattern.Circle:
                return Mathf.Max(1, skill.projectileCount);

            default:
                return 1;
        }
    }

    public static bool AllowsPartialBulletUse(SkillData skill)
    {
        return skill != null &&
               skill.resourceType == SkillResourceType.Bullet &&
               skill.bulletShortageMode == BulletShortageMode.AllowPartialUse &&
               GetEffectiveProjectileCount(skill) == Mathf.Max(0, skill.consumeAmount);
    }
}
